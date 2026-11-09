import pytest

from bookextractor.boundary_score import f1, tokens


def test_tokens_lowercase_words():
    assert tokens("Hello, World 2!") == ["hello", "world", "2"]


def test_f1_identical():
    assert f1("a b c", "a b c") == 1.0


def test_f1_disjoint():
    assert f1("a b", "c d") == 0.0


def test_f1_empty():
    assert f1("", "a b") == 0.0
    assert f1("a b", "") == 0.0


def test_f1_subset():
    assert f1("a b c d", "a b") == pytest.approx(0.667, abs=0.001)
