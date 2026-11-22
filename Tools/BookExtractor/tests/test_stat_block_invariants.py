"""Fixture-wide invariant: the regression gate scores only the reviewed entities, so this guards the rest."""
import gzip
import json
import re
from collections import defaultdict

import pytest
from regression_run import DATA, load_fixture

from bookextractor.pages_io import extract_pages, page_from_dict

_ARMOR_CLASS_LINE = re.compile(r"^armor class\b", re.I | re.M)

# Monster ids that already hold two stat-block headers on the fixture pages (not fixed here).
KNOWN_VIOLATIONS: set[str] = set()


def _pages_by_slug() -> dict[str, dict[int, object]]:
    pages, _ = load_fixture()
    extra = json.loads(gzip.decompress((DATA / "pattern_cases.json.gz").read_bytes()))["pages"]
    pages = {**pages, **{k: page_from_dict(v) for k, v in extra.items()}}
    by_slug: dict[str, dict[int, object]] = defaultdict(dict)
    for key, page in pages.items():
        slug, n = key.rsplit(":", 1)
        by_slug[slug][int(n)] = page
    return by_slug


def test_no_monster_holds_two_stat_block_headers():
    violations: set[str] = set()
    for slug, numbered in _pages_by_slug().items():
        entities = extract_pages([numbered[n] for n in sorted(numbered)], slug)
        violations |= {e["id"] for e in entities if e["type"] == "Monster"
                       and len(_ARMOR_CLASS_LINE.findall(e["canonicalText"])) > 1}
    assert violations - KNOWN_VIOLATIONS == set()
