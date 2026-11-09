"""End a section's own lines where its printed text ends (sibling heading, map label), guarded like boundary_fix."""
from __future__ import annotations

from ..boundary import BLine, cut, kept_enough, name_matches
from ..model import Line
from . import ParseContext
from .charcommon import prose_entries
from .sections import page_column


def trim_own(own: list[Line], name: str, heads: int, starts: set[int], siblings: set[str],
             ctx: ParseContext) -> list[Line]:
    """`own` without the lines past its end. A heading cut applies only at a line naming another section of the same
    parse (`siblings`); a trim keeping under half the tokens is not applied. Never approves."""
    if len(own) <= heads or not own:
        return own
    bl = [BLine(l.text, l.page, page_column(l, ctx), l.height, l.box) for l in own]
    sub = frozenset(i for i, l in enumerate(own) if i > 0 and id(l) in starts)
    c = cut(bl, name, own[0].page, sub)
    if not c.drop:
        return own
    drop = set(c.drop)
    if c.heading is not None and not any(name_matches(own[c.heading].text, n) for n in siblings):
        drop -= set(range(c.heading, len(own)))
        if not drop:
            return own
    new = [l for i, l in enumerate(own) if i not in drop]
    if len(new) < len(own) and not kept_enough(prose_entries(own, heads, len(own)),
                                               prose_entries(new, min(heads, len(new)), len(new))):
        return own
    return new
