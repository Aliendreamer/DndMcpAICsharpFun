from bookextractor.textnorm import normalize_text, values_match


def test_normalize_hyphenation():
    assert normalize_text("ob-\nject") == "object"


def test_normalize_whitespace():
    assert normalize_text("a  b\n c") == "a b c"


def test_case_not_normalised():
    assert not values_match("Fire", "fire")


def test_nested_match_ignores_key_order_and_whitespace():
    assert values_match([{"name": "Bite", "entries": ["a\nb"]}], [{"entries": ["a b"], "name": "Bite"}])


def test_type_mismatch():
    assert not values_match(14, "14")


def test_bool_vs_int():
    assert not values_match(True, 1)
    assert values_match(1, 1.0)
