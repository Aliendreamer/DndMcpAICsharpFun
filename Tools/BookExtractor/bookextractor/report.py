"""Recognition report for one extraction run."""
from __future__ import annotations

THRESHOLD = 0.95
# the extractor's types (assemble.ALL_TYPES), counted even when a run produced none of one
TYPES = ("Monster", "Spell", "MagicItem", "Class", "Subclass", "Race", "Subrace", "Feat", "Background")


def build_report(book: str, pages_total: int, pages_rules: int, pages_covered: int,
                 entities: list[dict]) -> dict:
    counts = {t: sum(1 for e in entities if e["type"] == t) for t in TYPES}
    failed = [{"id": e["id"], "checks": [c["name"] for c in e["checks"] if not c["passed"]]}
              for e in entities if any(not c["passed"] for c in e["checks"])]
    passing = len(entities) - len(failed)
    passing_share = passing / len(entities) if entities else 0.0
    coverage_share = pages_covered / pages_rules if pages_rules else 0.0
    warnings: list[str] = []
    if passing_share < THRESHOLD:
        warnings.append(f"recognition {passing_share:.1%} is under 95%: entities passing all checks")
    if coverage_share < THRESHOLD:
        warnings.append(f"recognition {coverage_share:.1%} is under 95%: rules pages covered")
    return {"book": book, "pagesTotal": pages_total, "pagesRules": pages_rules, "pagesCovered": pages_covered,
            "counts": counts, "passingShare": passing_share, "coverageShare": coverage_share,
            "warnings": warnings, "failed": failed}
