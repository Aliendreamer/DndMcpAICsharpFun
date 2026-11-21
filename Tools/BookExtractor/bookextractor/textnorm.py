"""Match normaliser: text equality ignoring whitespace, line breaks and
end-of-line hyphenation. Case and punctuation are NOT normalised.
A C# twin copies these rules exactly."""
import re

_EOL_HYPHEN = re.compile(r"-[ \t]*\n\s*")


def normalize_text(s: str) -> str:
    s = _EOL_HYPHEN.sub("", s)
    return re.sub(r"\s+", " ", s).strip()


def values_match(a, b) -> bool:
    if isinstance(a, str) and isinstance(b, str):
        return normalize_text(a) == normalize_text(b)
    if isinstance(a, bool) or isinstance(b, bool):
        return isinstance(a, bool) and isinstance(b, bool) and a == b
    if isinstance(a, list) and isinstance(b, list):
        return len(a) == len(b) and all(values_match(x, y) for x, y in zip(a, b))
    if isinstance(a, dict) and isinstance(b, dict):
        return a.keys() == b.keys() and all(values_match(a[k], b[k]) for k in a)
    if isinstance(a, (str, list, dict)) or isinstance(b, (str, list, dict)):
        return False
    return a == b
