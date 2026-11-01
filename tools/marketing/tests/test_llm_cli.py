"""The `claude -p` backend, against a stand-in for Claude Code that records what it was given."""
import json
import os
import stat
import sys

import pytest

from km import env
from km.llm import client, ledger, prompts
from km.llm.steps import step


def fake_claude(tmp_path, result: dict | None, code: int = 0, noise: str = '{"type": "system", "subtype": "init"}'):
    """An executable that saves its arguments, stdin, folder and environment, and prints `result`."""
    saved = tmp_path / "seen.json"
    answer = tmp_path / "answer.txt"
    answer.write_text("\n".join([noise, "not json at all", json.dumps(result) if result else ""]), encoding="utf-8")
    script = tmp_path / "claude"
    script.write_text(f"""#!{sys.executable}
import json, os, pathlib, sys
args = sys.argv[1:]
system = pathlib.Path(args[args.index("--system-prompt-file") + 1]).read_text(encoding="utf-8")
json.dump({{"args": args, "stdin": sys.stdin.read(), "cwd": os.getcwd(), "system": system, "env": dict(os.environ)}},
          open({str(saved)!r}, "w"))
print(open({str(answer)!r}).read())
sys.exit({code})
""", encoding="utf-8")
    script.chmod(script.stat().st_mode | stat.S_IEXEC)
    return script, saved


def success(output=None, **extra) -> dict:
    return {"type": "result", "subtype": "success", "is_error": False, "stop_reason": "end_turn", "result": "",
            "structured_output": {"items": []} if output is None else output, "session_id": "s-1",
            "usage": {"input_tokens": 1000, "output_tokens": 2000, "cache_read_input_tokens": 0,
                      "cache_creation_input_tokens": 0, "server_tool_use": {"web_search_requests": 0}}, **extra}


def failure(text: str, status=None) -> dict:
    return {"type": "result", "subtype": "success", "is_error": True, "stop_reason": "stop_sequence", "result": text,
            "api_error_status": status, "usage": {}}


def request(images=None) -> client.Request:
    return client.Request(step=step("translate"), system=prompts.system("translate"), user="Say something.",
                          schema=client.schema("translate"), ref="test", images=images or [])


@pytest.fixture
def backend(monkeypatch, tmp_path):
    monkeypatch.setenv("KEHAI_LLM_BACKEND", "cli")
    monkeypatch.delenv("KEHAI_LLM_REPLAY", raising=False)
    monkeypatch.setattr(env, "ENV_FILE", tmp_path / "no.env")
    monkeypatch.setattr(env, "_loaded", False)

    def use(result, code=0):
        script, saved = fake_claude(tmp_path, result, code)
        monkeypatch.setenv("KEHAI_CLAUDE_BIN", str(script))
        return saved

    return use


def seen(saved) -> dict:
    return json.loads(saved.read_text(encoding="utf-8"))


def test_the_request_is_the_steps_own_with_claude_codes_extras_left_out(backend, tmp_path, monkeypatch):
    saved = backend(success())
    monkeypatch.setenv("ANTHROPIC_API_KEY", "sk-should-not-be-used")
    monkeypatch.setenv("CLAUDECODE", "1")
    assert client.ask(request(), tmp_path / "root") == {"items": []}
    s = seen(saved)
    args = s["args"]
    assert args[0] == "-p"
    assert args[args.index("--model") + 1] == "claude-opus-5-5" and args[args.index("--effort") + 1] == "low"
    assert json.loads(args[args.index("--json-schema") + 1]) == client.schema("translate")
    assert args[args.index("--tools") + 1] == "" and args[args.index("--setting-sources") + 1] == ""
    for flag in ("--strict-mcp-config", "--no-session-persistence", "--disable-slash-commands"):
        assert flag in args
    assert "Say something." in s["stdin"] and s["system"] == "\n\n".join(b["text"] for b in request().system)
    assert sorted(os.listdir(tmp_path)).count("root") == 1 and str(tmp_path) not in s["cwd"], "an empty folder of its own"
    assert "ANTHROPIC_API_KEY" not in s["env"], "a key would bill the API instead of the plan"
    assert "CLAUDECODE" not in s["env"]
    assert s["env"]["CLAUDE_CODE_MAX_OUTPUT_TOKENS"] == str(step("translate").max_tokens)
    assert s["env"]["CLAUDE_CODE_DISABLE_CLAUDE_MDS"] == "1" and s["env"]["CLAUDE_CODE_DISABLE_AUTO_MEMORY"] == "1"


def test_a_sign_in_token_from_the_env_file_reaches_claude_code(backend, tmp_path, monkeypatch):
    saved = backend(success())
    monkeypatch.setenv("CLAUDE_CODE_OAUTH_TOKEN", "token")
    client.ask(request(), tmp_path / "root")
    assert seen(saved)["env"]["CLAUDE_CODE_OAUTH_TOKEN"] == "token"


def test_pictures_go_before_the_text(backend, tmp_path):
    saved = backend(success())
    picture = tmp_path / "frame.jpg"
    picture.write_bytes(b"jpeg")
    client.ask(request([{"label": "frame at 1 s", "path": str(picture), "width": 10, "height": 10}]), tmp_path / "root")
    content = json.loads(seen(saved)["stdin"])["message"]["content"]
    assert [b["type"] for b in content] == ["text", "image", "text"]
    assert content[1]["source"]["data"] == "anBlZw==" and content[2]["text"] == "Say something."


def test_the_plan_costs_nothing_but_the_ledger_keeps_what_it_would_have_cost(backend, tmp_path, monkeypatch):
    backend(success())
    monkeypatch.setenv("KEHAI_LLM_MONTHLY_USD", "0.000001")
    root = tmp_path / "root"
    client.ask(request(), root)
    row = ledger.rows(root)[0]
    assert float(row["usd"]) == 0 and ledger.month_spent(root) == 0, "the API's cap doesn't count plan use"
    assert row["request_id"] == "s-1" and row["input_tokens"] == "1000" and row["output_tokens"] == "2000"
    assert row["note"] == "plan, about $0.0440 at API prices"


def test_not_being_signed_in_says_what_to_do(backend, tmp_path):
    backend(failure("Failed to authenticate: OAuth session expired and could not be refreshed"), code=1)
    with pytest.raises(env.Missing, match="claude auth login"):
        client.ask(request(), tmp_path / "root")


def test_the_plans_limit_makes_the_step_wait(backend, tmp_path):
    backend(failure("You've hit your limit · resets 11am", 429), code=1)
    with pytest.raises(ledger.OverBudget, match="hit your limit"):
        client.ask(request(), tmp_path / "root")


def test_a_failure_with_no_result_is_tried_again_later(backend, tmp_path):
    backend(None, code=1)
    with pytest.raises(client.ApiError, match="no result"):
        client.ask(request(), tmp_path / "root")


def test_another_error_is_tried_again_later(backend, tmp_path):
    backend(failure("Overloaded", 529), code=1)
    with pytest.raises(client.ApiError, match="Overloaded"):
        client.ask(request(), tmp_path / "root")


def test_a_refusal_stops_the_step(backend, tmp_path):
    backend(success(stop_reason="refusal", structured_output=None, result="I can't help with that."))
    with pytest.raises(client.Refused):
        client.ask(request(), tmp_path / "root")


def test_an_answer_that_gave_up_on_the_schema_gets_the_repair_round(backend, tmp_path):
    backend({"type": "result", "subtype": "error_max_structured_output_retries", "is_error": True, "usage": {}}, code=1)
    with pytest.raises(client.Invalid, match="not JSON"):
        client.ask(request(), tmp_path / "root")


def test_a_cut_off_answer_is_asked_again_with_more_room(backend, tmp_path):
    saved = backend(success(stop_reason="max_tokens", structured_output=None, result="{\"items\": ["))
    with pytest.raises(client.Invalid):
        client.ask(request(), tmp_path / "root")
    assert seen(saved)["env"]["CLAUDE_CODE_MAX_OUTPUT_TOKENS"] == str(2 * step("translate").max_tokens)


def test_the_backend_is_a_setting(backend, monkeypatch):
    backend(success())
    assert isinstance(client.default_transport(), client.CliTransport)
    monkeypatch.setenv("KEHAI_LLM_BACKEND", "api")
    monkeypatch.setenv("ANTHROPIC_API_KEY", "sk-test")
    assert isinstance(client.default_transport(), client.SdkTransport)
    monkeypatch.setenv("KEHAI_LLM_BACKEND", "nonsense")
    with pytest.raises(env.Missing, match="KEHAI_LLM_BACKEND"):
        client.default_transport()


def test_ready_follows_the_backend(backend, monkeypatch):
    backend(success())
    assert client.ready()
    monkeypatch.delenv("KEHAI_CLAUDE_BIN")
    monkeypatch.setattr(client, "claude_binary", lambda: None)
    assert not client.ready()
    with pytest.raises(env.Missing, match="Claude Code"):
        client.CliTransport()
    monkeypatch.setenv("KEHAI_LLM_BACKEND", "api")
    monkeypatch.delenv("ANTHROPIC_API_KEY", raising=False)
    assert not client.ready()
    monkeypatch.setenv("ANTHROPIC_API_KEY", "sk-test")
    assert client.ready()
