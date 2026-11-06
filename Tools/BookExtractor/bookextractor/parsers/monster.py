"""Monster stat-block parser (Monster Manual layout) and its verification checks."""
from __future__ import annotations

import re
import statistics
from copy import deepcopy

from rapidfuzz import fuzz, process

from ..checks import XP_BY_CR, Check, hp_con_check, hp_dice_check
from ..layout import fix_digits
from ..model import Line
from . import Parsed

SIZES = ["Tiny", "Small", "Medium", "Large", "Huge", "Gargantuan"]
SIZE_CODE = {"Tiny": "T", "Small": "S", "Medium": "M", "Large": "L", "Huge": "H", "Gargantuan": "G"}
FIELDS = ["Armor Class", "Hit Points", "Speed", "Saving Throws", "Skills", "Damage Vulnerabilities",
          "Damage Resistances", "Damage Immunities", "Condition Immunities", "Senses", "Languages", "Challenge"]
SECTIONS = {"actions": "action", "reactions": "reaction", "legendary actions": "legendary",
            "lair actions": "lair", "bonus actions": "bonus"}
# "alignment" covers a borrowed one ("Large construct, as the eidolon's alignment")
ALIGN = re.compile(r"\b(lawful|chaotic|neutral|unaligned|any|good|evil|alignment)\b", re.I)
ABILITIES = ["str", "dex", "con", "int", "wis", "cha"]
_HEADER = "STR DEX CON INT WIS CHA"
_PAIR = re.compile(r"(\d{1,3})[\s.,;:]*\(\s*([+-]{0,2})\s*(\d{1,2})\s*[)}\]]")
# a score whose leading digit(s) OCR'd as junk ("$7+3)", ".4:(-3)"): junk, ones digit, optional "(", signed modifier
_GARBLED = re.compile(r"(?<![\w(])[^\w\s(]{1,2}(\d)[^\w\s+\-]{0,2}\(?\s*([+-])(\d{1,2})\s*[)}\]]")
_ABILITY_TOKEN = re.compile(_PAIR.pattern + "|" + _GARBLED.pattern)
_SMALL = r"(?:of|the|and|to|with|a|an|in|on|or|for|from|by|at)"
_ENTRY = re.compile(r"(?:(?<=^)|(?<=[.)] ))([A-Z][A-Za-z'\-]*(?: (?:[A-Z0-9][A-Za-z'\-/]*|" + _SMALL
                    + r")){0,5}(?: \([^)]{1,40}\))?)\. ")
_LEGAL_CR = set(XP_BY_CR)


# ---------- small helpers ----------
def _lab(t: str, f: str) -> bool:
    """Fuzzy 'line starts with label f' (OCR noise like 'Annor Class', 'Hit Poi nts')."""
    k = re.sub(r"[^a-z]", "", t.lower())
    fk = f.replace(" ", "").lower()
    return k.startswith(fk) or fuzz.ratio(k[: len(fk)], fk) >= 85


def _after_label(t: str, f: str) -> str:
    """Drop the label by letter count, so a split label ('Hit Poi nts') is still removed whole."""
    need = len(f.replace(" ", ""))
    seen = 0
    for i, ch in enumerate(t):
        if ch.isalpha():
            seen += 1
            if seen == need:
                return t[i + 1:].strip()
    return ""


def _size_of(t: str) -> str | None:
    w = t.split()[0] if t.split() else ""
    hit = process.extractOne(w, SIZES, score_cutoff=50)
    return hit[0] if hit else None


CREATURE_TYPES = ["aberration", "beast", "celestial", "construct", "dragon", "elemental", "fey", "fiend",
                  "giant", "humanoid", "monstrosity", "ooze", "plant", "undead"]
# VRGR/TCE print no alignment: "Medium Monstrosity (Shapechanger)", "Large or Huge Undead"
_SIZE_WORD = r"(?:tiny|small|medium|large|huge|gargantuan)"
_BARE_SIZE_LINE = re.compile(rf"^{_SIZE_WORD}(?: or {_SIZE_WORD})? (?:swarm of {_SIZE_WORD} )?([a-z]+)(?: \([^)]*\))?\.?$",
                             re.I)
_GLUED_SIZE = re.compile(r"^(Tiny|Small|Medium|Large|Huge|Gargantuan)(?=[A-Z])")


def _spaced(t: str) -> str:
    """Restore the space OCR lost after the size word ('TinyMonstrosity')."""
    return _GLUED_SIZE.sub(r"\1 ", t.strip())


def _singular(w: str) -> str:
    """'Monstrosities' -> 'monstrosity', 'Beasts' -> 'beast'; 'undead' stays."""
    w = w.lower()
    if w.endswith("ies"):
        return w[:-3] + "y"
    return w[:-1] if w.endswith("s") and w[:-1] in CREATURE_TYPES else w


def _is_size_line(t: str) -> bool:
    """"<Size> <type>, <alignment>" — not merely any line with "evil" in it ("Fire Giant of Evil Fire") —
    or the whole line "<Size> <creature type> (tags)" with no alignment at all."""
    if "," in t and ALIGN.search(t) and _size_of(t):
        return True
    m = _BARE_SIZE_LINE.match(_spaced(t))
    return bool(m) and bool(process.extractOne(_singular(m.group(1)), CREATURE_TYPES, scorer=fuzz.ratio,
                                               score_cutoff=85))


def _is_ability_header(t: str) -> bool:
    """The STR DEX CON INT WIS CHA header, tolerating OCR noise ('INI', '1NT')."""
    t = t.strip(" .,:;|_-")
    return len(t.split()) == 6 and fuzz.ratio(t.upper(), _HEADER) >= 80


def _is_heading_caps(t: str) -> bool:
    return len(t) > 2 and t.upper() == t and bool(re.search(r"[A-Z]{3}", t)) and not re.search(r"\d", t)


def _join_text(texts: list[str]) -> str:
    s = ""
    for t in texts:
        if s.endswith("-") and t[:1].islower():
            s = s[:-1] + t
        else:
            s = f"{s} {t}" if s else t
    return s


_SMALL_WORDS = {"of", "the", "and", "to", "with", "a", "an", "in", "on", "or", "for", "from", "by", "at"}


def _title(s: str) -> str:
    """Title-case an OCR'd printed name ('GOBLIn BOss' -> 'Goblin Boss') without 'Giant'S' artefacts."""
    def word(w: str, first: bool) -> str:
        if not first and w.lower() in _SMALL_WORDS:
            return w.lower()
        return w[:1].upper() + w[1:].lower()

    out, first = [], True
    for tok in re.split(r"(\s+)", s.strip()):
        if tok.isspace() or not tok:
            out.append(tok)
            continue
        parts = re.split(r"([/\-(])", tok)
        for p in parts:
            out.append(p if p in "/-(" and p else word(p, first and p != ""))
            if p and p not in "/-(":
                first = False
    return "".join(out)


# ---------- locating stat blocks ----------
def _monster_starts(L: list[Line]) -> list[int]:
    """Index of the size/type/alignment line for each stat block (anchored on the Armor Class line)."""
    st: list[int] = []
    for i, l in enumerate(L):
        if _lab(l.text, "Armor Class") and (not st or i - st[-1] > 3):
            for j in (i - 1, i - 2):
                if j >= 0 and _is_size_line(L[j].text):
                    st.append(j)
                    break
    return st


def _rel_heights(L: list[Line]) -> list[float]:
    by_page: dict[int, list[float]] = {}
    for l in L:
        by_page.setdefault(l.page, []).append(l.height)
    med = {p: statistics.median(h) or 1.0 for p, h in by_page.items()}
    return [l.height / med[l.page] for l in L]


def _pick_name(L: list[Line], rel: list[float], i: int) -> int | None:
    """Name = nearest large line above the size line on the same page and column (by position).

    The only fallback is a short capitalised line directly above; never a previous page.
    """
    z = L[i]
    # a heading touching the size line and taller than it is the name, whatever the page median
    # (BGG's big decorative quotes push a real heading under 1.2x the median)
    if i > 0:
        l = L[i - 1]
        gap = z.box[1] - l.box[3]
        overlap = min(l.box[2], z.box[2]) - max(l.box[0], z.box[0])
        if l.page == z.page and -0.3 * z.height <= gap < 0.6 * z.height and overlap > 0 \
                and l.height >= 1.1 * z.height and _heading_like(l.text):
            return i - 1
    best: tuple[float, int] | None = None
    for j in range(max(0, i - 400), min(len(L), i + 400)):
        l = L[j]
        if l.page != z.page or j == i or rel[j] < 1.2 or not _heading_like(l.text):
            continue
        gap = z.box[1] - l.box[3]
        overlap = min(l.box[2], z.box[2]) - max(l.box[0], z.box[0])
        if -0.3 * z.height <= gap < 6 * z.height and overlap > 0 and (best is None or gap < best[0]):
            best = (gap, j)
    if best:
        return best[1]
    for j in range(i - 1, max(i - 4, -1), -1):
        l = L[j]
        t = l.text
        if l.page == z.page and _heading_like(t) and not t.endswith(".") and not t[:1].islower():
            return j
    return None


def _heading_like(t: str) -> bool:
    """Short, starts with a letter (never a "—Bigby" quote attribution), not the alignment line."""
    t = t.strip()
    return bool(t) and t[0].isalpha() and len(t.split()) <= 6 and not _is_size_line(t)


def _heading_lines(L: list[Line], rel: list[float], nj: int) -> list[int]:
    """The name line plus up to two lines directly above it that continue the same heading
    ("GITHYANKI SUPREME" over "COMMANDER"): same page and column, heading-sized, height within 20%,
    touching (no paragraph gap), short and not a sentence."""
    top = nj
    for _ in range(2):
        k = top - 1
        if k < 0:
            break
        a, b = L[k], L[top]
        t = a.text.strip()
        if a.page != b.page or rel[k] < 1.2 or abs(a.height - b.height) > 0.2 * b.height:
            break
        if _is_size_line(t) or len(t.split()) > 6 or t.endswith((".", ":", ",")) or t[:1].islower():
            break
        gap = b.box[1] - a.box[3]
        overlap = min(a.box[2], b.box[2]) - max(a.box[0], b.box[0])
        if not (-0.3 * b.height <= gap < 0.5 * b.height and overlap > 0):
            break
        top = k
    return list(range(top, nj + 1))


# ---------- field conversion ----------
def _int(s: str | None) -> int | None:
    m = re.match(r"\s*(\d+)", fix_digits(s or ""))
    return int(m.group(1)) if m else None


def _split_top(s: str, sep: str = ",") -> list[str]:
    out, depth, cur = [], 0, ""
    for ch in s:
        depth += (ch == "(") - (ch == ")")
        if ch == sep and depth <= 0:
            out.append(cur.strip())
            cur = ""
        else:
            cur += ch
    if cur.strip():
        out.append(cur.strip())
    return out


def _alignment(s: str) -> list[str]:
    low = s.lower().strip().rstrip(".")
    if low == "unaligned":
        return ["U"]
    if low == "any alignment":
        return ["A"]
    words = re.findall(r"[a-z]+", low)
    codes = {"lawful": "L", "neutral": "N", "chaotic": "C", "good": "G", "evil": "E"}
    if words and all(w in codes for w in words) and len(words) <= 2:
        return [codes[w] for w in words]
    return [s.strip()] if s.strip() else []


def _type_field(head: str) -> str | dict:
    h = " ".join(head.split()[1:])
    sw = re.match(r"swarm of (\w+) (\w+)(?: \(.*\))?$", h, re.I)
    if sw:
        return _singular(sw.group(2))  # schema allows no swarm-size key; the swarm wording stays in Parsed.text
    m = re.match(r"([A-Za-z\-]+)(?:\s*\((.*)\))?$", h)
    if not m:
        return h
    tags = [t.strip() for t in m.group(2).split(",")] if m.group(2) else []
    return {"type": m.group(1).lower(), "tags": tags} if tags else m.group(1).lower()


def _ac_field(raw: str) -> list:
    raw = fix_digits(raw)
    n = _int(raw)
    if n is None:
        return []
    m = re.match(r"\s*\d+\s*\((.*)\)", raw)
    if not m:
        return [n]
    inner = m.group(1).strip()
    if re.search(r"\bwith\b", inner):
        return [{"ac": n, "condition": inner}]
    return [{"ac": n, "from": [p.strip() for p in inner.split(",") if p.strip()]}]


def _speed_field(raw: str) -> dict:
    out: dict = {}
    raw = fix_digits(raw)
    for part in _split_top(raw):
        m = re.match(r"(?:(walk|burrow|climb|fly|swim)\s+)?(\d+)\s*ft\.?(.*)", part, re.I)
        if not m:
            continue
        mode = (m.group(1) or "walk").lower()
        out[mode] = int(m.group(2))
        if "hover" in m.group(3).lower():
            out["canHover"] = True
    return out


def _bonus_map(raw: str, saves: bool) -> dict:
    out: dict = {}
    for part in _split_top(raw):
        m = re.match(r"([A-Za-z ]+?)\s*([+-]\s*\d+)$", fix_digits(part))
        if m:
            k = m.group(1).strip().lower()
            out[k[:3] if saves else k] = m.group(2).replace(" ", "")
    return out


def _damage_list(raw: str) -> list:
    res: list = []
    for grp in [g.strip() for g in raw.split(";") if g.strip()]:
        if re.search(r"nonmagical|from ", grp, re.I):
            m = re.match(r"(.*?)\s+(from .*)$", grp, re.I)
            kinds = m.group(1) if m else grp
            note = m.group(2) if m else ""
            res.append({"immune": [k.strip() for k in re.split(r",\s*(?:and\s+)?|\s+and\s+", kinds) if k.strip()],
                        "note": note, "cond": True})
        else:
            res.extend(k.strip() for k in grp.split(",") if k.strip())
    return res


def _simple_list(raw: str) -> list[str]:
    return [p.strip() for p in re.split(r",|;", raw) if p.strip()]


def _senses(raw: str) -> tuple[list[str], int | None]:
    raw = fix_digits(raw)
    passive = None
    out: list[str] = []
    for p in _simple_list(raw):
        m = re.match(r"passive Perception (\d+)", p, re.I)
        if m:
            passive = int(m.group(1))
        else:
            out.append(p)
    return out, passive


def _split_entries(text: str) -> list[tuple[str | None, str]]:
    ms = list(_ENTRY.finditer(text))
    if not ms:
        return [(None, text)] if text else []
    res: list[tuple[str | None, str]] = []
    if ms[0].start() > 0:
        res.append((None, text[: ms[0].start()].strip()))
    for a, b in zip(ms, ms[1:] + [None]):
        res.append((a.group(1), text[a.end(): b.start() if b else len(text)].strip()))
    return res


# ---------- ability rows ----------
def _garbled_score(ones: str, sign: str, mod: str) -> tuple[int, int] | None:
    """The one score 1..30 ending in `ones` whose modifier is the printed one (None if not unique)."""
    m = int(f"{sign}{mod}")
    hits = [v for v in range(1, 31) if v % 10 == int(ones) and (v - 10) // 2 == m]
    return (hits[0], m) if len(hits) == 1 else None


def _ability_tokens(text: str) -> list[tuple[int, int, bool, str | None]]:
    out: list[tuple[int, int, bool, str | None]] = []
    for m in _ABILITY_TOKEN.finditer(text):
        if m.group(1) is not None:
            a, s, md = m.group(1), m.group(2), m.group(3)
            out.append((int(a), int(f"{s[-1:]}{md}"), bool(s), None))
        else:
            g = _garbled_score(m.group(4), m.group(5), m.group(6))
            if g:
                out.append((g[0], g[1], True,
                            f"score read {m.group(0)!r}, repaired to {g[0]} ({g[1]:+d}) from its printed modifier"))
    return out


def ability_pairs(lines: list[Line]) -> list[tuple[int, int, bool, str | None]]:
    """Six (score, modifier, signed, repair note) tuples read from the one or two lines after the STR..CHA header.

    A row may be split across two lines, so both lines are joined before matching.
    """
    for i, l in enumerate(lines):
        if _is_ability_header(l.text):
            joined = fix_digits(" ".join(x.text for x in lines[i + 1: i + 3]))
            pairs = _ability_tokens(joined)
            if len(pairs) >= 6:
                return pairs[:6]
            one = fix_digits(lines[i + 1].text) if i + 1 < len(lines) else ""
            return _ability_tokens(one)
    return []


def repair_abilities(pairs: list[tuple[int, int, bool, str | None]]) -> list[tuple[int, int, bool, str | None]]:
    """Fix the corroborated OCR misread: a leading 1 read as 7 (73 (+1) -> 13).

    Only when the score disagrees with its printed, signed modifier and swapping the leading 7 for 1
    gives a score whose modifier matches. When the modifier's own 1 was read as 7 too (73 (+7)), the pair
    is repaired only if swapping 7 -> 1 in both makes them agree. Anything else is left for the
    ability-modifiers check.
    """
    out: list[tuple[int, int, bool, str | None]] = []
    for k, (a, m, signed, note) in zip(ABILITIES, pairs):
        if signed and (a - 10) // 2 != m and len(str(a)) >= 2 and str(a).startswith("7"):
            r = int("1" + str(a)[1:])
            m2 = int(f"{'-' if m < 0 else '+'}{str(abs(m)).replace('7', '1')}")
            if 1 <= r <= 30 and (r - 10) // 2 == m:
                note = f"{k.upper()} read {a} ({m:+d}), repaired to {r}"
                a = r
            elif 1 <= r <= 30 and m2 != m and (r - 10) // 2 == m2:
                note = f"{k.upper()} read {a} ({m:+d}), repaired to {r} ({m2:+d})"
                a, m = r, m2
        elif signed and (a - 10) // 2 != m and "7" in str(abs(m)):
            # the modifier's 1 read as 7 (12 (+7), 9 (-7)): repaired only when the swap makes the score fit
            m2 = int(f"{'-' if m < 0 else '+'}{str(abs(m)).replace('7', '1')}")
            if (a - 10) // 2 == m2:
                note = f"{k.upper()} modifier read {a} ({m:+d}), repaired to {m2:+d}"
                m = m2
            elif note:
                note = f"{k.upper()} {note}"
        elif note:
            note = f"{k.upper()} {note}"
        out.append((a, m, signed, note))
    return out


def _challenge_pairs(lines: list[Line]) -> list[tuple[str, int]]:
    """Every '<cr> (<xp> XP)' printed in the Challenge line and its continuation line(s)."""
    out: list[tuple[str, int]] = []
    for i, l in enumerate(lines):
        if _lab(l.text, "Challenge"):
            j = i
            while j < len(lines) and j < i + 3:
                t = fix_digits(lines[j].text)
                if j > i and not re.match(r"[\d/]+\s*\(", t):
                    break
                out += [(c, int(x.replace(",", ""))) for c, x in
                        re.findall(r"([\d/]+)\s*\(\s*([\d,]+)\s*XP\)", t, re.I)]
                j += 1
            if out:
                return out
    return out


# ---------- the parser ----------
def _section_key(t: str) -> str | None:
    """'Actions', 'Legendary Actions', ... (fuzzy) -> the body section key."""
    if len(t.split()) <= 3 and not t.endswith("."):
        hit = process.extractOne(t.lower(), list(SECTIONS), scorer=fuzz.ratio, score_cutoff=85)
        return SECTIONS[hit[0]] if hit else None
    return None


def _leaves_block(L: list[Line], rel: list[float], j: int, end: int) -> bool:
    """After a paragraph-sized gap in the same column, a paragraph that cannot continue the stat block ends it.

    Every stat-block paragraph after the fields opens with a "Name." lead-in set flush with the block. A new
    sentence after a finished one, going on for two or more stacked lines, without one (EGW's adventure text
    below Yinra Emberwind) or with one indented like a host's paragraph (DMG "Golden Lions (Rare)." below the
    giant fly) belongs to the page, unless an attack or an Actions-style heading still follows on this page or
    the next (MM sets lore between a block's two columns).
    """
    if j == 0:
        return False
    a, b = L[j - 1], L[j]
    t = b.text.strip()
    med = b.height / rel[j] if rel[j] else b.height
    gap = b.box[1] - a.box[3]
    overlap = min(a.box[2], b.box[2]) - max(a.box[0], b.box[0])
    if a.page != b.page or overlap <= 0 or gap < med:
        return False
    if _section_key(a.text) or _section_key(t) or _is_ability_header(t) or _PAIR.search(fix_digits(t)):
        return False
    # a sentence cut short by an OCR-dropped line or by art goes on: only a new sentence after a finished one
    if not t[:1].isupper() or not re.search(r"[.!?)\-–—]\s*$", a.text.strip()):
        return False
    # a paragraph goes on below its first line; a lone line (a running foot "166 GOBLINS") is page furniture
    c = L[j + 1] if j + 1 < len(L) else None
    if c is None or c.page != b.page or not -0.3 * med <= c.box[1] - b.box[3] < 0.5 * med \
            or min(b.box[2], c.box[2]) - max(b.box[0], c.box[0]) <= 0:
        return False
    if _LEAD_IN.match(t) and b.box[0] - a.box[0] < 0.5 * b.height:
        return False
    # the block may go on in the next column after the page's text (MM: lore set between a block's halves)
    return not any(L[k].page <= b.page + 1 and (_ATTACK.search(L[k].text) or _section_key(L[k].text))
                   for k in range(j + 1, min(end, len(L))))


_LEAD_IN = re.compile(r"^[A-Z][^.]{0,50}\. ")  # "Curse ofthe Husk. A humanoid ..." (OCR-glued words too)
_ATTACK = re.compile(r"\b(?:Melee|Ranged)(?: or Ranged)? (?:Weapon|Spell) Attack\b", re.I)


def _parse_block(L: list[Line], rel: list[float], i: int, nj: int | None, end: int) -> Parsed:
    st_line = _spaced(L[i].text)
    # first comma outside parentheses separates "size type (tags)" from alignment
    parts = _split_top(st_line)
    head = parts[0] if parts else st_line
    align = ", ".join(parts[1:]) if len(parts) > 1 else ""
    size = _size_of(st_line)
    name = _title(" ".join(L[k].text for k in _heading_lines(L, rel, nj))) if nj is not None else ""
    nj = _heading_lines(L, rel, nj)[0] if nj is not None else None

    j = i + 1
    cur: str | None = None
    vals: dict[str, str] = {}
    abil_done = 0
    ended = False
    while j < end:
        t = L[j].text
        f = next((f for f in FIELDS if _lab(t, f)), None)
        if not f and _leaves_block(L, rel, j, end):
            ended = True
            break
        if f and not (f == "Speed" and "Speed" in vals) and f not in vals:
            cur = f
            vals[f] = _after_label(t, f)
            if f == "Challenge":
                j += 1
                while j < end and re.match(r"[\d/]+\s*\(", fix_digits(L[j].text)):
                    vals[f] += " " + L[j].text
                    j += 1
                break
        elif _is_ability_header(t):
            cur = None
            abil_done = 1
        elif abil_done == 1 and _PAIR.search(fix_digits(t)):
            cur = None
            if len(_PAIR.findall(fix_digits(t))) >= 6:
                abil_done = 2
        elif cur and cur != "Challenge":
            vals[cur] += " " + t
        j += 1
    stat_end = j

    body: dict[str, list[str]] = {"trait": [], "action": [], "bonus": [], "reaction": [], "legendary": [], "lair": []}
    sec = "trait"
    while j < end and not ended:
        t = L[j].text
        key = _section_key(t)
        if key:
            sec = key
        elif _is_heading_caps(t) and rel[j] > 1.0 and len(t.split()) <= 8:
            break
        elif _leaves_block(L, rel, j, end):
            break
        else:
            body[sec].append(t)
        j += 1
    last = j

    fields: dict = {"size": [SIZE_CODE[size]] if size else [], "type": _type_field(head)}
    if align:
        fields["alignment"] = _alignment(align)
    ac = _ac_field(vals.get("Armor Class", ""))
    if ac:
        fields["ac"] = ac
    hpm = re.match(r"\s*(\d+)[:;.,]?(?:\s*\(([^)]*)\))?", fix_digits(vals.get("Hit Points", "")))
    if hpm:
        hp: dict = {"average": int(hpm.group(1))}
        if hpm.group(2):
            raw = re.sub(r"(?<![0-9A-Za-z])T(?=d\d)", "1", re.sub(r"\s+", "", hpm.group(2))).replace("~", "-")
            hp["formula"] = re.sub(r"\s*([+-])\s*", r" \1 ", raw)
        fields["hp"] = hp
    sp = _speed_field(vals.get("Speed", ""))
    if sp:
        fields["speed"] = sp
    pairs = repair_abilities(ability_pairs(L[i:stat_end]))
    if len(pairs) == 6:
        for k, (score, _mod, _signed, _note) in zip(ABILITIES, pairs):
            fields[k] = score
    if "Saving Throws" in vals:
        fields["save"] = _bonus_map(vals["Saving Throws"], True)
    if "Skills" in vals:
        fields["skill"] = _bonus_map(vals["Skills"], False)
    for key, label in (("vulnerable", "Damage Vulnerabilities"), ("resist", "Damage Resistances"),
                       ("immune", "Damage Immunities")):
        if label in vals:
            fields[key] = _damage_list(vals[label])
    if "Condition Immunities" in vals:
        fields["conditionImmune"] = _simple_list(vals["Condition Immunities"])
    if "Senses" in vals:
        sn, passive = _senses(vals["Senses"])
        if sn:
            fields["senses"] = sn
        if passive is not None:
            fields["passive"] = passive
    if "Languages" in vals:
        langs = _simple_list(vals["Languages"])
        if langs and langs != ["-"]:
            fields["languages"] = langs
    crm = re.match(r"\s*([\d/]+)", fix_digits(vals.get("Challenge", "")))
    if crm:
        fields["cr"] = crm.group(1)
    else:
        fields["cr"] = ""

    for sec_name, texts in body.items():
        if not texts:
            continue
        entries = _split_entries(_join_text(texts))
        if sec_name == "lair":
            fields["lair"] = [t for _, t in entries]
            continue
        out: list[dict] = []
        header: list[str] = []
        for nm, tx in entries:
            if nm is None:
                if out:
                    out[-1]["entries"][0] = f'{out[-1]["entries"][0]} {tx}'.strip()
                elif sec_name == "legendary":
                    header.append(tx)
                else:
                    out.append({"name": "", "entries": [tx]})
            else:
                out.append({"name": nm, "entries": [tx]})
        if sec_name == "legendary" and header:
            fields["legendaryHeader"] = header
        if out:
            fields[sec_name] = out

    block = L[(nj if nj is not None else i): last]
    return Parsed(type="Monster", name=name, fields=fields,
                  text="\n".join(l.text for l in block), lines=block)


def _expand_variants(p: Parsed) -> list[Parsed]:
    """A stat block whose Challenge line lists several CRs ('1 (200 XP) for a red, orange, or yellow
    faerie dragon; 2 (450 XP) for a green, ...') becomes one Parsed per listed colour."""
    pairs = _challenge_pairs(p.lines)
    ch_text = ""
    for i, l in enumerate(p.lines):
        if l.text.lower().startswith("challenge"):
            cont = [l.text]
            for x in p.lines[i + 1: i + 3]:
                if not re.match(r"[\d/]+\s*\(", fix_digits(x.text)):
                    break
                cont.append(x.text)
            ch_text = fix_digits(" ".join(cont))
            break
    segs = re.findall(r"([\d/]+)\s*\(\s*[\d,]+\s*XP\)\s*for an?\s+([^;]+?)(?=\s*;|\s+[\d/]+\s*\(|$)", ch_text, re.I)
    if len(pairs) < 2 or not segs or not p.name:
        return [p]
    out: list[Parsed] = []
    for cr, seg in segs:
        toks = [t.strip() for t in re.split(r",\s*(?:or\s+)?|\s+or\s+", seg) if t.strip()]
        colours = [t.split()[0] for t in toks[:-1]]
        last = toks[-1].split()
        colours.append(last[0])
        for c in colours:
            v = Parsed(type="Monster", name=f"{p.name} ({c.title()})", fields=deepcopy(p.fields),
                       text=p.text, lines=p.lines, variant_of=p.name)
            v.fields["cr"] = cr
            out.append(v)
    return out or [p]


def parse_monsters(lines: list[Line]) -> list[Parsed]:
    """Parse every stat block found in `lines` (reading order, pages concatenated)."""
    L = lines
    st = _monster_starts(L)
    rel = _rel_heights(L)
    out: list[Parsed] = []
    for k, i in enumerate(st):
        nj = _pick_name(L, rel, i)
        end = len(L)
        if k + 1 < len(st):
            nn = _pick_name(L, rel, st[k + 1])
            nn = _heading_lines(L, rel, nn)[0] if nn is not None else None
            end = nn if nn is not None and i < nn < st[k + 1] else st[k + 1]
        for p in _expand_variants(_parse_block(L, rel, i, nj, end)):
            p.checks = monster_checks(p)
            out.append(p)
    return out


# ---------- checks ----------
def monster_checks(p: Parsed) -> list[Check]:
    f = p.fields
    hp = f.get("hp") or {}
    cr = f.get("cr") or ""
    pairs = repair_abilities(ability_pairs(p.lines))
    checks: list[Check] = []

    def req(name: str, ok: bool, what: str) -> None:
        checks.append(Check(name, ok, "" if ok else f"{what} missing"))

    req("abilities-present", all(isinstance(f.get(a), int) for a in ABILITIES), "ability scores")
    req("ac-present", bool(f.get("ac")), "armor class")
    req("hp-present", "average" in hp, "hit points")
    req("cr-present", bool(cr), "challenge rating")
    req("speed-present", bool(f.get("speed")), "speed")
    checks.append(hp_dice_check(hp.get("average"), hp.get("formula")))
    checks.append(hp_con_check(hp.get("formula"), f.get("con")))
    checks.append(Check("cr-legal", cr in _LEGAL_CR, "" if cr in _LEGAL_CR else f"{cr!r} is not a legal CR"))

    xps = [x for c, x in _challenge_pairs(p.lines) if c == cr]
    exp = XP_BY_CR.get(cr)
    if exp is None or not xps:
        checks.append(Check("xp-matches-cr", False, f"no printed XP found for CR {cr!r}"))
    else:
        ok = xps[0] == exp or (cr == "0" and xps[0] == 10)
        checks.append(Check("xp-matches-cr", ok, "" if ok else f"CR {cr} is {exp} XP, printed {xps[0]}"))

    # an unreadable sign ("5(3)") is accepted when the magnitude matches the score
    bad = [f"{a} ({m:+d})" for a, m, signed, _n in pairs
           if not 1 <= a <= 30 or ((a - 10) // 2 != m and (signed or abs((a - 10) // 2) != abs(m)))]
    if len(pairs) < 6:
        checks.append(Check("ability-modifiers", False, "fewer than six score/modifier pairs"))
    else:
        checks.append(Check("ability-modifiers", not bad,
                            "" if not bad else "score/modifier mismatch: " + ", ".join(bad)))
    checks += [Check("ability-repaired", True, n) for *_x, n in pairs if n]
    return checks
