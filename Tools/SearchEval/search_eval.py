"""Entity search test set: for each question, the rank of the first result whose id matches the expected pattern.

    python3 Tools/SearchEval/search_eval.py [--out FILE] [--compare FILE] [--url http://localhost:5101]

`questions.json` holds [question, "id-part|id-part"]. Needs the running app; only embeds the questions.
"""
import argparse
import json
import re
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

TOP_K = 20


def rank(url: str, question: str, pattern: str) -> int | None:
    q = urllib.parse.urlencode({"q": question, "topK": TOP_K})
    while True:
        try:
            with urllib.request.urlopen(f"{url}/retrieval/entities/search?{q}", timeout=120) as r:
                data = json.load(r)
            break
        except urllib.error.HTTPError as e:
            if e.code != 429:  # the endpoint is rate limited
                raise
            time.sleep(10)
    hits = data if isinstance(data, list) else data.get("results") or data.get("items") or []
    return next((i + 1 for i, h in enumerate(hits) if re.search(pattern, h["id"])), None)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--url", default="http://localhost:5101")
    ap.add_argument("--out", type=Path)
    ap.add_argument("--compare", type=Path)
    args = ap.parse_args()
    questions = json.loads((Path(__file__).parent / "questions.json").read_text(encoding="utf-8"))
    ranks = {q: rank(args.url, q, p) for q, p in questions}
    if args.out:
        args.out.write_text(json.dumps(ranks, indent=1), encoding="utf-8")
    old = json.loads(args.compare.read_text(encoding="utf-8")) if args.compare else {}
    for q, r in ranks.items():
        was = f"  was {old[q] or '-'}" if q in old and old[q] != r else ""
        if was or not args.compare:
            print(f"{r or '-':>3}  {q}{was}")
    for name, rs in (("now", ranks), ("before", old)):
        if rs:
            v = list(rs.values())
            mrr = sum(1 / x for x in v if x) / len(v)
            print(f"{name}: top1 {sum(x == 1 for x in v)} top3 {sum(bool(x) and x <= 3 for x in v)} "
                  f"top{TOP_K} {sum(bool(x) for x in v)} of {len(v)}, MRR {mrr:.3f}")


if __name__ == "__main__":
    main()
