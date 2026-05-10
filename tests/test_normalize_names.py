import sys
sys.path.insert(0, 'scripts')
from normalize_canonical_names import dnd_title_case, has_ocr_artifacts, normalize_entity

# --- dnd_title_case ---

def test_all_caps_simple():
    assert dnd_title_case("FIREBALL") == "Fireball"

def test_all_caps_with_small_word():
    assert dnd_title_case("CIRCLE OF SPORES") == "Circle of Spores"

def test_all_caps_starts_with_small_word():
    assert dnd_title_case("OF MICE AND MEN") == "Of Mice and Men"

def test_apostrophe_corrected():
    assert dnd_title_case("TASHA'S CAULDRON") == "Tasha's Cauldron"

def test_hyphenated_word():
    assert dnd_title_case("SPIDER-CLIMB") == "Spider-Climb"

# --- has_ocr_artifacts ---

def test_clean_name_no_artifact():
    assert not has_ocr_artifacts("Circle of Spores")

def test_all_caps_is_artifact():
    assert has_ocr_artifacts("CIRCLE OF SPORES")

def test_split_word_is_artifact():
    assert has_ocr_artifacts("Path of the Beast f eature")

def test_noise_dots_is_artifact():
    assert has_ocr_artifacts("Some ..... Thing")

def test_alternating_case_is_artifact():
    assert has_ocr_artifacts("Gons OF YouR WoRLD")

def test_single_word_no_artifact():
    assert not has_ocr_artifacts("Fighter")

# --- normalize_entity ---

def test_all_caps_entity_gets_title_cased():
    e = {"id": "x", "name": "CIRCLE OF SPORES", "needsReview": False}
    result = normalize_entity(e)
    assert result["name"] == "Circle of Spores"
    assert result["needsReview"] is False

def test_garbled_entity_gets_flagged():
    e = {"id": "x", "name": "Path of the Beast f eature"}
    result = normalize_entity(e)
    assert result["name"] == "Path of the Beast f eature"
    assert result["needsReview"] is True

def test_clean_entity_unchanged():
    e = {"id": "x", "name": "Fighter"}
    result = normalize_entity(e)
    assert result["name"] == "Fighter"
    assert result["needsReview"] is False

def test_idempotent_all_caps():
    e1 = {"id": "x", "name": "FIREBALL"}
    e2 = normalize_entity(dict(e1))
    e3 = normalize_entity(dict(e2))
    assert e2 == e3

def test_idempotent_already_clean():
    e = {"id": "x", "name": "Fireball", "needsReview": False}
    assert normalize_entity(dict(e)) == normalize_entity(normalize_entity(dict(e)))
