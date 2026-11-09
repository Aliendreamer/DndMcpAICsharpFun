"""Align OCR page lines to a stored section's entries, and remove cut lines from those entries."""
from __future__ import annotations

import re

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


def drop_runs(texts: list[str], drop: set[int]) -> list[list[str]]:
    """Group dropped line indices into runs of consecutive lines (reading order) as text lists."""
    runs: list[list[str]] = []
    prev = None
    for i in sorted(drop):
        if prev is None or i != prev + 1:
            runs.append([])
        runs[-1].append(texts[i])
        prev = i
    return runs


def _slots(entries: list) -> list[tuple[int, int | None, str]]:
    """(entry index, item index or None, raw text) for every searchable string."""
    slots: list[tuple[int, int | None, str]] = []
    for ei, e in enumerate(entries):
        if isinstance(e, str):
            slots.append((ei, None, e))
        elif isinstance(e, dict) and e.get("type") == "list" and isinstance(e.get("items"), list):
            for ii, it in enumerate(e["items"]):
                if isinstance(it, str):
                    slots.append((ei, ii, it))
    return slots


def _run_tokens(run: list[str]) -> list[str]:
    """Token sequence of a run; a line ending in '-' is joined to the next line's first word."""
    out: list[str] = []
    carry: str | None = None
    for line in run:
        t = tokens(line)
        if carry is not None and t:
            t[0] = carry + t[0]
            carry = None
        if line.rstrip().endswith("-") and t:
            carry = t.pop()
        out.extend(t)
    if carry:
        out.append(carry)
    return out


def _find(stream: list[str], seq: list[str]) -> list[int]:
    n = len(seq)
    return [i for i in range(len(stream) - n + 1) if stream[i:i + n] == seq]


def remove(entries: list, dropped: list[list[str]]) -> list | None:
    """Remove runs of dropped lines (lists of consecutive line texts, reading order) from entries.

    Matching is on casefolded \\w+ tokens over all string slots as one stream, so a run may span
    paragraphs and tolerates punctuation/bullets. A run not found exactly once is retried line by
    line; lines not found anywhere are ignored, lines matching more than once give None.
    Never mutates the input.
    """
    slots = _slots(entries)
    toks: list[tuple[int, int, int, str]] = []  # slot, start, end, token
    for si, (_, _, raw) in enumerate(slots):
        for m in re.finditer(r"\w+", raw):
            toks.append((si, m.start(), m.end(), m.group().lower()))
    alive = list(range(len(toks)))
    deleted: set[int] = set()

    cursor = 0  # original-stream index just past the previous match (reading-order monotonic)

    def take(seq: list[str]) -> bool | None:
        """True removed (first match at/after cursor), False absent, None only before cursor."""
        nonlocal cursor
        if not seq:
            return False
        stream = [toks[k][3] for k in alive]
        hits = _find(stream, seq)
        if not hits:
            return False
        later = [h for h in hits if alive[h] >= cursor]
        if not later:
            return None
        h = later[0]
        cursor = alive[h + len(seq) - 1] + 1
        deleted.update(alive[h:h + len(seq)])
        alive[:] = [k for k in alive if k not in deleted]
        return True

    for run in dropped:
        r = take(_run_tokens(run))
        if r is True:
            continue
        if r is None and len(run) == 1:
            return None
        carry = False
        for line in run:
            t = tokens(line)
            if carry and t:
                t = t[1:]
            carry = line.rstrip().endswith("-")
            if carry and t:
                t = t[:-1]
            if take(t) is None:
                return None

    spans: dict[int, list[tuple[int, int]]] = {}
    for k in sorted(deleted):
        si, a, b, _ = toks[k]
        lst = spans.setdefault(si, [])
        lst.append((a, b))
    new: dict[int, str] = {}
    for si, lst in spans.items():
        raw = slots[si][2]
        # merge token spans that were adjacent in the stream (only whitespace/punct between them
        # and all in-between tokens deleted) by deleting between first..last of contiguous groups
        idx = [k for k in sorted(deleted) if toks[k][0] == si]
        groups: list[list[int]] = [[idx[0]]]
        for k in idx[1:]:
            groups[-1].append(k) if k == groups[-1][-1] + 1 else groups.append([k])
        cuts = []
        for g in groups:
            a, b = toks[g[0]][1], toks[g[-1]][2]
            while a > 0 and not raw[a - 1].isspace() and not re.match(r"\w", raw[a - 1]):
                a -= 1
            while b < len(raw) and not raw[b].isspace() and not re.match(r"\w", raw[b]):
                b += 1
            cuts.append((a, b))
        out_s, pos = [], 0
        for a, b in cuts:
            out_s.append(raw[pos:a] + " ")
            pos = b
        out_s.append(raw[pos:])
        txt = _norm("".join(out_s))
        new[si] = txt if re.search(r"\w", txt) else ""

    where = {(e, i): si for si, (e, i, _) in enumerate(slots)}
    result: list = []
    for ei, e in enumerate(entries):
        if isinstance(e, str):
            si = where[(ei, None)]
            t = new.get(si, e)
            if t:
                result.append(t)
        elif isinstance(e, dict) and e.get("type") == "list" and isinstance(e.get("items"), list):
            items = []
            for ii, it in enumerate(e["items"]):
                if isinstance(it, str):
                    t = new.get(where[(ei, ii)], it)
                    if t:
                        items.append(t)
                else:
                    items.append(it)
            if items:
                result.append({**e, "items": items})
        else:
            result.append(e)
    return result
