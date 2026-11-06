"""VehicleMount and Object parsers: vehicles with printed statistics, and objects with a stat block.

Vehicles (`parse_vehicles`), in the 5etools `vehicles.json` shapes:

- a ship statistics table ("Ship | Cost | Speed | Crew | Passengers | Cargo (tons) | AC | HP | Damage Threshold",
  the DMG "Airborne and Waterborne Vehicles"): each labelled row is a SHIP with `cost` (copper), `pace` (miles per
  hour), `capCrew`, `capPassenger`, `capCargo` (tons) and `hull` {ac, hp, dt}. A printed dash leaves its field
  absent. The OCR drops dashes in the right-hand cells, so an incomplete row's numbers are placed by their x
  position against the columns of the table's complete rows; an ambiguous placement fails `vehicle-columns-placed`.
  A row whose label the OCR lost is not emitted: no name is taken from anywhere else.
- an object statistics block that prints a Speed ("The apparatus of Kwalish is a Large object with the following
  statistics: Armor Class: 20 ...") is an OBJECT vehicle with `size`, `ac`, `hp`, `speed` and `immune`.
- a heading whose prose states a crew and a speed ("operated by a crew of three and carries up to a dozen
  passengers ... a top speed of 15 miles per hour", the MTF githyanki ships): `capCrew`, `capPassenger` (only an
  "up to N passengers" figure; "more than a hundred" is not one), `pace` and `dimensions` (length, width) as
  stated. `vehicleType` is absent: the prose prints no kind.

Objects (`parse_objects`): a heading over "<Size> object" and "Armor Class: N" lines (the DMG siege equipment).
Fields: `ac`, `hp` {average}, `immune` / `resist` / `vulnerable` / `conditionImmune`, `description` (the prose
paragraphs) and `action` (each "Name. ..." lead-in paragraph). A block that prints a Speed is a vehicle instead.

Neither parser is registered in assemble yet (Phase 2b ruling P1).
"""
from __future__ import annotations

import re
from statistics import median

from ..checks import Check
from ..layout import fix_digits
from ..model import Line, Page
from . import ParseContext, Parsed
from .charcommon import SENTENCE_END, heading_like, indented, squash, tidy_case, title_case
from .equipment import cost_check, cost_value, is_heading, weight_value
from .monster import _after_label, _damage_list, _is_heading_caps, _join_text, _lab, _speed_field, _title

_SIZES = {"tiny": "T", "small": "S", "medium": "M", "large": "L", "huge": "H", "gargantuan": "G"}
_SIZE_WORD = r"(Tiny|Small|Medium|Large|Huge|Gargantuan)"
_OBJECT_LINE = re.compile(rf"^{_SIZE_WORD} object$", re.I)
_STATS_LEAD = re.compile(rf"^(?:The |An? )?(?P<name>.+?) is an? {_SIZE_WORD} object with the following statistics:$",
                         re.I)
_STAT_FIELDS = ["Armor Class", "Hit Points", "Speed", "Damage Vulnerabilities", "Damage Resistances",
                "Damage Immunities", "Condition Immunities", "Damage Threshold"]
_DAMAGE_TYPES = {"acid", "bludgeoning", "cold", "fire", "force", "lightning", "necrotic", "piercing", "poison",
                 "psychic", "radiant", "slashing", "thunder"}
_CONDITIONS = {"blinded", "charmed", "deafened", "exhaustion", "frightened", "grappled", "incapacitated",
               "invisible", "paralyzed", "petrified", "poisoned", "prone", "restrained", "stunned", "unconscious"}
_LIST_KEYS = {"Damage Vulnerabilities": "vulnerable", "Damage Resistances": "resist", "Damage Immunities": "immune",
              "Condition Immunities": "conditionImmune"}
_ACTION = re.compile(r"^(?P<name>[A-Z][A-Za-z'’-]*(?: [A-Z][A-Za-z'’-]*){0,3})\.\s+(?P<rest>\S.*)$")
_INT = re.compile(r"^\d+$")
_DASH = re.compile(r"^[-–—]+$")


def _set(fields: dict, key: str, value) -> None:
    if value is not None:
        fields[key] = value


def _number(tok: str) -> float | int | None:
    """A printed count or measure: "150", "1/2", "11/2" (the OCR of "1½")."""
    return weight_value(f"{tok} lb.") if re.fullmatch(r"\d+(?:/\d+)?|\d+\.\d+", tok) else None


# ---------- ship statistics tables ----------
_SHIP_COLUMNS = [("crew", r"crew"), ("passengers", r"passengers?"), ("cargo", r"cargo(?:\s*\(tons\))?"),
                 ("ac", r"ac"), ("hp", r"hp"), ("dt", r"damage\s+threshold")]


def _yc(l: Line) -> float:
    return (l.box[1] + l.box[3]) / 2


def _band(page: Page, l: Line) -> list[Line]:
    """The OCR lines on `l`'s printed row (a table row is often read as two or more lines), left to right."""
    return sorted((o for o in page.lines if abs(_yc(o) - _yc(l)) <= 0.5 * l.height), key=lambda o: o.box[0])


def _tokens(lines: list[Line]) -> list[tuple[str, float]]:
    """Each word with its estimated x centre (by character offset inside its OCR line)."""
    out = []
    for l in lines:
        per = (l.box[2] - l.box[0]) / max(len(l.text), 1)
        for m in re.finditer(r"\S+", l.text):
            out.append((m.group(0), l.box[0] + (m.start() + m.end()) / 2 * per))
    return out


def _ship_header(page: Page, l: Line) -> list[str] | None:
    """The value columns after Speed when `l` is the first cell of a "<label> Cost Speed Crew ..." header row."""
    band = _band(page, l)
    if band[0] is not l:
        return None
    text = " ".join(o.text for o in band).lower()
    m = re.match(r"^\S+\s+cost\s+speed\b(.*)$", text)
    if not m:
        return None
    cols = []
    for key, pat in _SHIP_COLUMNS:
        c = re.search(rf"\b{pat}\b", m.group(1))
        if c:
            cols.append((c.start(), key))
    keys = [k for _, k in sorted(cols)]
    return keys if {"crew", "ac", "hp"} <= set(keys) else None


def _place(vals: list[tuple[str, float]], centres: list[float]) -> tuple[list[int], float, float]:
    """Order-preserving assignment of `vals` to columns by x: (column of each value, its cost, the next best cost)."""
    n, m = len(vals), len(centres)
    best: list[tuple[float, list[int]]] = []

    def go(i: int, c: int, cost: float, cols: list[int]) -> None:
        if i == n:
            best.append((cost, cols))
            return
        for k in range(c, m - (n - i) + 1):
            go(i + 1, k + 1, cost + abs(vals[i][1] - centres[k]), cols + [k])

    go(0, 0, 0.0, [])
    best.sort(key=lambda b: b[0])
    return best[0][1], best[0][0], best[1][0] if len(best) > 1 else float("inf")


def _ship_rows(page: Page) -> list[tuple[str, list[tuple[str, float]], list[Line], list[str]]]:
    """(label, value tokens after the speed, lines, value columns) per printed row; the label may be empty."""
    out = []
    for head in sorted(page.lines, key=lambda l: l.box[1]):
        cols = _ship_header(page, head)
        if not cols:
            continue
        h = head.height
        below = sorted((l for l in page.lines if _yc(l) > head.box[3] and l.box[0] >= head.box[0] - 2 * h),
                       key=lambda l: l.box[1])
        seen: set[int] = set()
        last = head.box[3]
        for l in below:
            if id(l) in seen:
                continue
            band = [o for o in _band(page, l) if o.box[0] >= head.box[0] - 2 * h]
            seen.update(id(o) for o in band)
            toks = _tokens(band)
            words = [t for t, _ in toks]
            k = next((i for i in range(len(words) - 3) if re.fullmatch(r"[\d,]+", words[i]) and
                      re.fullmatch(r"(cp|sp|ep|gp|pp)\.?", words[i + 1], re.I) and
                      words[i + 3].lower().startswith("mph")), None)
            if k is None or l.box[1] - last > 3 * h:
                break
            last = max(o.box[3] for o in band)
            out.append((" ".join(words[:k]), [(f"{words[k]} {words[k + 1]}", 0.0), (words[k + 2], 0.0)] + toks[k + 4:],
                        band, cols))
    return out


def _ship(label: str, toks: list[tuple[str, float]], lines: list[Line], cols: list[str],
          centres: list[float] | None) -> Parsed:
    cost_tok, speed_tok, vals = toks[0][0], toks[1][0], toks[2:]
    vals = [(fix_digits(t), x) for t, x in vals]
    checks = [cost_check(cost_tok, label)]
    placed: list[int] | None = list(range(len(vals))) if len(vals) == len(cols) else None
    if placed is None:
        if centres and len(vals) < len(cols):
            placed, cost, second = _place(vals, centres)
            gap = median(b - a for a, b in zip(centres, centres[1:])) if len(centres) > 1 else 0
            if second - cost < 0.5 * gap:
                checks.append(Check("vehicle-columns-placed", False, f"the cells {[t for t, _ in vals]} of "
                                    f"{label!r} could sit under more than one set of columns {cols}"))
            else:
                checks.append(Check("vehicle-columns-placed", True))
        else:
            checks.append(Check("vehicle-columns-placed", False, f"{len(vals)} cells {[t for t, _ in vals]} of "
                                f"{label!r} for {len(cols)} columns {cols}"))
    cells = {cols[c]: t for (t, _), c in zip(vals, placed or [])}
    f: dict = {"vehicleType": "SHIP"}
    _set(f, "cost", cost_value(cost_tok))
    pace = _number(fix_digits(speed_tok))
    bad = [] if pace is not None else [f"speed {speed_tok!r} mph"]
    _set(f, "pace", pace)
    hull: dict = {}
    for key, t in cells.items():
        if _DASH.match(t):
            continue
        v = _number(t)
        if v is None or (key != "cargo" and not isinstance(v, int)):
            bad.append(f"{key} {t!r}")
            continue
        if key == "crew":
            f["capCrew"] = v
        elif key == "passengers":
            f["capPassenger"] = v
        elif key == "cargo":
            f["capCargo"] = v
        else:
            hull[key] = v
    if hull:
        f["hull"] = hull
    checks.append(Check("vehicle-stats-valid", not bad, f"unreadable cells of {label!r}: {', '.join(bad)}"
                        if bad else ""))
    text = f"{label}. " + " ".join(f"{k}: {v}" for k, v in [("Cost", cost_tok), ("Speed", f"{speed_tok} mph")] +
                                   list(cells.items()))
    return Parsed(type="VehicleMount", name=label, fields=f, text=text, lines=list(lines), checks=checks,
                  table_row=True)


def _ship_tables(ctx: ParseContext) -> list[Parsed]:
    out = []
    for n in sorted(ctx.pages):
        rows = _ship_rows(ctx.pages[n])
        if not rows:
            continue
        cols = rows[0][3]
        full = [r[1][2:] for r in rows if len(r[1]) - 2 == len(cols)]
        centres = [median(v[k][1] for v in full) for k in range(len(cols))] if full else None
        for label, toks, lines, cs in rows:
            if label.strip():
                out.append(_ship(label.strip(), toks, lines, cs, centres))
    return out


# ---------- stat blocks ("Large object / Armor Class: 15 / ...") ----------
def _stat_label(t: str) -> str | None:
    head = t.split(":")[0] if ":" in t else t
    return next((f for f in _STAT_FIELDS if _lab(head, f) and len(squash(head)) <= len(squash(f)) + 2), None)


def _block_starts(L: list[Line]) -> list[tuple[int, int, str, str]]:
    """(name line, first stat line, name, size code) per stat block."""
    out = []
    for i, l in enumerate(L):
        t = l.text.strip()
        m = _OBJECT_LINE.match(t)
        if m and i and i + 1 < len(L) and _stat_label(L[i + 1].text) == "Armor Class":
            prev = L[i - 1]
            if prev.page == l.page and not prev.text.rstrip().endswith(SENTENCE_END) and heading_like(prev.text):
                out.append((i - 1, i + 1, _title(prev.text.strip()), _SIZES[m.group(1).lower()]))
            continue
        if i + 1 < len(L):
            m = _STATS_LEAD.match(f"{t} {L[i + 1].text.strip()}")
            if m and i + 2 < len(L) and _stat_label(L[i + 2].text) == "Armor Class":
                out.append((i, i + 2, _lead_name(L, i, m.group("name")), _SIZES[m.group(2).lower()]))
    return out


def _lead_name(L: list[Line], i: int, phrase: str) -> str:
    """The printed heading the statistics sentence names ("apparatus of Kwalish" -> "Apparatus of Kwalish"), or
    the phrase itself with its first letter raised when no heading above it matches."""
    for j in range(i - 1, max(-1, i - 60), -1):
        if squash(L[j].text) == squash(phrase):
            return _title(L[j].text.strip())
    return phrase[:1].upper() + phrase[1:]


def _stats(L: list[Line], s: int) -> tuple[dict[str, str], int]:
    """The stat lines from `s` (label -> value text) and the index after them; a wrapped value line joins."""
    stats: dict[str, str] = {}
    cur = None
    j = s
    while j < len(L):
        t = L[j].text.strip()
        lab = _stat_label(t) if ":" in t[:30] else None
        if lab and lab not in stats:
            cur = lab
            stats[lab] = _after_label(t, lab).lstrip(":").strip()
        elif cur and (t[:1].islower() or t[:1] == "(" or stats[cur].count("(") > stats[cur].count(")")):
            stats[cur] = f"{stats[cur]} {t}"
        else:
            break
        j += 1
    return stats, j


def _block_end(L: list[Line], s: int, stop: int, med: float, same_page: bool) -> int:
    for j in range(s, stop):
        l = L[j]
        t = l.text.strip()
        if l.height > 1.3 * med or (_is_heading_caps(t) and len(t.split()) <= 4):
            return j
        if same_page and (l.page != L[s].page or l.height < 0.8 * med):
            return j
    return stop


def _paragraphs(L: list[Line], s: int, e: int) -> list[str]:
    paras: list[list[str]] = []
    for j in range(s, e):
        if not paras or (indented(L, j) and paras[-1][-1].rstrip().endswith(SENTENCE_END)):
            paras.append([])
        paras[-1].append(L[j].text.strip())
    return [_join_text(p) for p in paras]


def _stat_fields(stats: dict[str, str], name: str) -> tuple[dict, list[str]]:
    f: dict = {}
    bad: list[str] = []
    ac = fix_digits(stats.get("Armor Class", ""))
    if _INT.match(ac):
        f["ac"] = [int(ac)]
    else:
        bad.append(f"Armor Class {stats.get('Armor Class', '')!r}")
    if "Hit Points" in stats:
        hp = fix_digits(stats["Hit Points"])
        m = re.match(r"^(\d+)(?:\s*\((\d+d\d+(?:\s*[+-]\s*\d+)?)\))?$", hp)
        if m:
            f["hp"] = {"average": int(m.group(1)), **({"formula": m.group(2)} if m.group(2) else {})}
        else:
            bad.append(f"Hit Points {stats['Hit Points']!r}")
    else:
        bad.append("Hit Points not printed")
    for label, key in _LIST_KEYS.items():
        if label not in stats:
            continue
        vals = _damage_list(stats[label]) if key != "conditionImmune" else \
            [v.strip() for v in stats[label].split(",") if v.strip()]
        known = _CONDITIONS if key == "conditionImmune" else _DAMAGE_TYPES
        odd = [v for v in vals if isinstance(v, str) and v.lower() not in known]
        if odd:
            bad.append(f"{label} {', '.join(odd)!r}")
        f[key] = vals
    return f, bad


def _blocks(L: list[Line]) -> list[tuple[str, str, dict[str, str], int, int, int, bool]]:
    """(name, size, stats, name line, content start, content end, from a statistics sentence)."""
    starts = _block_starts(L)
    med = median(l.height for l in L) if L else 0
    out = []
    for k, (n, s, name, size) in enumerate(starts):
        stats, c = _stats(L, s)
        stop = starts[k + 1][0] if k + 1 < len(starts) else len(L)
        lead = not _OBJECT_LINE.match(L[n + 1].text.strip()) if n + 1 < len(L) else True
        out.append((name, size, stats, n, c, _block_end(L, c, stop, med, lead), lead))
    return out


def parse_objects(body: list[Line], ctx: ParseContext) -> list[Parsed]:
    out = []
    for name, size, stats, n, c, e, _ in _blocks(body):
        if "Speed" in stats:
            continue
        f, bad = _stat_fields(stats, name)
        desc, actions = [], []
        for para in _paragraphs(body, c, e):
            m = _ACTION.match(para)
            if m and (actions or desc):
                actions.append({"name": m.group("name"), "entries": [m.group("rest")]})
            elif actions:
                actions[-1]["entries"].append(para)
            else:
                desc.append(para)
        if desc:
            f["description"] = "\n".join(desc)
        if actions:
            f["action"] = actions
        checks = [Check("object-stats-valid", not bad, f"unreadable statistics of {name!r}: {'; '.join(bad)}"
                        if bad else "")]
        text = f"{name}. " + " ".join(l.text.strip() for l in body[n + 1:e])
        out.append(Parsed(type="Object", name=name, fields=f, text=text, lines=body[n:e], checks=checks))
    return out


# ---------- prose vehicles ----------
_NUMBER_WORDS = {"one": 1, "two": 2, "three": 3, "four": 4, "five": 5, "six": 6, "seven": 7, "eight": 8, "nine": 9,
                 "ten": 10, "eleven": 11, "twelve": 12, "dozen": 12, "fifteen": 15, "twenty": 20, "thirty": 30,
                 "forty": 40, "fifty": 50, "sixty": 60, "hundred": 100}
_CREW = re.compile(r"\bcrew of (\w+)\b", re.I)
_PACE = re.compile(r"\b(\d+) miles per hour\b", re.I)
_PASSENGERS = re.compile(r"\bup to (?:a )?(\w+) passengers\b", re.I)
_LONG_WIDE = re.compile(r"\b(\d+) feet long and (\d+) feet wide\b", re.I)
_WIDE_LONG = re.compile(r"\b(\d+) feet wide and (\d+) feet long\b", re.I)


def _count(w: str) -> int | None:
    return int(w) if w.isdigit() else _NUMBER_WORDS.get(w.lower())


def _prose_heading(L: list[Line], i: int) -> bool:
    t = L[i].text.strip()
    return heading_like(t) and (title_case(t) or _is_heading_caps(t)) and \
        (i == 0 or L[i - 1].text.rstrip().endswith(SENTENCE_END))


def _prose_vehicles(L: list[Line]) -> list[Parsed]:
    heads = [i for i in range(len(L)) if _prose_heading(L, i)]
    out = []
    for k, h in enumerate(heads):
        e = heads[k + 1] if k + 1 < len(heads) else len(L)
        if e - h > 25 or e - h < 3:
            continue
        paras = _paragraphs(L, h + 1, e)
        text = " ".join(paras)
        crew, pace = _CREW.search(text), _PACE.search(text)
        if not (crew and pace and _count(crew.group(1))):
            continue
        f: dict = {"capCrew": _count(crew.group(1))}
        pas = _PASSENGERS.search(text)
        if pas:
            _set(f, "capPassenger", _count(pas.group(1)))
        f["pace"] = int(pace.group(1))
        lw, wl = _LONG_WIDE.search(text), _WIDE_LONG.search(text)
        if lw:
            f["dimensions"] = [f"{lw.group(1)} ft.", f"{lw.group(2)} ft."]
        elif wl:
            f["dimensions"] = [f"{wl.group(2)} ft.", f"{wl.group(1)} ft."]
        f["entries"] = paras
        name = tidy_case(_title(L[h].text.strip())) if _is_heading_caps(L[h].text.strip()) else \
            tidy_case(L[h].text.strip())
        out.append(Parsed(type="VehicleMount", name=name, fields=f, text=f"{name}. {text}", lines=L[h:e],
                          checks=[Check("vehicle-stats-valid", True)]))
    return out


_SHIP_LINE = re.compile(r"^(?P<label>[A-Z][A-Za-z ]*?):\s*(?P<value>\S.*)$")


def _stat_line_ships(L: list[Line]) -> list[Parsed]:
    """A heading over "Purchase Cost: / Speed: N mph / Maximum Cargo: N tons / Crew: / Maximum Passenger
    Occupancy:" lines (EGW Skyship): a SHIP with `cost`, `pace`, `capCargo`, `capPassenger` and `capCrew`. A crew
    printed as a range ("6-10") sets no `capCrew`; a passing `crew-range` check records it. The other lines
    (travel and shipping costs) are its `entries`."""
    out: list[Parsed] = []
    for i in range(1, len(L)):
        if not re.match(r"^Purchase Cost:", L[i].text.strip()) or not is_heading(L, i - 1):
            continue
        stats: list[tuple[str, str]] = []
        j = i
        while j < len(L) and L[j].page == L[i].page and (m := _SHIP_LINE.match(L[j].text.strip())):
            stats.append((m.group("label").strip().lower(), m.group("value").strip()))
            j += 1
        st = dict(stats)
        name = _title(L[i - 1].text.strip())
        f: dict = {"vehicleType": "SHIP"}
        bad: list[str] = []
        checks = [cost_check(st["purchase cost"], name)]
        _set(f, "cost", cost_value(st["purchase cost"]))
        speed = re.fullmatch(r"(\d+)\s*mph", fix_digits(st.get("speed", "")))
        if speed:
            f["pace"] = int(speed.group(1))
        elif "speed" in st:
            bad.append(f"Speed {st['speed']!r}")
        cargo = re.fullmatch(r"(\d+(?:\.\d+)?)\s*tons?", fix_digits(st.get("maximum cargo", "")))
        if cargo:
            f["capCargo"] = _number(cargo.group(1))
        elif "maximum cargo" in st:
            bad.append(f"Maximum Cargo {st['maximum cargo']!r}")
        pax = st.get("maximum passenger occupancy", "")
        if re.fullmatch(r"\d+", fix_digits(pax)):
            f["capPassenger"] = int(fix_digits(pax))
        elif pax:
            bad.append(f"Maximum Passenger Occupancy {pax!r}")
        crew = fix_digits(st.get("crew", ""))
        if re.fullmatch(r"\d+", crew):
            f["capCrew"] = int(crew)
        elif re.fullmatch(r"\d+\s*[-–]\s*\d+", crew):
            checks.append(Check("crew-range", True, f"the crew of {name!r} is a range ({crew}): no capCrew"))
        elif crew:
            bad.append(f"Crew {crew!r}")
        known = {"purchase cost", "speed", "maximum cargo", "maximum passenger occupancy", "crew"}
        ents = [L[k].text.strip() for k in range(i, j) if stats[k - i][0] not in known]
        if ents:
            f["entries"] = ents
        checks.append(Check("vehicle-stats-valid", not bad, f"unreadable statistics of {name!r}: {'; '.join(bad)}"
                            if bad else ""))
        out.append(Parsed(type="VehicleMount", name=name, fields=f,
                          text="\n".join([name] + [L[k].text.strip() for k in range(i, j)]), lines=L[i - 1:j],
                          checks=checks))
    return out


def parse_vehicles(body: list[Line], ctx: ParseContext) -> list[Parsed]:
    out = _ship_tables(ctx)
    for name, size, stats, n, c, e, _ in _blocks(body):
        if "Speed" not in stats:
            continue
        sf, bad = _stat_fields(stats, name)
        f = {"vehicleType": "OBJECT", "size": size, **{k: v for k, v in sf.items() if k in ("ac", "hp", "immune")}}
        speed = _speed_field(stats["Speed"])
        if speed:
            f["speed"] = speed
        else:
            bad.append(f"Speed {stats['Speed']!r}")
        paras = _paragraphs(body, c, e)
        if paras:
            f["entries"] = paras
        checks = [Check("vehicle-stats-valid", not bad, f"unreadable statistics of {name!r}: {'; '.join(bad)}"
                        if bad else "")]
        text = f"{name}. " + " ".join(l.text.strip() for l in body[n:e])
        out.append(Parsed(type="VehicleMount", name=name, fields=f, text=text, lines=body[n:e], checks=checks))
    out += _prose_vehicles(body)
    out += _stat_line_ships(body)
    return out
