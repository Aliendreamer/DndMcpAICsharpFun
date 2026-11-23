"""fullbook_cases_add.py <ocr-cache> <case> <slug> <page...> — append OCR pages and a case to
tests/data/fullbook_cases.json.gz (a dev tool; run from Tools/BookExtractor). The fileHash is read from
`books/canonical/<slug>.json` in HEAD through git's filters, as fullbook_compare.py does."""
from __future__ import annotations

import gzip
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))
sys.path.insert(0, str(HERE))
from bookextractor.model import load_ocr_page  # noqa: E402
from bookextractor.pages_io import page_to_dict  # noqa: E402
from fullbook_compare import _repo_root, file_hash_of  # noqa: E402

DATA = HERE / "data" / "fullbook_cases.json.gz"


def main() -> None:
    cache, case, slug, *pages = sys.argv[1:]
    nums = [int(p) for p in pages]
    fh = file_hash_of(slug, _repo_root())
    raw = json.loads(gzip.decompress(DATA.read_bytes()))
    for n in nums:
        raw["pages"][f"{slug}:{n}"] = page_to_dict(load_ocr_page(Path(cache) / fh / "ocr" / f"{n:03d}.json", n))
    raw["cases"][case] = {"slug": slug, "pages": nums}
    DATA.write_bytes(gzip.compress(json.dumps(raw).encode()))
    print(f"added case {case}: {slug} pages {nums}")


if __name__ == "__main__":
    main()
