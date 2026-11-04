"""Entity-name repair from the book's own vocabulary (no external word lists, never 5etools).

OCR splits or glues words inside names ("identi fy", "Oilof Sharpness"). The vocabulary is every word of the
book's rules pages with its count; a word is known when it occurs at least twice in body text, not counting the
entity's own heading. A name token that is unknown is repaired only by joining it with the next token or
splitting it into two known words; a repair is accepted only when every resulting word is known.
"""
from __future__ import annotations

import re
from collections import Counter
from dataclasses import dataclass, field
from typing import Iterable

MIN_COUNT = 2
_GLUED_SMALL = {"of", "and", "the"}
SMALL_WORDS = {"of", "the", "and", "a", "an", "against", "to", "in", "on", "from"}
_WORD = re.compile(r"[a-z]+(?:'[a-z]+)?")
_PLAIN = re.compile(r"^[A-Za-z']+$")


def words_of(text: str) -> list[str]:
    return _WORD.findall(text.lower().replace("’", "'"))


@dataclass
class Vocabulary:
    """Lowercase word counts and adjacent-word-pair counts over the book's body-text lines."""
    words: Counter = field(default_factory=Counter)
    pairs: Counter = field(default_factory=Counter)


def build_vocabulary(texts: Iterable[str]) -> Vocabulary:
    v = Vocabulary()
    for t in texts:
        ws = words_of(t)
        v.words.update(ws)
        v.pairs.update(zip(ws, ws[1:]))
    return v


def own_heading_counts(name: str) -> Vocabulary:
    """What the entity's own heading contributed to the vocabulary, to subtract from it."""
    return build_vocabulary([name])


@dataclass
class NameResult:
    name: str
    repaired: bool = False
    original: str = ""
    unknown: list[str] = field(default_factory=list)


class _Lexicon:
    def __init__(self, vocab: Vocabulary, own: Vocabulary):
        self.vocab = vocab
        self.own = own

    def count(self, w: str) -> int:
        return self.vocab.words.get(w, 0) - self.own.words.get(w, 0)

    def pair_count(self, a: str, b: str) -> int:
        return self.vocab.pairs.get((a, b), 0) - self.own.pairs.get((a, b), 0)

    def word_known(self, w: str) -> bool:
        if len(w) == 1:
            return w == "a"
        if self.count(w) >= MIN_COUNT:
            return True
        base = w[:-2] if w.endswith("'s") else None
        if base and self.count(base) >= MIN_COUNT:
            return True
        # plain plural / singular of a known word ("tricks" / "trick")
        if len(w) > 3 and w.endswith("s") and self.count(w[:-1]) >= MIN_COUNT:
            return True
        return len(w) > 2 and self.count(w + "s") >= MIN_COUNT

    def token_known(self, token: str) -> bool:
        """A whitespace token is known when every word of it is (hyphen compounds like 'x-ray' may hold
        a single letter)."""
        ws = words_of(token)
        if not ws:
            return True  # punctuation / digits only
        if "-" in token and len(ws) > 1:
            return all(self.word_known(w) or (len(w) == 1 and w.isalpha()) for w in ws)
        return all(self.word_known(w) for w in ws)

    def split(self, w: str) -> tuple[str, str] | None:
        """Two known words glued together. Accepted only with evidence: either one part is a glued
        'of'/'and'/'the' (the usual lost space) and the other is a word of 3+ letters, or the two words
        really do occur next to each other in the body text. Otherwise a legitimate compound
        ('Druidcraft') or an inflected word ('Constrictor') would be torn apart silently."""
        best: tuple[int, str, str] | None = None
        for i in range(1, len(w)):
            a, b = w[:i], w[i:]
            if len(a) < 3 and a not in _GLUED_SMALL or len(b) < 3 and b not in _GLUED_SMALL:
                continue
            if not (self.word_known(a) and self.word_known(b)):
                continue
            glued_small = (a in _GLUED_SMALL and len(b) >= 3) or (b in _GLUED_SMALL and len(a) >= 3)
            if glued_small or self.pair_count(a, b) >= 1:
                score = min(self.count(a), self.count(b))
                if best is None or score > best[0]:
                    best = (score, a, b)
        return (best[1], best[2]) if best else None


def _cap(word: str, first: bool) -> str:
    if not first and word.lower() in SMALL_WORDS:
        return word.lower()
    return word[:1].upper() + word[1:]


def repair_name(name: str, vocab: Vocabulary, own: Vocabulary | None = None) -> NameResult:
    """Return the repaired name and the tokens still unknown afterwards."""
    own = own if own is not None else own_heading_counts(name)
    lex = _Lexicon(vocab, own)
    tokens = name.split()
    out: list[str] = []
    repaired = False
    i = 0
    while i < len(tokens):
        t = tokens[i]
        if lex.token_known(t):
            out.append(t)
            i += 1
            continue
        nxt = tokens[i + 1] if i + 1 < len(tokens) else None
        if _PLAIN.match(t) and len(t) > 1:
            # (a) join with the next token ("identi fy", "woodl and"); a lone letter is never absorbed
            if nxt and _PLAIN.match(nxt) and len(nxt) > 1:
                joined = (t + nxt).lower()
                if lex.word_known(joined):
                    out.append(_cap(joined, not out))
                    repaired = True
                    i += 2
                    continue
            # (b) split into two known words ("oilof", "animalfriendship")
            sp = lex.split(t.lower())
            if sp:
                out.append(_cap(sp[0], not out))
                out.append(_cap(sp[1], False))
                repaired = True
                i += 1
                continue
        out.append(t)
        i += 1
    result = " ".join(out)
    # the first token keeps a capital, the small words stay lower case, only on a repaired name
    if repaired:
        result = " ".join(_cap(w, k == 0) if w.lower() in SMALL_WORDS or k == 0 else w
                          for k, w in enumerate(result.split()))
    unknown = [t for t in result.split() if not lex.token_known(t)]
    return NameResult(name=result, repaired=repaired, original=name, unknown=unknown)
