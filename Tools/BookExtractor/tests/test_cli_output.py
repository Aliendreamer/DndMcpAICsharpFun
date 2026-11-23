"""`extract` writes the coverage and the left-out lines next to the entities and the report."""
import json

from regression_run import DATA
import gzip

from bookextractor.assemble import ALL_TYPES, extract_book
from bookextractor.cli import output_of
from bookextractor.pages_io import page_from_dict

_RAW = json.loads(gzip.decompress((DATA / "fullbook_cases.json.gz").read_bytes()))


def test_output_carries_coverage_and_left_out():
    c = _RAW["cases"]["front-phb-index"]
    result = extract_book([page_from_dict(_RAW["pages"][f"{c['slug']}:{n}"]) for n in c["pages"]], c["slug"],
                          set(ALL_TYPES))
    out = json.loads(json.dumps(output_of(result)))
    assert {"entities", "report", "coverage", "leftOut"} <= set(out)
    assert {"contentLines", "unheld", "unheldLines"} <= set(out["coverage"])
    assert out["leftOut"] and set(out["leftOut"][0]) == {"page", "text"}
