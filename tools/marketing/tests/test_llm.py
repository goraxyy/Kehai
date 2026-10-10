"""The Claude client: prices, the ledger and the monthly cap, dry runs, replays, the repair round,
refusals, and the answer schemas staying inside what structured outputs accept."""
import csv
import datetime as dt
import glob
import json
import re

import pytest

from km import alerts, env, paths
from km.llm import client, ledger, pricing, prompts
from km.llm.steps import STEPS, step

from helpers import replay


@pytest.fixture
def root(tmp_path, monkeypatch):
    monkeypatch.delenv("KEHAI_LLM_REPLAY", raising=False)
    monkeypatch.delenv("KEHAI_LLM_MONTHLY_USD", raising=False)
    monkeypatch.delenv("KEHAI_LLM_MODEL", raising=False)
    return tmp_path / "marketing"


def request(schema=None, user="Say something.") -> client.Request:
    return client.Request(step=step("translate"), system=prompts.system("translate"), user=user,
                          schema=schema or client.schema("translate"), ref="test")


def test_prices_match_the_published_rates():
    # Opus 5.5: $4 in, $20 out, $0.20 cache reads, cache writes 1.25x input.
    assert pricing.cost("claude-opus-5-5", 1_000_000, 0) == pytest.approx(4.0)
    assert pricing.cost("claude-opus-5-5", 0, 1_000_000) == pytest.approx(20.0)
    assert pricing.cost("claude-opus-5-5", 0, 0, cache_read=1_000_000) == pytest.approx(0.20)
    assert pricing.cost("claude-opus-5-5", 0, 0, cache_write=1_000_000) == pytest.approx(5.0)
    assert pricing.cost("claude-sonnet-5-5", 1_000_000, 1_000_000) == pytest.approx(12.0)
    assert pricing.cost("some-future-model", 1_000_000, 0) == pytest.approx(10.0), "unknown models priced high"


def test_every_step_runs_on_opus_with_an_explicit_effort():
    for s in STEPS.values():
        assert s.model == "claude-opus-5-5"
        assert s.effort in ("low", "medium", "high", "xhigh", "max")
        assert s.expect < s.max_tokens


def test_the_request_asks_for_structured_output_effort_and_fallbacks(root):
    p = request().params(fallback=True)
    assert p["model"] == "claude-opus-5-5"
    assert p["output_config"]["effort"] == "low"
    assert p["output_config"]["format"]["type"] == "json_schema"
    assert "$schema" not in p["output_config"]["format"]["schema"]
    assert p["fallbacks"] == "default" and p["betas"] == ["server-side-fallback-2026-07-01"]
    assert "thinking" not in p, "Opus 5.5 thinks adaptively; disabling it is a 400"
    assert [b.get("cache_control") for b in p["system"]] == [{"type": "ephemeral"}] * 2
    assert "fallbacks" not in request().params(fallback=False)


def test_the_shared_system_prompt_is_the_same_for_every_step():
    firsts = {prompts.system(name)[0]["text"] for name in ("pick_moments", "write_short", "translate", "package")}
    assert len(firsts) == 1, "one cache entry serves a whole run"
    assert pricing.estimate_tokens(firsts.pop()) > 1024, "long enough to be cached"


def test_prompts_take_every_name_from_brand_json():
    brand = json.loads(paths.BRAND.read_text(encoding="utf-8"))
    names = [brand["game"]["name"], brand["game"]["japanese"], brand["antagonist"]["name"],
             brand["antagonist"]["japanese"], brand["studio"], brand["antagonist"]["russian"], brand["game"]["russian"]]
    for f in sorted((paths.HERE / "prompts").glob("*.md")):
        raw = f.read_text(encoding="utf-8")
        for name in names:
            assert name not in raw, f"{f.name} spells out {name}: use its {{{{placeholder}}}}"
        filled = prompts.text(f.stem)
        assert "{{" not in filled, f"{f.name} has an unknown placeholder"


@pytest.mark.parametrize("path", sorted(glob.glob(str(paths.SCHEMAS / "llm" / "*.schema.json"))))
def test_answer_schemas_stay_inside_what_structured_outputs_accept(path):
    schema = json.loads(open(path, encoding="utf-8").read())
    found = {"optional": 0, "unions": 0}

    def walk(node, where):
        if isinstance(node, dict):
            if node.get("type") == "object":
                assert node.get("additionalProperties") is False, f"{where}: objects need additionalProperties false"
                found["optional"] += len(set(node.get("properties", {})) - set(node.get("required", [])))
            if "anyOf" in node or isinstance(node.get("type"), list):
                found["unions"] += 1
            for bad in ("oneOf", "minimum", "maximum", "minLength", "maxLength", "propertyNames", "unevaluatedProperties",
                        "patternProperties"):
                assert bad not in node, f"{where}: structured outputs don't take {bad}"
            if "minItems" in node:
                assert node["minItems"] in (0, 1), f"{where}: minItems only 0 or 1"
            for k, v in node.items():
                walk(v, f"{where}/{k}")
        elif isinstance(node, list):
            for i, v in enumerate(node):
                walk(v, f"{where}/{i}")

    walk(schema, path.rsplit("/", 1)[-1])
    assert found["optional"] <= 24 and found["unions"] <= 16, found


def test_a_dry_run_writes_the_request_and_sends_nothing(root, capsys):
    assert client.ask(request(), root, dry_run=True) is None
    saved = list((root / "logs" / "llm_requests").glob("*-translate.json"))
    assert len(saved) == 1
    body = json.loads(saved[0].read_text(encoding="utf-8"))
    assert body["messages"][0]["content"] == "Say something."
    assert not ledger.ledger_file(root).exists()
    assert "about $" in capsys.readouterr().out


def test_without_a_key_the_api_says_how_to_set_it_up(root, monkeypatch):
    monkeypatch.delenv("ANTHROPIC_API_KEY", raising=False)
    monkeypatch.setattr(env, "ENV_FILE", root / "no.env")
    monkeypatch.setattr(env, "_loaded", False)
    with pytest.raises(env.Missing, match="ANTHROPIC_API_KEY"):
        client.SdkTransport()


def test_a_replayed_answer_is_checked_and_logged(root, tmp_path):
    t = client.ReplayTransport(replay(tmp_path / "r", {"translate": {"items": [{"id": "t1", "text": "Привет"}]}}))
    data = client.ask(request(), root, transport=t)
    assert data == {"items": [{"id": "t1", "text": "Привет"}]}
    rows = ledger.rows(root)
    assert len(rows) == 1 and rows[0]["step"] == "translate" and rows[0]["note"] == "replay"
    assert float(rows[0]["usd"]) == 0, "replays cost nothing"


def test_a_broken_answer_gets_one_repair_round(root, tmp_path):
    t = client.ReplayTransport(replay(tmp_path / "r", {
        "translate.1": {"items": [{"id": "t1", "text": ""}]},
        "translate.2": {"items": [{"id": "t1", "text": "Привет"}]},
    }))
    seen = []

    def checks(d):
        seen.append(d)
        return [] if d["items"][0]["text"] else ["item t1: empty"]

    assert client.ask(request(), root, checks, transport=t)["items"][0]["text"] == "Привет"
    assert len(seen) == 2 and len(ledger.rows(root)) == 2


def test_an_answer_still_broken_after_the_repair_is_saved_and_refused(root, tmp_path):
    t = client.ReplayTransport(replay(tmp_path / "r", {"translate": {"items": []}}))
    with pytest.raises(client.Invalid) as e:
        client.ask(request(), root, lambda d: ["no items"], transport=t)
    assert e.value.saved.exists() and e.value.problems == ["no items"]


def test_an_answer_outside_the_schema_counts_as_broken(root, tmp_path):
    t = client.ReplayTransport(replay(tmp_path / "r", {"translate.1": {"wrong": 1}, "translate.2": {"items": []}}))
    assert client.ask(request(), root, transport=t) == {"items": []}


def test_a_refusal_stops_the_step_and_tells_the_owner(root, tmp_path):
    t = client.ReplayTransport(replay(tmp_path / "r", {"translate": {"_replay": {
        "stop_reason": "refusal", "stop_details": {"type": "refusal", "category": "cyber"}}}}))
    with pytest.raises(client.Refused, match="cyber"):
        client.ask(request(), root, transport=t)
    assert [a["kind"] for a in alerts.pending(root)] == ["llm_refusal"]


def test_a_cut_off_answer_is_asked_again_with_more_room(root, tmp_path):
    t = client.ReplayTransport(replay(tmp_path / "r", {
        "translate.1": {"_replay": {"stop_reason": "max_tokens", "output": "{\"items\": ["}},
        "translate.2": {"items": []},
    }))
    sent = []
    original = t.send
    t.send = lambda r: sent.append(r.max_tokens) or original(r)
    assert client.ask(request(), root, transport=t) == {"items": []}
    assert sent == [None, 2 * step("translate").max_tokens]


class Api:
    """Stands in for the API: counts the calls and answers with a set usage."""

    name = "api"

    def __init__(self, usage):
        self.usage, self.calls = usage, 0

    def send(self, r):
        self.calls += 1
        return client.Reply(text='{"items": []}', stop="end_turn", model="claude-opus-5-5", request_id=f"req_{self.calls}",
                            usage=dict(self.usage))


def test_every_call_is_priced_into_the_ledger(root):
    api = Api({"input_tokens": 1000, "output_tokens": 2000, "cache_creation_input_tokens": 4000, "cache_read_input_tokens": 0})
    client.ask(request(), root, transport=api)
    row = ledger.rows(root)[0]
    assert row["model"] == "claude-opus-5-5" and row["request_id"] == "req_1"
    assert float(row["usd"]) == pytest.approx((1000 * 4 + 4000 * 5 + 2000 * 20) / 1e6)
    with ledger.ledger_file(root).open() as f:
        assert next(csv.reader(f)) == ledger.COLUMNS


def test_a_fallback_is_priced_by_the_model_that_answered(root):
    reply = client.Reply(text="{}", stop="end_turn", model="claude-opus-4-8", iterations=[
        {"type": "message", "model": "claude-opus-5-5", "input_tokens": 1000, "output_tokens": 0,
         "cache_creation_input_tokens": 0, "cache_read_input_tokens": 0},
        {"type": "fallback_message", "model": "claude-opus-4-8", "input_tokens": 1000, "output_tokens": 1000,
         "cache_creation_input_tokens": 0, "cache_read_input_tokens": 0}])
    assert reply.cost() == pytest.approx((1000 * 4 + 1000 * 5 + 1000 * 25) / 1e6)


def test_the_monthly_cap_stops_calls_before_they_are_sent(root, monkeypatch):
    monkeypatch.setenv("KEHAI_LLM_MONTHLY_USD", "1")
    ledger.record(root, {"time": dt.datetime.now().isoformat(timespec="seconds"), "step": "write_short", "usd": "0.99"})
    last_month = (ledger.month_start() - dt.timedelta(days=3)).isoformat(timespec="seconds")
    ledger.record(root, {"time": last_month, "step": "write_short", "usd": "50"})
    ledger.record(root, {"time": "2099-01-01T00:00:00", "step": "write_short", "usd": "50"})
    assert ledger.month_spent(root) == pytest.approx(0.99), "other months don't count"
    api = Api({"input_tokens": 1, "output_tokens": 1})
    with pytest.raises(ledger.OverBudget, match=r"\$0.99 of \$1.00"):
        client.ask(request(), root, transport=api)
    assert api.calls == 0
    with pytest.raises(ledger.OverBudget):
        client.ask(request(), root, transport=api)
    assert [a["kind"] for a in alerts.pending(root)] == ["llm_budget"], "the owner is told once a month"


def test_the_owner_hears_at_80_percent_once(root, monkeypatch):
    monkeypatch.setenv("KEHAI_LLM_MONTHLY_USD", "10")
    ledger.record(root, {"time": dt.datetime.now().isoformat(timespec="seconds"), "step": "x", "usd": "8.5"})
    ledger.after_call(root)
    ledger.after_call(root)
    assert len(alerts.pending(root)) == 1 and "8.50 of the $10.00" in alerts.pending(root)[0]["message"]


def test_the_model_can_be_overridden_for_every_step(monkeypatch):
    monkeypatch.setenv("KEHAI_LLM_MODEL", "claude-sonnet-5-5")
    assert step("write_short").resolved_model() == "claude-sonnet-5-5"


def test_names_fill_the_placeholders():
    text = prompts.fill("{{game}} {{game_jp}} {{karen}} {{karen_ru}} {{game_ru}}")
    assert re.fullmatch(r"\S+ \S+ \S+ \S+ \S+", text)
    with pytest.raises(KeyError):
        prompts.fill("{{nobody}}")
