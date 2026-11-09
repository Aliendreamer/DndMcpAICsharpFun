"""Align OCR page lines to a stored section's entries, and remove cut lines from those entries."""
from __future__ import annotations

from .boundary import BLine
from .boundary_score import tokens


def _norm(s: str) -> str:
    return " ".join(s.split())


def _text(e) -> str:
    """Searchable text of an entry: str as is; dict with items -> items joined; other dict -> string values."""
    if isinstance(e, str):
        return e
    if isinstance(e, dict):
        if isinstance(e.get("items"), list):
            return " ".join(i for i in e["items"] if isinstance(i, str))
        return " ".join(_text(v) for v in e.values() if isinstance(v, (str, dict, list)))
    if isinstance(e, list):
        return " ".join(_text(v) for v in e)
    return ""


def _trigrams(toks: list[str]) -> set[tuple[str, str, str]]:
    return {tuple(toks[i:i + 3]) for i in range(len(toks) - 2)}


def section_lines(lines: list[BLine], entries: list) -> list[int]:
    """Indices of lines whose text belongs to the stored entries (>=50% trigram overlap)."""
    all_toks = tokens(" ".join(_text(e) for e in entries))
    tri, vocab = _trigrams(all_toks), set(all_toks)
    out: list[int] = []
    for i, ln in enumerate(lines):
        t = tokens(ln.text)
        if not t:
            continue
        if len(t) < 3:
            ok = all(w in vocab for w in t)
        else:
            lt = _trigrams(t)
            ok = sum(g in tri for g in lt) * 2 >= len(lt)
        if ok:
            out.append(i)
    return out


def _slots(entries: list) -> list[tuple[int, int | None, str]]:
    """(entry index, item index or None, normalised text) for every searchable string."""
    slots: list[tuple[int, int | None, str]] = []
    for ei, e in enumerate(entries):
        if isinstance(e, str):
            slots.append((ei, None, _norm(e)))
        elif isinstance(e, dict) and e.get("type") == "list" and isinstance(e.get("items"), list):
            for ii, it in enumerate(e["items"]):
                if isinstance(it, str):
                    slots.append((ei, ii, _norm(it)))
    return slots


def _count(hay: str, needle: str) -> int:
    n, pos = 0, hay.find(needle)
    while pos != -1:
        n += 1
        pos = hay.find(needle, pos + 1)
    return n


def remove(entries: list, dropped: list[str]) -> list | None:
    """Remove each dropped text from entries; None if any is not found exactly once. Never mutates input."""
    cur: dict[tuple[int, int | None], str] = {(e, i): t for e, i, t in _slots(entries)}
    for d in dropped:
        needle = _norm(d)
        if not needle:
            continue
        hits = [(k, _count(t, needle)) for k, t in cur.items()]
        if sum(c for _, c in hits) != 1:
            return None
        key = next(k for k, c in hits if c)
        t = cur[key]
        pos = t.find(needle)
        cur[key] = _norm(t[:pos] + " " + t[pos + len(needle):])
    out: list = []
    for ei, e in enumerate(entries):
        if isinstance(e, str):
            if (ei, None) in cur:
                t = cur[(ei, None)]
                if t:
                    out.append(t if t != _norm(e) else e)
            continue
        if isinstance(e, dict) and e.get("type") == "list" and isinstance(e.get("items"), list):
            items = []
            for ii, it in enumerate(e["items"]):
                if isinstance(it, str):
                    t = cur[(ei, ii)]
                    if t:
                        items.append(t if t != _norm(it) else it)
                else:
                    items.append(it)
            if items:
                out.append({**e, "items": items})
            continue
        out.append(e)
    return out
