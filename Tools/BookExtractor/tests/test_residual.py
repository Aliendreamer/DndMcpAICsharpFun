"""Residual doubts (rr12/rr13): fixtures are the real lines (text, box, page) of the entities that got through."""
from types import SimpleNamespace

from bookextractor.model import Line
from bookextractor.residual import residual_checks


def L(rows):
    return [Line(t, (x0, y0, x1, y1), y1 - y0, pg) for t, x0, y0, x1, y1, pg in rows]


def run(name, rows, typ="Rule", heights=None, recurring=frozenset()):
    return {c.name: c.reason for c in
            residual_checks(SimpleNamespace(name=name, type=typ, lines=L(rows)), heights or {}, recurring)}


ALCHEMIST = [
    ("AlchemISt'S Supplies", 133, 216, 445, 246, 80),
    ("Alchemist's supplies enable a character to produce use-", 133, 249, 749, 281, 80),
    ("ful concoctions, such as acid or alchemist's fire.", 132, 281, 662, 310, 80),
    ("are carrying.", 133, 957, 278, 987, 80),
    ("Alchemist's Supplies", 133, 1015, 435, 1043, 80),
    ("Activity DC", 142, 1056, 715, 1088, 80),
    ("Neutralize acid 20", 137, 1222, 713, 1254, 80),
    ("BrEWer'S SUPpLIes", 133, 1287, 394, 1316, 80),
    ("Brewing is the art of producing beer. Not only does beer", 132, 1320, 757, 1351, 80),
    ("serve as an alcoholic beverage, but the process of brew-", 132, 1351, 753, 1382, 80),
]


def test_same_level_neighbour_absorbed():
    assert "neighbour-absorbed" in run("Alchemist's Supplies", ALCHEMIST)


def test_own_table_caption_and_recurring_label_are_not_neighbours():
    assert "neighbour-absorbed" not in run("Alchemist's Supplies", ALCHEMIST[:7] + ALCHEMIST[8:] + ALCHEMIST[8:9])
    assert "neighbour-absorbed" not in run("Alchemist's Supplies", ALCHEMIST, recurring={"brewerssupplies"})


STEP4 = [
    ("Step 4: SeLect Monsters", 844, 666, 1298, 703, 90),
    ("After using the tables from the previous step to deter-", 843, 717, 1437, 748, 90),
    ("mine the challenge ratings of the monsters in your en-", 841, 748, 1443, 780, 90),
    ("In addition to assessing monsters by challenge rating,", 862, 837, 1463, 874, 90),
    ("it's important to look at how certain monsters might", 841, 870, 1422, 902, 90),
    ("so stich with ten, eleven tops.", 945, 1091, 1279, 1133, 90),
    ("saving throws are all useful indicators. Compare the", 250, 1488, 838, 1526, 91),
]


def test_pull_quote_line_detached_inside_a_sentence():
    assert "quote-leak" in run("Step 4: Select Monsters", STEP4)


def test_wrapped_line_directly_below_is_no_quote():
    rows = STEP4[:5] + [("so stich with ten, eleven tops.", 841, 905, 1279, 937, 90)] + STEP4[6:]
    assert "quote-leak" not in run("Step 4: Select Monsters", rows)


def test_epigraph_block_over_the_opening_paragraph():
    rows = [("The Demonic Point of View", 151, 343, 685, 376, 9),
            ("WHy do WE desCEND UPoN tHE doMAIN oF oUR HaTED", 148, 403, 801, 428, 9),
            ("enemies? Because that's where the fighting is!", 149, 440, 645, 469, 9),
            ("Most other creatures believe demons to be little more", 148, 544, 773, 579, 9),
            ("than mindless engines of destruction and violence. If", 148, 577, 770, 610, 9)]
    assert "quote-leak" in run("The Demonic Point of View", rows, "Lore")


TACTICAL = [("TACTICAL MAPS", 850, 826, 1071, 858, 250)] + [
    (f"You can draw tactical maps with colored markers on a {i}", 850, 860 + 32 * i, 1460, 890 + 32 * i, 250)
    for i in range(10)] + [("HALF COveR (SqUaRes) Three- Quarters Cover (Squares)", 703, 1462, 1460, 1490, 250)]


def test_figure_labels_at_the_end():
    assert "figure-label-tail" in run("Tactical Maps", TACTICAL)
    assert "figure-label-tail" not in run("Tactical Maps", TACTICAL[:-1])


def test_stray_number_at_the_end():
    rows = [("INSPIRATION", 146, 645, 345, 680, 240)] + [
        (f"Awarding inspiration is an effective way to encourage {i}", 147, 694 + 31 * i, 742, 725 + 31 * i, 240)
        for i in range(7)] + [("24.0", 26, 2048, 72, 2072, 240)]
    assert "stray-number-tail" in run("Inspiration", rows)
    assert "stray-number-tail" not in run("Inspiration", rows[:-1])


def test_table_with_lines_missing():
    rows = [("Travel Pace", 124, 476, 298, 511, 183),
            ("pace makes it possible to sneak around and to search an", 124, 510, 700, 540, 183),
            ("Pace Minute Hour Day Effect", 134, 560, 610, 592, 183),
            ("Fast 400 feet 4 miles 30 miles -5 penalty to", 131, 595, 678, 629, 183),
            ("Normal 300 feet 3 miles 24 miles", 134, 698, 538, 728, 183),
            ("Slow 200 feet 2 miles 18 miles Able to use stealth", 132, 730, 735, 763, 183)]
    assert "table-cut" in run("Travel Pace", rows)
    rows[4] = ("Normal 300 feet 3 miles 24 miles", 134, 630, 538, 660, 183)
    rows[5] = ("Slow 200 feet 2 miles 18 miles Able to use stealth", 132, 662, 735, 692, 183)
    assert "table-cut" not in run("Travel Pace", rows)


def test_table_running_off_the_page_foot():
    rows = [("NPC Stat Blocks", 794, 1638, 1116, 1673, 279),
            ("Appendix B of the Monster Manual contains stat blocks", 796, 1691, 1408, 1716, 279)] + [
        (f"Aggressive Orc {i}", 153, 121 + 33 * i, 600, 151 + 33 * i, 280) for i in range(9)] + [
        ("Legendary Resistance Ancient black dragon", 153, 1890, 657, 1921, 280)]
    assert "table-cut" in run("NPC Stat Blocks", rows, heights={280: 2200})
    assert "table-cut" not in run("NPC Stat Blocks", rows[:-1], heights={280: 2200})


def test_lead_in_only_and_table_only():
    assert "table-cut" in run("Sample Poisons", [("SAMPLe POISoNs", 759, 1785, 1048, 1819, 257),
                                                  ("Each type of poison has its own debilitating effects.", 758, 1835, 1328, 1865, 257)])
    assert "table-cut" not in run("Disengage", [("DISENGAGE", 759, 1785, 1048, 1819, 257),
                                                ("If you take the Disengage action, your movement doesn't provoke opportunity attacks for the rest of the turn.", 758, 1835, 1328, 1865, 257)])


def test_god_is_not_checked():
    assert run("Corellon", ALCHEMIST, "God") == {}


def test_rule_that_is_only_a_table():
    rows = [("Map Travel Pace", 874, 822, 1111, 854, 242),
            ("Map Scale Slow Pace Normal Pace Fast Pace", 885, 863, 1462, 896, 242),
            ("Dungeon 20 sq./min. 30 sq./min. 40 sq./min.", 882, 901, 1477, 933, 242),
            ("(1 sq. = 10 ft.)", 885, 935, 1026, 965, 242),
            ("Province 2 hexes/hr., 3 hexes/hr., 4 hexes/hr.,", 883, 1035, 1477, 1063, 242)]
    assert "table-cut" in run("Map Travel Pace", rows)
