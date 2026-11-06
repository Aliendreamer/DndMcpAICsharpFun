"""Magic item parser (Dungeon Master's Guide layout) and its verification checks."""
from __future__ import annotations

import re
import statistics

from ..checks import Check
from ..model import Line
from . import Parsed
from .monster import _title
from .spell import _paragraphs

CATEGORIES = ["Armor", "Potion", "Ring", "Rod", "Scroll", "Staff", "Wand", "Weapon", "Wondrous item"]
_RARITY = r"(?:very rare|uncommon|common|rare|legendary|artifact|rarity varies|rarity by [a-z]+|varies)"
_CAT = r"(Armor|Potion|Ring|Rod|Scroll|Staff|Wand|Weapon|Wondrous item)"
_PAREN = r"\((?:[^()]|\([^)]*\))*\)"
_TYPE_START = re.compile(
    rf"^{_CAT}\b\s*({_PAREN}|\([^)]*)?\s*(?:[,.;]\s*(?:{_RARITY}\b|very\b|$)|\s+{_RARITY}\b)", re.I)
_TYPE_FULL = re.compile(rf"^{_CAT}\b\s*({_PAREN})?\s*[,.;]?\s*(.*)$", re.I | re.S)
_ATTUNE = re.compile(r"\(?\s*requires attunement\s*(.*?)\s*\)?\s*$", re.I | re.S)
_BONUS = re.compile(rf"({_RARITY})\s*\(\s*\+(\d)\s*\)", re.I)
_FURNITURE = re.compile(r"^(?:chapter\s*\d+.*|.*\|\s*treasure|treasure\s*\|.*|\W*treasure\W*|\d{1,3})$", re.I)
_STATBLOCK = re.compile(r"^(?:tiny|small|medium|large|huge|gargantuan)\b", re.I)
_TERMINAL = re.compile(r"[.!?:)\"'”]\s*$")
_SMALL = {"of", "the", "and", "to", "in", "a", "an", "for", "on", "or", "with"}
_RARITIES = {"common", "uncommon", "rare", "very rare", "legendary", "artifact", "varies"}


def _is_type_line(t: str) -> bool:
    return bool(_TYPE_START.match(t))


def _row_like(l: Line) -> bool:
    """A table row: a dice-range label ("97-00 Portable ram") or fragments separated by a column-sized gap."""
    return bool(re.match(r"\d{1,3}(?:-\d{1,3})?\s+\S", l.text)) or \
        any(b.box[0] - a.box[2] > 1.5 * l.height for a, b in zip(l.frags, l.frags[1:]))


def _heading_like(t: str) -> bool:
    words = t.split()
    return 0 < len(words) <= 7 and not re.search(r"[.!?:;]\s*$", t) and t[:1].isalpha()


def _gap(a: Line, b: Line) -> float | None:
    """Vertical gap between two consecutive lines of one column; None when they are not stacked."""
    if a.page != b.page:
        return None
    g = b.box[1] - a.box[3]
    return None if g < -a.height else max(g, 0.0)


def _type_end(L: list[Line], i: int) -> int:
    """Index after the type line starting at i (it can wrap over up to two more lines)."""
    j = i + 1
    t = L[i].text
    while j < len(L) and j < i + 3 and L[j].page == L[i].page:
        open_paren = t.count("(") > t.count(")")
        dangling = bool(re.search(r"(?:,|\b(?:or|very|and|by|a|an|of))\s*$", t, re.I))
        # "(requires attunement ...)" or the last bonus of a wrapped rarity list: "(+3) (requires ...)"
        carried = bool(re.match(r"\(\s*(?:requires|\+\d\))", L[j].text, re.I))
        if not (open_paren or dangling or carried):
            break
        t = f"{t} {L[j].text}"
        j += 1
    return j


def _names_start(L: list[Line], i: int, lo: int) -> int:
    """Index of the first printed-name line above the type line at i (a name may wrap over two lines)."""
    if i - 2 < lo:
        return i - 1
    a, b = L[i - 2], L[i - 1]
    g_ab = _gap(a, b)
    if g_ab is None or g_ab > 0.35 * b.height:
        return i - 1
    if not (_heading_like(a.text) and _heading_like(b.text)) or _is_type_line(a.text) or re.search(r"\d", a.text):
        return i - 1
    if b.text[:1].islower() and b.text.split()[0].lower() not in {"of", "the", "and", "or", "to", "in", "against"}:
        return i - 1
    if i - 3 >= lo:
        g_before = _gap(L[i - 3], a)
        if g_before is not None and g_before < 0.4 * a.height:
            return i - 1  # tightly stacked on the line above: body text, not a name
    return i - 2


def _starts(L: list[Line]) -> list[tuple[int, int, int]]:
    """(name start, type line, body start) for every item."""
    out: list[tuple[int, int, int]] = []
    prev_body = 0
    for i, l in enumerate(L):
        if i == 0 or not _is_type_line(l.text):
            continue
        if len(L[i - 1].text.split()) > 12 or _is_type_line(L[i - 1].text):
            continue
        ns = _names_start(L, i, max(prev_body, 0))
        out.append((ns, i, _type_end(L, i)))
        prev_body = i + 1
    return out


def _captions(L: list[Line], med: float) -> set[int]:
    """Indexes of art labels: short heading-like clusters isolated by large vertical gaps."""
    cap: set[int] = set()
    n = len(L)
    i = 0
    while i < n:
        j = i
        while j + 1 < n:
            g = _gap(L[j], L[j + 1])
            if g is None or g >= 0.5 * med:
                break
            j += 1
        size = j - i + 1
        before = _gap(L[i - 1], L[i]) if i > 0 else None
        after = _gap(L[j], L[j + 1]) if j + 1 < n else None
        isolated = (before is None or before >= 0.8 * med) and (after is None or after >= 0.8 * med)
        if size <= 4 and isolated and all(_heading_like(L[x].text) and not _is_type_line(L[x].text)
                                          and not re.search(r"\d", L[x].text) for x in range(i, j + 1)):
            cap.update(range(i, j + 1))
        i = j + 1
    return cap


def _section_break(L: list[Line], x: int, med: float) -> bool:
    """An oversized heading (e.g. 'Destroying Artifacts') that is not an item name ends the item."""
    l = L[x]
    if l.height <= 1.15 * med or not _heading_like(l.text) or len(l.text) < 4 or re.search(r"\d", l.text):
        return False
    if not all(w[:1].isupper() or w.lower() in _SMALL for w in l.text.split()):
        return False  # a table row ('Blue sapphire Air elemental'), not a heading
    nxt = L[x + 1].text if x + 1 < len(L) else ""
    gap = _gap(L[x - 1], l) if x > 0 else None
    if gap is not None and gap < 0.6 * med:
        return False  # tight under the line above: a table header, not a section heading
    return not (_STATBLOCK.match(nxt) or _is_type_line(nxt))


# ---------- field conversion ----------
def _type_fields(printed: str) -> dict:
    """Parse the joined type line into type/rarity/attunement/variants (values may be None when unparseable)."""
    m = _TYPE_FULL.match(printed.strip())
    out: dict = {"type": None, "rarity": None, "reqAttune": None, "variants": []}
    if not m:
        return out
    cat = next((c for c in CATEGORIES if c.lower() == m.group(1).lower()), None)
    sub = (m.group(2) or "").strip()
    rest = m.group(3).strip()
    if cat:
        out["type"] = cat.lower() + (f" {sub}" if sub else "")
    att = _ATTUNE.search(rest)
    if att:
        out["reqAttune"] = re.sub(r"\s+", " ", att.group(1)).strip() or True
        rest = rest[:att.start()].strip(" ,;")
    words = re.findall(_RARITY, rest, re.I)
    seen = {w.lower() for w in words}
    if not seen:
        return out
    if len(seen) > 1 or any(w.startswith("rarity") or w == "varies" for w in seen):
        out["rarity"] = "varies"
        out["variants"] = [{"name": f"+{b}", "rarity": r.lower(), "bonus": int(b)} for r, b in _BONUS.findall(rest)]
    else:
        out["rarity"] = seen.pop()
    return out


def _name(raw: str) -> str:
    """Printed name without a sidebar label ("Magic Item: Infernal Tack", MTF)."""
    return _title(re.sub(r"^\s*magic item\s*:\s*", "", re.sub(r"\s+", " ", raw), flags=re.I))


def _leaves_inset(prev: Line, l: Line, nxt: Line | None, margin: float, med: float) -> bool:
    """`l` sits below `prev` after a gap but starts well left of the item's own left edge, and its paragraph
    goes on below it: the item was an inset sidebar (MTF "Magic Item: Greater Silver Sword") and the column's
    own text resumes below it. A lone line there (a page-edge mark, a running foot) is not that text."""
    g = _gap(prev, l)
    if g is None or g < 0.6 * med or margin - l.box[0] < 0.5 * l.height:
        return False
    g2 = _gap(l, nxt) if nxt is not None else None
    return g2 is not None and g2 < 0.5 * med and min(l.box[2], nxt.box[2]) > max(l.box[0], nxt.box[0])


def _parse_item(L: list[Line], med: float, caps: set[int], ns: int, ti: int, nxt: int, end: int) -> Parsed:
    printed = " ".join(L[x].text for x in range(ti, nxt))
    t = _type_fields(printed)
    body: list[Line] = []
    prev = L[nxt - 1]
    first_column = True
    margin = min(L[x].box[0] for x in range(ns, nxt) if L[x].page == prev.page)
    for x in range(nxt, end):
        if x in caps or _FURNITURE.match(L[x].text):
            continue
        below = L[x + 1] if x + 1 < len(L) else None
        if _section_break(L, x, med) or (first_column and _leaves_inset(prev, L[x], below, margin, med)):
            break
        # an inset sidebar is told by its own column only: once the text moves to another column or page
        # (art captions, tables and quotes there set their own edges) the check stops
        first_column = first_column and _gap(prev, L[x]) is not None
        margin = min(margin, L[x].box[0])
        prev = L[x]
        body.append(L[x])
    entries = _paragraphs(body) if body else []
    fields: dict = {"type": t["type"], "rarity": t["rarity"], "reqAttune": t["reqAttune"],
                    "wondrous": bool(t["type"] and t["type"].startswith("wondrous item")), "entries": entries}
    if t["variants"]:
        fields["variants"] = t["variants"]
    name = _name(" ".join(L[x].text for x in range(ns, ti)))
    block = L[ns:nxt] + body
    text = "\n".join([name, printed] + [l.text for l in body])
    return Parsed(type="MagicItem", name=name, fields=fields, text=text, lines=block)


def parse_items(lines: list[Line]) -> list[Parsed]:
    """Parse every magic item found in `lines` (reading order, pages concatenated)."""
    L = [l for l in lines if not _FURNITURE.match(l.text)]
    if not L:
        return []
    med = statistics.median(l.height for l in L) or 1.0
    caps = _captions(L, med)
    st = [s for s in _starts(L) if s[0] not in caps and s[1] not in caps]
    out: list[Parsed] = []
    for k, (ns, ti, nxt) in enumerate(st):
        end = st[k + 1][0] if k + 1 < len(st) else min(len(L), nxt + 250)
        p = _parse_item(L, med, caps, ns, ti, nxt, end)
        p.checks = item_checks(p)
        out.append(p)
    return out


# ---------- checks ----------
def _printed_type(p: Parsed) -> str:
    return p.text.split("\n")[1] if "\n" in p.text else ""


def _words_twice(name: str) -> bool:
    w = name.lower().split()
    pairs = [tuple(w[i:i + 2]) for i in range(len(w) - 1)]
    return len(pairs) != len(set(pairs))


def item_checks(p: Parsed) -> list[Check]:
    f = p.fields
    printed = _printed_type(p)
    low = printed.lower()
    ents = f.get("entries") or []
    last = ents[-1] if ents else ""

    def chk(name: str, ok: bool, why: str) -> Check:
        return Check(name, ok, "" if ok else why)

    first = re.match(rf"^{_CAT}\b", printed, re.I)
    type_ok = bool(f.get("type")) and bool(first) and f["type"].startswith(first.group(1).lower()) \
        and f.get("wondrous") is (first.group(1).lower() == "wondrous item")
    rarity = f.get("rarity")
    printed_words = {w.lower() for w in re.findall(_RARITY, re.sub(r"\(requires attunement.*", "", printed, flags=re.I), re.I)}
    if rarity == "varies":
        rarity_ok = len(printed_words) > 1 or any(w.startswith("rarity") or w == "varies" for w in printed_words)
    else:
        rarity_ok = rarity in _RARITIES and printed_words == {rarity}
    has_att = "requires attunement" in low
    att = f.get("reqAttune")
    att_ok = has_att == bool(att) and (not isinstance(att, str) or
                                       re.sub(r"[^a-z]", "", att.lower()) in re.sub(r"[^a-z]", "", low))
    table_end = sum(_row_like(l) for l in p.lines[-6:]) >= 2  # the description ends in a table
    complete = bool(ents) and (not isinstance(last, str) or "•" in last or table_end
                               or bool(_TERMINAL.search(last)))
    name_ok = 0 < len(p.name.split()) <= 12 and not _words_twice(p.name) and not _is_type_line(p.name)
    return [
        chk("type-parsed", type_ok, f"type {f.get('type')!r} does not match printed line {printed!r}"),
        chk("rarity-parsed", rarity_ok, f"rarity {rarity!r} does not match printed line {printed!r}"),
        chk("attunement-agrees", att_ok, f"reqAttune {att!r} disagrees with printed line {printed!r}"),
        chk("entries-complete", complete, f"description does not end in terminal punctuation: ...{str(last)[-40:]!r}"),
        chk("name-sane", name_ok, f"name {p.name!r} looks wrong"),
    ]
