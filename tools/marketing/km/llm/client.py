"""One Claude call for one pipeline step.

- The answer is JSON in the step's schema (structured outputs, `output_config.format`), checked
  again here along with the step's own rules; a failing answer gets one repair round.
- The system prompt is the style guide and the game reference (shared by every step, cached),
  then the step's instructions (cached too: a run repeats a step).
- Every call is priced and logged to logs/llm_costs.csv; the monthly cap is checked first.
- A refusal (`stop_reason: "refusal"`) stops the step and tells the owner; server-side fallback
  (`fallbacks: "default"`) gets the first chance to rescue it.
- Dry run: the request is written to logs/llm_requests/ with its estimated cost; nothing is sent.
- KEHAI_LLM_REPLAY=<folder> answers from files instead of the API (<step>.json, or <step>.1.json,
  <step>.2.json… in order), for tests and for running the pipeline without a key.
- Pictures (a reference video's frames) go before the text, each after a line saying what it is;
  a dry run logs their paths, not their bytes.
"""
from __future__ import annotations

import base64
import datetime as dt
import json
import sys
from dataclasses import dataclass, field, replace
from pathlib import Path
from typing import Callable

from jsonschema import Draft202012Validator

from .. import alerts, env, paths
from . import ledger, pricing
from .steps import Step

FALLBACK_BETA = "server-side-fallback-2026-07-01"


def schema(name: str) -> dict:
    """A step's answer schema (schemas/llm/<name>.schema.json) as the API takes it."""
    s = json.loads((paths.SCHEMAS / "llm" / f"{name}.schema.json").read_text(encoding="utf-8"))
    for key in ("$schema", "$id", "title"):
        s.pop(key, None)
    return s


class Refused(Exception):
    """Claude declined the request."""


class ApiError(Exception):
    """The API couldn't answer (network, rate limit, an error on its side, a bad request)."""


class Invalid(Exception):
    """The answer still broke the step's rules after the repair round."""

    def __init__(self, message: str, problems: list[str], saved: Path | None = None):
        super().__init__(message)
        self.problems = problems
        self.saved = saved


@dataclass
class Request:
    step: Step
    system: list[dict]
    user: str
    schema: dict
    ref: str = ""
    max_tokens: int | None = None
    # [{"label": "frame at 1.5 s", "path": "/…/x.jpg", "width": 384, "height": 682}]
    images: list[dict] = field(default_factory=list)

    def content(self, inline: bool = True) -> str | list[dict]:
        if not self.images:
            return self.user
        blocks: list[dict] = []
        for im in self.images:
            data = base64.b64encode(Path(im["path"]).read_bytes()).decode("ascii") if inline else f"<{im['path']}>"
            blocks.append({"type": "text", "text": im["label"]})
            blocks.append({"type": "image", "source": {"type": "base64", "media_type": "image/jpeg", "data": data}})
        blocks.append({"type": "text", "text": self.user})
        return blocks

    def params(self, fallback: bool, inline: bool = True) -> dict:
        p = {
            "model": self.step.resolved_model(),
            "max_tokens": self.max_tokens or self.step.max_tokens,
            "system": self.system,
            "messages": [{"role": "user", "content": self.content(inline)}],
            "output_config": {"effort": self.step.effort,
                              "format": {"type": "json_schema", "schema": self.schema}},
        }
        if fallback:
            p["betas"] = [FALLBACK_BETA]
            p["fallbacks"] = "default"
        return p

    def estimate(self) -> tuple[int, float]:
        """High guesses: input tokens, and dollars with no cache hits and the usual output."""
        text = "".join(b["text"] for b in self.system) + self.user + json.dumps(self.schema)
        text += "".join(im["label"] for im in self.images)
        tokens = pricing.estimate_tokens(text) + sum(image_tokens(im["width"], im["height"]) for im in self.images)
        return tokens, pricing.cost(self.step.resolved_model(), tokens, self.step.expect)


def image_tokens(width: int, height: int) -> int:
    """About what a picture costs as input: width × height / 750."""
    return max(1, round(width * height / 750))


@dataclass
class Reply:
    text: str
    stop: str
    model: str
    request_id: str = ""
    usage: dict = field(default_factory=dict)
    iterations: list[dict] = field(default_factory=list)
    stop_details: dict | None = None
    fallback: bool = False

    def cost(self) -> float:
        if self.iterations:
            return round(sum(pricing.cost(it.get("model") or self.model, it.get("input_tokens", 0),
                                          it.get("output_tokens", 0), it.get("cache_creation_input_tokens", 0),
                                          it.get("cache_read_input_tokens", 0))
                             for it in self.iterations if it.get("type") in ("message", "fallback_message")), 6)
        u = self.usage
        return pricing.cost(self.model, u.get("input_tokens", 0), u.get("output_tokens", 0),
                            u.get("cache_creation_input_tokens", 0) or 0, u.get("cache_read_input_tokens", 0) or 0)


class SdkTransport:
    """The Claude API, through the official SDK, with the key from .env (the capped workspace's)."""

    name = "api"

    def __init__(self) -> None:
        import anthropic

        self.anthropic = anthropic
        self.client = anthropic.Anthropic(api_key=env.need("ANTHROPIC_API_KEY", "the Claude steps"), max_retries=3)
        self.fallback = (env.get("KEHAI_LLM_FALLBACK", "default") or "default") != "off"

    def send(self, request: Request) -> Reply:
        a = self.anthropic
        try:
            try:
                return self._send(request.params(self.fallback))
            except a.BadRequestError as e:
                if self.fallback and "fallback" in str(e).lower():
                    print("llm: the API refused the fallback option; sending without it", file=sys.stderr)
                    self.fallback = False
                    return self._send(request.params(False))
                raise
        except a.AuthenticationError as e:
            raise env.Missing(f"the Claude API refused ANTHROPIC_API_KEY ({e.message}): check the key in "
                              "tools/marketing/.env") from e
        except a.PermissionDeniedError as e:
            raise env.Missing(f"the key isn't allowed this request ({e.message}): check its workspace in the Console") from e
        except a.RateLimitError as e:
            raise ApiError(f"rate limited by the Claude API even after retries; try again later ({e.message})") from e
        except a.APIConnectionError as e:
            raise ApiError(f"can't reach the Claude API ({e}); is the network up?") from e
        except a.APIStatusError as e:
            raise ApiError(f"the Claude API answered {e.status_code}: {e.message} (request {e.request_id})") from e

    def _send(self, params: dict) -> Reply:
        with self.client.beta.messages.stream(**params) as stream:
            msg = stream.get_final_message()
        text = next((b.text for b in msg.content if b.type == "text"), "")
        usage = msg.usage.model_dump(exclude_none=True)
        iterations = usage.pop("iterations", None) or []
        details = msg.stop_details.model_dump(exclude_none=True) if msg.stop_details else None
        return Reply(text=text, stop=msg.stop_reason or "", model=msg.model, request_id=getattr(msg, "_request_id", None) or msg.id,
                     usage=usage, iterations=iterations, stop_details=details,
                     fallback=any(it.get("type") == "fallback_message" for it in iterations))


class ReplayTransport:
    """Answers from files: the JSON the model would have returned, or {"_replay": {...}} to act out
    a refusal or a cut-off answer ({"_replay": {"stop_reason": "refusal"}})."""

    name = "replay"

    def __init__(self, folder: Path) -> None:
        self.folder = folder
        self.used: dict[str, int] = {}

    def send(self, request: Request) -> Reply:
        name = request.step.name
        n = self.used.get(name, 0) + 1
        self.used[name] = n
        file = self.folder / f"{name}.{n}.json"
        if not file.exists():
            file = self.folder / f"{name}.json"
        if not file.exists():
            raise ApiError(f"no replay answer for {name} in {self.folder} ({name}.json or {name}.{n}.json)")
        data = json.loads(file.read_text(encoding="utf-8"))
        stop = "end_turn"
        details = None
        if isinstance(data, dict) and "_replay" in data:
            meta = data["_replay"]
            stop = meta.get("stop_reason", stop)
            details = meta.get("stop_details")
            data = meta.get("output", {})
        text = data if isinstance(data, str) else json.dumps(data, ensure_ascii=False)
        tokens, _ = request.estimate()
        return Reply(text=text, stop=stop, model="replay", request_id=f"replay:{file.name}",
                     usage={"input_tokens": tokens, "output_tokens": len(text) // 3}, stop_details=details)


def default_transport():
    folder = env.get("KEHAI_LLM_REPLAY")
    return ReplayTransport(Path(folder).expanduser()) if folder else SdkTransport()


def _stamp() -> str:
    return dt.datetime.now().strftime("%Y%m%d_%H%M%S")


def call(request: Request, root: Path, transport=None, dry_run: bool = False) -> Reply | None:
    tokens, estimate = request.estimate()
    model = request.step.resolved_model()
    if dry_run:
        out = root / "logs" / "llm_requests" / f"{_stamp()}-{request.step.name}.json"
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(json.dumps(request.params(True, inline=False), indent=2, ensure_ascii=False), encoding="utf-8")
        cached = pricing.estimate_tokens("".join(b["text"] for b in request.system))
        print(f"dry run: {request.step.name} on {model} (effort {request.step.effort}): about {tokens:,} input "
              f"tokens ({cached:,} of them the cached system prompt), up to {request.max_tokens or request.step.max_tokens:,} "
              f"output; about ${estimate:.3f}. Month so far: ${ledger.month_spent(root):.2f} of ${ledger.cap():.2f}. "
              f"Request: {out}")
        return None
    transport = transport or default_transport()
    if transport.name == "api":
        ledger.check(root, estimate, request.step.name)
    reply = transport.send(request)
    usd = 0.0 if transport.name == "replay" else reply.cost()
    u = reply.usage
    ledger.record(root, {
        "time": dt.datetime.now().isoformat(timespec="seconds"), "step": request.step.name, "ref": request.ref,
        "model": reply.model, "request_id": reply.request_id,
        "input_tokens": u.get("input_tokens", 0), "output_tokens": u.get("output_tokens", 0),
        "cache_write_tokens": u.get("cache_creation_input_tokens", 0) or 0,
        "cache_read_tokens": u.get("cache_read_input_tokens", 0) or 0,
        "usd": f"{usd:.6f}", "stop_reason": reply.stop,
        "note": "replay" if transport.name == "replay" else ("fallback" if reply.fallback else ""),
    })
    if transport.name == "api":
        ledger.after_call(root)
    return reply


def _repair(request: Request, previous: str, problems: list[str]) -> Request:
    listed = "\n".join(f"- {p}" for p in problems[:40])
    user = (f"{request.user}\n\n<previous_answer>\n{previous}\n</previous_answer>\n\n"
            f"<problems>\nThat answer can't be used as it is:\n{listed}\n</problems>\n\n"
            "Return the whole answer again with these fixed. Keep everything that was already right.")
    return replace(request, user=user)


def _save_failure(root: Path, request: Request, text: str, problems: list[str]) -> Path:
    out = root / "logs" / "llm_failed" / f"{_stamp()}-{request.step.name}.json"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps({"step": request.step.name, "ref": request.ref, "problems": problems, "answer": text},
                              indent=2, ensure_ascii=False), encoding="utf-8")
    return out


def ask(request: Request, root: Path, checks: Callable[[dict], list[str]] | None = None, transport=None,
        dry_run: bool = False) -> dict | None:
    """The step's answer as data, checked against its schema and `checks`; None on a dry run."""
    transport = transport if transport is not None else (None if dry_run else default_transport())
    reply = call(request, root, transport, dry_run)
    if reply is None:
        return None
    validator = Draft202012Validator(request.schema)
    for attempt in (1, 2):
        if reply.stop == "refusal":
            category = (reply.stop_details or {}).get("category")
            alerts.alert(root, "llm_refusal", f"Claude declined {request.step.name} ({request.ref or 'no ref'})"
                         + (f", category {category}" if category else "") + ".",
                         step=request.step.name, ref=request.ref, details=reply.stop_details)
            raise Refused(f"Claude declined {request.step.name}" + (f" ({category})" if category else ""))
        data = None
        if reply.stop == "max_tokens":
            problems = ["the answer was cut off at max_tokens"]
        else:
            try:
                data = json.loads(reply.text)
                problems = [f"/{'/'.join(map(str, e.absolute_path))}: {e.message}"
                            for e in validator.iter_errors(data)]
                if not problems and checks:
                    problems = checks(data)
            except json.JSONDecodeError as e:
                problems = [f"not JSON: {e}"]
        if not problems:
            return data
        if attempt == 2:
            break
        print(f"llm: {request.step.name}: {len(problems)} problem(s), asking once more: {problems[0]}", file=sys.stderr)
        if reply.stop == "max_tokens":
            request = replace(request, max_tokens=min(2 * (request.max_tokens or request.step.max_tokens), 128000))
        else:
            request = _repair(request, reply.text, problems)
        reply = call(request, root, transport)
    saved = _save_failure(root, request, reply.text, problems)
    raise Invalid(f"{request.step.name}: the answer still has {len(problems)} problem(s) after a repair round "
                  f"(first: {problems[0]}); saved to {saved}", problems, saved)
