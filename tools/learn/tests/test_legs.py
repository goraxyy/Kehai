"""C4.3: a bot's legs come from its logged choices; a person's are cut from the track."""
import legs
import records
from fixtures import shift, write


def test_bot_legs_are_its_logged_choices(tmp_path):
    rec = shift(bot="reckless:x", seconds=30, choices=[(1.0, "Mop|Hurry|Nearest|mop|s1|1|heuristic|ok", 9.0, 3.0),
                                                        (12.0, "ServeTill|Walk|Nearest|serve|c1|1|heuristic|cut", 2.0, 2.0)])
    ls = legs.legs([records.load(write(str(tmp_path), "b.json", rec))])
    assert [l["what"] for l in ls] == ["Mop", "ServeTill"]
    assert ls[0]["to"] == [9.0, 3.0] and ls[0]["found"] == "logged"
    assert ls[1]["ok"] is False
    assert ls[0]["metres"] > 0 and ls[0]["t1"] == 12.0


def test_a_persons_legs_are_cut_at_jobs_and_stops(tmp_path):
    rec = shift(player="me", seconds=40, jobs=("served a customer", "mopped a spill"))
    for f in rec["frames"]:
        if 20 <= f[0] <= 25:
            f[4] = 0                      # standing still five seconds
    ls = legs.legs([records.load(write(str(tmp_path), "p.json", rec))])
    assert all(l["found"] == "track" for l in ls)
    assert {"served a customer", "mopped a spill"} <= {l["what"] for l in ls}
    assert any(l["what"] == "stop" for l in ls)
