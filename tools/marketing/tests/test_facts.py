"""The fact sheet: its evidence is still true, no prompt or doc breaks it, and every step reads it."""
import pytest

from km import facts, paths
from km.llm import client, ledger, prompts
from km.llm.steps import STEPS, step

from helpers import replay


def test_every_facts_evidence_is_still_in_the_repository():
    assert facts.stale() == []


def test_a_change_in_the_game_shows_up_as_a_stale_fact(tmp_path):
    problems = facts.stale(tmp_path)
    assert problems and all("is gone" in p for p in problems)
    first = facts.load()[0]["evidence"][0]
    file = tmp_path / first["file"]
    file.parent.mkdir(parents=True)
    file.write_text("something else", encoding="utf-8")
    assert any("no longer says" in p for p in facts.stale(tmp_path))


@pytest.mark.parametrize("path", sorted(paths.HERE.glob("prompts/*.md")) + [facts.REPO / "docs/agents/marketing/MARKETING.md"],
                         ids=lambda p: p.name)
def test_no_prompt_or_marketing_doc_uses_a_banned_phrase(path):
    assert facts.banned_in(path.read_text(encoding="utf-8")) == []


def test_public_steps_are_real_steps():
    assert set(facts.PUBLIC_STEPS) <= set(STEPS)


def test_phrases_are_matched_whatever_the_case_or_apostrophe():
    assert facts.banned_in("She doesn’t rest. SHE GIVES YOU OVERTIME.") == ["gives you overtime"]
    assert facts.banned_in("She doesn't kill you.") == ["she doesn't kill you"]
    assert facts.banned_in("Her touch ends the shift.") == []


def test_every_step_reads_the_fact_sheet_with_the_names_filled_in():
    shared = prompts.system("write_short")[0]["text"]
    for fact in facts.load():
        assert prompts.fill(fact["say"]) in shared
    assert "{{" not in shared
    assert "Don't claim" in shared and "gives you overtime" in shared


def public_request(name="package"):
    return client.Request(step=step(name), system=prompts.system(name), user="Write it.",
                          schema={"type": "object", "properties": {"title": {"type": "string"}},
                                  "required": ["title"], "additionalProperties": False}, ref="test")


def test_a_public_answer_with_a_banned_phrase_is_sent_back_once(tmp_path):
    t = client.ReplayTransport(replay(tmp_path / "r", {"package.1": {"title": "She gives you overtime"},
                                                       "package.2": {"title": "One touch and the shift starts again"}}))
    sent = []
    original = t.send
    t.send = lambda r: sent.append(r.user) or original(r)
    data = client.ask(public_request(), tmp_path / "root", transport=t)
    assert data == {"title": "One touch and the shift starts again"}
    assert len(ledger.rows(tmp_path / "root")) == 2 and "gives you overtime" in sent[1]


def test_a_private_step_may_use_the_phrases(tmp_path):
    t = client.ReplayTransport(replay(tmp_path / "r", {"weekly_report": {"title": "She gives you overtime"}}))
    assert client.ask(public_request("weekly_report"), tmp_path / "root", transport=t)["title"]
