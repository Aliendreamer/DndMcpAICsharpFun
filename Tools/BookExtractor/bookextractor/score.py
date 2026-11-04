"""Dev-only scorer: compare an extract output file against the 5etools answer key.

Never imported by the extraction path (assemble, parsers, reader, service); the CLI loads it lazily
for the `score` subcommand only.
"""
from __future__ import annotations

import collections
import json
import re
import statistics
from pathlib import Path

from rapidfuzz import fuzz, process

BOOKS = {"mm": "Monster", "phb": "Spell", "dmg": "MagicItem"}

_ATK = {"mw": "Melee Weapon Attack:", "rw": "Ranged Weapon Attack:", "mw,rw": "Melee or Ranged Weapon Attack:",
        "ms": "Melee Spell Attack:", "rs": "Ranged Spell Attack:", "ms,rs": "Melee or Ranged Spell Attack:"}


def key(n: str | None) -> str:
    return re.sub(r"[^a-z0-9+]", "", (n or "").lower())


def untag(e: str) -> str:
    """Expand 5etools {@tag ...} markup to the plain text the printed book shows."""
    e = re.sub(r"\{@atk ([^}]*)\}", lambda m: _ATK.get(m.group(1), m.group(1)), e)
    e = re.sub(r"\{@hit (-?\d+)\}", lambda m: f"{int(m.group(1)):+d}", e)
    e = re.sub(r"\{@h\}", "Hit: ", e)
    e = re.sub(r"\{@dc (\d+)\}", r"DC \1", e)
    e = re.sub(r"\{@recharge (\d)\}", r"(Recharge \1-6)", e)
    e = re.sub(r"\{@recharge\}", "(Recharge 6)", e)
    return re.sub(r"\{@\w+ ([^|}]*)[^}]*\}", r"\1", e)


def flat(e) -> str:
    if e is None:
        return ""
    if isinstance(e, str):
        return untag(e)
    if isinstance(e, list):
        return " ".join(flat(x) for x in e)
    if isinstance(e, dict):
        out = " ".join(flat(e.get(k)) for k in ("name", "entries", "items", "text", "entry") if e.get(k))
        if e.get("rows"):
            out += " " + " ".join(flat(r) for r in e["rows"])
        return out
    return str(e)


def words(s: str) -> list[str]:
    return re.findall(r"[a-z0-9]+", re.sub(r"(\d)\s*ft\b", r"\1 ft", s.lower()))


def shingles(s: str, n: int = 5) -> set[str]:
    w = words(s)
    return {" ".join(w[i:i + n]) for i in range(len(w) - n + 1)}


def coverage(truth: str, got: str) -> tuple[float | None, float | None]:
    """(share of the truth's 5-word shingles we have, share of our shingles that are truth's)."""
    t, g = shingles(truth), shingles(got)
    inter = len(t & g)
    return (inter / len(t) if t else None), (inter / len(g) if g else None)


# ---------------- item variant collapse ----------------
_COLORS = "Black|Blue|Brass|Bronze|Copper|Gold|Green|Red|Silver|White"


def base_item_name(n: str) -> str:
    """Collapse a 5etools item variant to the base entry the book prints."""
    n = re.sub(r"^\+\d\s+", "", n)
    n = re.sub(r"\s*\([^)]*\)$", "", n)
    n = n.split(",")[0]
    n = re.sub(rf"^({_COLORS}) Dragon Scale Mail$", "Dragon Scale Mail", n)
    n = re.sub(r"^Belt of \w+ Giant Strength$", "Belt of Giant Strength", n)
    n = re.sub(r"^Crystal Ball of .*", "Crystal Ball", n)
    n = re.sub(r"^Potion of \w+ Giant Strength$", "Potion of Giant Strength", n)
    n = re.sub(r"^Ioun Stone.*", "Ioun Stone", n)
    n = re.sub(r"^(Potion|Ring) of \w+ Resistance$", r"\1 of Resistance", n)
    n = re.sub(r"^Scroll of Protection from .*", "Scroll of Protection", n)
    n = re.sub(r"^Manual of \w+ Golems$", "Manual of Golems", n)
    n = re.sub(r"^Potion of (Greater|Superior|Supreme) Healing$", "Potion of Healing", n)
    n = re.sub(r"^Ring of \w+ Elemental Command$", "Ring of Elemental Command", n)
    n = re.sub(r"^(Eye|Hand) of Vecna$", "Eye and Hand of Vecna", n)
    return n


def load_truth(book: str, fivetools_dir: Path) -> list[dict]:
    d = Path(fivetools_dir)
    if book == "mm":
        return [m for m in json.loads((d / "bestiary/bestiary-mm.json").read_text("utf-8"))["monster"]
                if not m.get("_copy")]
    if book == "phb":
        return json.loads((d / "spells/spells-phb.json").read_text("utf-8"))["spell"]
    seen: dict[str, dict] = {}
    for i in json.loads((d / "items.json").read_text("utf-8"))["item"]:
        if i.get("source") == "DMG" and i.get("rarity") not in (None, "none"):
            b = base_item_name(i["name"])
            rec = seen.setdefault(b, dict(i, name=b, _rarities=set()))
            rec["_rarities"].add(i["rarity"])
    return list(seen.values())


# ---------------- truth-side field renderers ----------------
def t_ac(m: dict):
    a = m.get("ac")
    a = (a[0] if a else None) if isinstance(a, list) else a
    return a.get("ac") if isinstance(a, dict) else a


def t_cr(m: dict):
    c = m.get("cr")
    return c.get("cr") if isinstance(c, dict) else c


def _first_int(v):
    if isinstance(v, list):
        v = v[0] if v else None
    if isinstance(v, dict):
        v = v.get("ac")
    return v


def text_monster(m: dict) -> str:
    return flat([m.get(k) for k in ("trait", "action", "reaction", "legendary") if m.get(k)])


def same_time(truth: dict, got) -> bool:
    if not got:
        return False
    g = got[0] if isinstance(got, list) else got
    t = truth["time"][0]
    return g.get("number") == t["number"] and g.get("unit") == t["unit"]


def same_range(truth: dict, got) -> bool:
    if not got:
        return False
    t = truth["range"]
    td, gd = t.get("distance", {}), got.get("distance", {})
    return (t["type"] == got.get("type") and td.get("type") == gd.get("type")
            and td.get("amount") == gd.get("amount"))


def same_components(truth: dict, got) -> bool:
    if not got:
        return False
    c = truth.get("components", {})
    return {k for k in "vsm" if c.get(k)} == {k for k in "vsm" if got.get(k)}


def same_duration(truth: dict, got, concentration) -> bool:
    if not got:
        return False
    t = truth["duration"][0]
    g = got[0] if isinstance(got, list) else got
    if t["type"] != g.get("type"):
        return False
    if bool(t.get("concentration")) != bool(g.get("concentration", concentration)):
        return False
    if t["type"] == "timed":
        td, gd = t.get("duration", {}), g.get("duration", {})
        return td.get("type") == gd.get("type") and td.get("amount") == gd.get("amount")
    return True


def same_rarity(truth: dict, got) -> bool:
    t = truth.get("rarity")
    family = truth.get("_rarities") or {t}
    return got == t or got in family or (got == "varies" and (len(family) > 1 or t == "varies"))


def attunes(v) -> bool:
    return bool(v)


# ---------------- scoring ----------------
def score_record(book: str, fields: dict, t: dict) -> tuple[dict, float | None, float | None]:
    r: dict[str, bool] = {}
    if book == "mm":
        r["ac"] = _first_int(fields.get("ac")) == t_ac(t)
        r["hp"] = (fields.get("hp") or {}).get("average") == t.get("hp", {}).get("average")
        r["cr"] = str(fields.get("cr")) == str(t_cr(t))
        r["abilities"] = [fields.get(k) for k in ("str", "dex", "con", "int", "wis", "cha")] == \
                         [t.get(k) for k in ("str", "dex", "con", "int", "wis", "cha")]
        got = flat([fields.get(k) for k in ("trait", "action", "reaction", "legendary") if fields.get(k)])
        cov, prec = coverage(text_monster(t), got)
    elif book == "phb":
        r["level"] = fields.get("level") == t["level"]
        r["school"] = fields.get("school") == t["school"]
        r["time"] = same_time(t, fields.get("time"))
        r["range"] = same_range(t, fields.get("range"))
        r["components"] = same_components(t, fields.get("components"))
        r["duration"] = same_duration(t, fields.get("duration"), fields.get("concentration"))
        got = flat(fields.get("entries")) + " " + flat(fields.get("entriesHigherLevel"))
        cov, prec = coverage(flat(t.get("entries")) + " " + flat(t.get("entriesHigherLevel")), got)
    else:
        r["rarity"] = same_rarity(t, fields.get("rarity"))
        r["attune"] = attunes(fields.get("reqAttune")) == attunes(t.get("reqAttune"))
        cov, prec = coverage(flat(t.get("entries")), flat(fields.get("entries")))
    return r, cov, prec


def _match(keys: list[str], k: str) -> str | None:
    if k in keys:
        return k
    hit = process.extractOne(k, keys, scorer=fuzz.ratio, score_cutoff=90)
    return hit[0] if hit else None


def score_book(book: str, entities: list[dict], fivetools_dir: Path | str) -> dict:
    """Score extracted entities of this book's type against 5etools. Returns a plain dict."""
    if book not in BOOKS:
        raise ValueError(f"unknown book {book!r}; expected one of {sorted(BOOKS)}")
    truth = load_truth(book, Path(fivetools_dir))
    idx: dict[str, dict] = {}
    for e in entities:
        if e.get("type") == BOOKS[book]:
            name = base_item_name(e["name"]) if book == "dmg" else e["name"]
            idx.setdefault(key(name), e)
    keys = list(idx)
    agg: dict[str, list[int]] = collections.defaultdict(lambda: [0, 0])
    covs: list[float] = []
    precs: list[float] = []
    missing: list[str] = []
    diffs: list[str] = []
    found = 0
    text_ok = [0, 0]
    for t in truth:
        m = _match(keys, key(t["name"]))
        if not m:
            missing.append(t["name"])
            continue
        found += 1
        res, cov, prec = score_record(book, idx[m]["fields"], t)
        for f, ok in res.items():
            agg[f][0] += ok
            agg[f][1] += 1
            if not ok:
                diffs.append(f"{t['name']}: {f} wrong")
        if cov is not None:
            covs.append(cov)
            text_ok[0] += cov >= 0.9
            text_ok[1] += 1
        if prec is not None:
            precs.append(prec)
    return {
        "book": book, "truth": len(truth), "found": found, "extra": len(idx) - found,
        "fields": {f: {"pct": 100 * a / n, "n": n} for f, (a, n) in agg.items() if n},
        "textAtLeast90": {"pct": 100 * text_ok[0] / text_ok[1] if text_ok[1] else 0.0, "n": text_ok[1]},
        "textCoverageMedian": 100 * statistics.median(covs) if covs else 0.0,
        "textPrecisionMedian": 100 * statistics.median(precs) if precs else 0.0,
        "missing": missing, "diffs": diffs,
    }


def format_table(res: dict) -> str:
    fields = list(res["fields"])
    head = f"{'found':>9} {'extra':>6} " + " ".join(f"{f:>11}" for f in fields) + \
        "  text>=90%  text-median  text-precision"
    cells = " ".join(f"{res['fields'][f]['pct']:>6.0f}% ({res['fields'][f]['n']})".rjust(11) for f in fields)
    row = (f"{res['found']:>4}/{res['truth']:<4} {res['extra']:>6} {cells}  "
           f"{res['textAtLeast90']['pct']:8.0f}%  {res['textCoverageMedian']:10.0f}%  "
           f"{res['textPrecisionMedian']:12.0f}%")
    return f"=== {res['book'].upper()}: {res['truth']} entries in 5etools ===\n{head}\n{row}"


def run(book: str, path: Path, fivetools_dir: Path, diffs_out: Path | None = None) -> int:
    data = json.loads(Path(path).read_text("utf-8"))
    res = score_book(book, data["entities"], fivetools_dir)
    print(format_table(res))
    if diffs_out:
        lines = [f"MISSING {n}" for n in res["missing"]] + res["diffs"]
        diffs_out.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return 0
