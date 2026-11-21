import pytest

from regression_run import load_baseline, score_all

BASELINE = load_baseline()


@pytest.fixture(scope="module")
def results():
    return [r for r in score_all() if r["id"] in BASELINE]


def test_no_lost_match(results):
    lost = [r["id"] for r in results if BASELINE[r["id"]]["match"] and not r["match"]]
    assert not lost, lost


def test_no_lost_found(results):
    lost = [r["id"] for r in results if BASELINE[r["id"]]["found"] and not r["found"]]
    assert not lost, lost


def test_no_f1_drop(results):
    dropped = [(r["id"], BASELINE[r["id"]]["f1"], r["f1"]) for r in results
               if r["f1"] < BASELINE[r["id"]]["f1"] - 0.005]
    assert not dropped, dropped
