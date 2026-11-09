import json
from pathlib import Path

from bookextractor.boundary import BLine, approvable, cut
from bookextractor.boundary_align import drop_runs, remove, section_lines
from bookextractor.boundary_score import entries_text, f1

ROWS = json.loads((Path(__file__).parent / "data" / "boundary_regression.json").read_text())


def run(row):
    L = [BLine(l["text"], l["page"], l["col"], l["h"], tuple(l["box"]), l.get("shade", 0.0)) for l in row["lines"]]
    idx = section_lines(L, [row["heading_line"]] + row["entries"])
    S = [L[i] for i in idx]
    c = cut(S, row["name"], first_page=min(l.page for l in L)) if S else cut([], row["name"], 1)
    after = remove(row["entries"], drop_runs([l.text for l in S], c.drop)) if c.drop else row["entries"]
    after = after if after is not None else row["entries"]
    return c, after


RESULTS = [(r, *run(r)) for r in ROWS]


def test_match_entries_keep_f1():
    bad = [(r["id"], round(f1(entries_text(a), entries_text(r["entries"])), 3))
           for r, c, a in RESULTS if r["verdict"] == "MATCH"
           and f1(entries_text(a), entries_text(r["entries"])) < 0.95]
    assert not bad, bad


def test_approvable_fixed_never_worse():
    bad = []
    for r, c, a in RESULTS:
        if r["verdict"] != "FIXED" or not approvable(r["entries"], a, c, r["name"], r["checks"]):
            continue
        b, n = f1(entries_text(r["entries"]), r["corrected"]), f1(entries_text(a), r["corrected"])
        if n < b - 0.005:
            bad.append((r["id"], round(b, 3), round(n, 3)))
    assert not bad, bad


def test_report_numbers():
    fixed = [(r, c, a) for r, c, a in RESULTS if r["verdict"] == "FIXED"]
    match = [(r, c, a) for r, c, a in RESULTS if r["verdict"] == "MATCH"]
    with_cut = [x for x in fixed if x[1].drop]
    none_rm = 0
    for r, c, a in with_cut:
        L = [BLine(l["text"], l["page"], l["col"], l["h"], tuple(l["box"]), l.get("shade", 0.0)) for l in r["lines"]]
        S = [L[i] for i in section_lines(L, [r["heading_line"]] + r["entries"])]
        if remove(r["entries"], drop_runs([l.text for l in S], c.drop)) is None:
            none_rm += 1
    imp = unch = worse = 0
    ap = ap_imp = ap_worse = 0
    sb = sa = 0.0
    for r, c, a in fixed:
        b, n = f1(entries_text(r["entries"]), r["corrected"]), f1(entries_text(a), r["corrected"])
        sb += b
        sa += n
        d = n - b
        k = "imp" if d > 0.005 else "worse" if d < -0.005 else "unch"
        imp += k == "imp"
        unch += k == "unch"
        worse += k == "worse"
        if approvable(r["entries"], a, c, r["name"], r["checks"]):
            ap += 1
            ap_imp += k == "imp"
            ap_worse += k == "worse"
    mm = [(f1(entries_text(a), entries_text(r["entries"])), r["id"]) for r, c, a in match]
    print(f"\nFIXED rows={len(fixed)} with cut={len(with_cut)} remove->None={none_rm}")
    print(f"FIXED improved={imp} unchanged={unch} worse={worse}")
    print(f"approvable={ap} (improved {ap_imp}, worsened {ap_worse})")
    print(f"mean F1 FIXED before={sb/len(fixed):.4f} after={sa/len(fixed):.4f}")
    print(f"MATCH rows={len(match)} with cut={sum(1 for x in match if x[1].drop)} min F1={min(mm)[0]:.4f} ({min(mm)[1]})")
