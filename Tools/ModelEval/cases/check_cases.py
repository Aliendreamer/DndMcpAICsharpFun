#!/usr/bin/env python3
"""Checks that the facts every reference and cross-check question expects from a tool are in our own books.

Run from the repo root: python3 Tools/ModelEval/cases/check_cases.py
Each case names its evidence entities; their text (with fields) must hold every tool fact, matched as the eval does.
"""
import glob
import json
import re
import sys


def norm(s):
    s = re.sub(r"[\s,*_`:]", "", s.lower())
    return re.sub("[‐-―−]", "-", s).replace("‘", "'").replace("’", "'")


def has(text, variant):
    v = norm(variant)
    left = r"(?<!\d)(?<!\d/)" if v[0].isdigit() else ""
    right = r"(?!\d)(?!/\d)" if v[-1].isdigit() else ""
    return re.search(left + re.escape(v) + right, text) is not None


books = {}
for path in glob.glob("books/canonical/*.json"):
    if path.split("/")[-1].count(".") > 1:
        continue
    for e in json.load(open(path, encoding="utf-8"))["entities"]:
        if e.get("disposition", "Accepted") == "Accepted":
            books[e["id"]] = norm((e.get("canonicalText") or "") + " " + json.dumps(e.get("fields") or {}, ensure_ascii=False))

bad = 0
sets = {n: json.load(open(f"Tools/ModelEval/cases/{n}.json", encoding="utf-8")) for n in ("reference", "crosscheck")}
for name, cases in sets.items():
    for c in cases:
        missing = [i for i in c["evidence"] if i not in books]
        text = " ".join(books.get(i, "") for i in c["evidence"])
        lost = [f["variants"] for f in c["toolDataFacts"] or [] if not any(has(text, v) for v in f["variants"])]
        if missing or lost:
            bad += 1
            print(f"{c['id']}: missing entities {missing}, facts not in them {lost}")
    print(f"{name}: {len(cases)} questions")
prompts = [c["prompt"] for cases in sets.values() for c in cases]
if len(set(prompts)) != len(prompts):
    bad += 1
    print("a question appears twice")
sys.exit(1 if bad else 0)
