"""C1.3: the scorecard reads bots and people the same way, and judges against written targets."""
import scorecard
import records
from fixtures import shift, write


def make(tmp_path):
    paths = []
    for p, (persona, sprint, jobs) in enumerate([("reckless", 0.9, ("served a customer", "mopped a spill") * 3),
                                                 ("skittish", 0.2, ("mopped a spill", "restocked a shelf") * 3)]):
        for career in range(2):
            for n in range(3):
                label = f"{persona}:{p}|{career}"
                paths.append(write(tmp_path, f"{persona}_{career}_{n}.json",
                                   shift(bot=label, n=n + 1, seed=100 * p + 10 * career + n, sprint=sprint, jobs=jobs,
                                         choices=[(3.0, "Hide|Hurry|Nearest|move_to|Aisle 1|1|reflex|ok", 4.0, 1.0)])))
    paths.append(write(tmp_path, "me_1.json", shift(player="Employee #0417", n=1, seed=7)))
    paths.append(write(tmp_path, "me_2.json", shift(player="Employee #0417", n=2, seed=8)))
    return paths


def test_reads_bots_and_people_alike(tmp_path):
    make(tmp_path)
    shifts = records.load_all([str(tmp_path)])
    assert len(shifts) == 14
    c = scorecard.card(shifts)
    assert set(c["groups"]) == {"reckless", "skittish", "people"}
    assert c["groups"]["reckless"]["habits"]["sprint_share"] > c["groups"]["skittish"]["habits"]["sprint_share"]
    assert c["groups"]["reckless"]["reactions"]["hides"] == 1
    assert c["groups"]["people"]["players"] == 1


def test_pattern_beats_chance_for_a_strict_routine(tmp_path):
    make(tmp_path)
    c = scorecard.card(records.load_all([str(tmp_path)]))
    g = c["groups"]["reckless"]["pattern"]
    assert g["predictions"] > 0
    assert g["next_job"] == 1.0          # serve, mop, serve, mop… is fully predictable
    assert g["next_job"] > g["chance"]


def test_no_repeats_and_variety(tmp_path):
    make(tmp_path)
    c = scorecard.card(records.load_all([str(tmp_path)]))
    assert c["groups"]["skittish"]["no_repeats"]["identical"] == 0
    assert c["variety"]["accuracy"] > c["variety"]["chance"]
    assert c["bot_or_human"]["people"] == 1


def test_identical_shifts_are_caught(tmp_path):
    a = records.load(write(tmp_path, "a.json", shift(bot="x:1", seed=3)))
    b = records.load(write(tmp_path, "b.json", shift(bot="x:1", seed=3)))
    assert scorecard.identical(a, b)


def test_targets_judge(tmp_path):
    make(tmp_path)
    c = scorecard.card(records.load_all([str(tmp_path)]))
    v = scorecard.judge(c, {"bots": {"jobs_per_min": [0.1, 50], "pattern_over_chance": 0.0, "route_overlap_max": 1.0,
                                     "variety_over_chance": 0.0}})
    assert v and all(ok for *_, ok in v)
    v = scorecard.judge(c, {"bots": {"jobs_per_min": [100, 200]}})
    assert any(not ok for *_, ok in v)
    text = scorecard.markdown(c, v)
    assert "**fail**" in text


def test_glitch_jumps_count(tmp_path):
    rec = shift(bot="x:1", seed=1)
    rec["frames"][10][1] += 50
    s = records.load(write(tmp_path, "g.json", rec))
    assert scorecard.jumps(s) == 2      # there and back
