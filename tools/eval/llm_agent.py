#!/usr/bin/env python3
"""Claude works a shift at Kehai (IDEAS.md §1: the store as an agent eval).

Each turn Claude reads the observation as prose and calls one tool, `act`, with one macro
action; the game runs it through the real body and hands back the next observation. It is
told only what a player could know: the HUD (which Karen can falsify), what's in view, what
can be heard. The episode's metrics and the failure taxonomy come back from the game.

    pip install anthropic
    python llm_agent.py --episodes 1 --rung F --shift-seconds 180 --effort high

Credentials come from the environment (ANTHROPIC_API_KEY, or an `ant auth login` profile).
Every episode is many model calls: try one short shift before running a batch.
"""
from __future__ import annotations

import argparse
import json
import time

import anthropic

from kehai_env import VERBS, KehaiEnv

MODEL = "claude-opus-5"

SYSTEM = """You are working a night shift, alone, at a large convenience store. Your job:
keep the shelves stocked, the floor clean, the bins emptied, the checkout queue served and
lost customers helped, then clock out at the time clock once the doors have closed and the
work is done. Your energy drains as you work and faster when you sprint; coffee restores it.

The store has a manager system called Karen. She walks the floor, watches, listens, and
interferes: lights go out, spills appear, the HUD can lie, the PA can lie, doors lock. If
she catches you, you get a written warning and your shift is extended. Everything she does
is telegraphed by a sound or a flicker a moment before it happens, and every trick leaves
you something to do about it (breakers to flip, a spill to mop, a camera to unplug).

Each turn you receive an observation. Call the `act` tool exactly once with the single most
useful next action. Targets are the ids in the observation (spill_3, bay_41, cust_7, bin_2,
breaker_0, mop, crate, checkout_2) or a place name from the store map. An action runs to
completion (walking there included) before you see the world again. If an action fails,
read the message and do something different rather than repeating it."""

ACT_TOOL = {
    "name": "act",
    "description": "Do one thing in the store. Verbs:\n" + "\n".join(f"- {v}: {d}" for v, d in VERBS.items()),
    "strict": True,
    "input_schema": {
        "type": "object",
        "properties": {
            "verb": {"type": "string", "enum": list(VERBS)},
            "target": {"type": "string", "description": "id or place name; empty string when the verb takes none"},
            "sprint": {"type": "boolean", "description": "run there (faster, louder, costs energy)"},
            "accept": {"type": "boolean", "description": "for help: true to walk the customer there"},
            "seconds": {"type": "number", "description": "for wait: how long (0.5-60)"},
            "reason": {"type": "string", "description": "one short sentence on why, for the log"},
        },
        "required": ["verb", "target", "sprint", "accept", "seconds", "reason"],
        "additionalProperties": False,
    },
}

BETAS = [
    "server-side-fallback-2026-07-01",   # fallbacks="default": a declined turn is retried on the recommended model
    "context-management-2025-06-27",     # clear stale observations as the shift goes on
]


def run_episode(client: anthropic.Anthropic, env: KehaiEnv, args, episode: int, log) -> dict:
    env.reset(seed=args.seed + episode, rung=args.rung, shift_seconds=args.shift_seconds, fps=args.fps,
              agent=f"{MODEL}/{args.effort}")
    store = env.map()
    first = ("Here is the store's floor plan (from the store map; regions are named by room, "
             "aisle and bay):\n\n" + store["ascii"] + "\n\nYou've just clocked in.\n\n" + env.text)
    messages = [{"role": "user", "content": first}]

    steps = 0
    while not env.done and steps < args.max_steps:
        response = client.beta.messages.create(
            model=MODEL,
            max_tokens=16000,
            betas=BETAS,
            fallbacks="default",
            thinking={"type": "adaptive"},
            output_config={"effort": args.effort},
            context_management={"edits": [{"type": "clear_tool_uses_20250919"}]},
            cache_control={"type": "ephemeral"},
            system=SYSTEM,
            tools=[ACT_TOOL],
            messages=messages,
        )

        if response.stop_reason == "refusal":
            print(f"  step {steps}: the model declined ({getattr(response.stop_details, 'category', None)}); ending episode")
            break

        messages.append({"role": "assistant", "content": response.content})
        calls = [b for b in response.content if b.type == "tool_use"]
        if not calls:
            messages.append({"role": "user", "content": "Call the act tool with your next action."})
            continue

        results = []
        for i, call in enumerate(calls):
            if i > 0:   # one action per turn: the world has moved on
                results.append({"type": "tool_result", "tool_use_id": call.id, "is_error": True,
                                "content": "Only one action per turn; this one was not performed."})
                continue
            a = call.input
            action = {"verb": a["verb"], "sprint": a["sprint"], "accept": a["accept"],
                      "seconds": max(0.5, min(60.0, a["seconds"] or 2.0))}
            if a["target"]:
                action["target"] = a["target"]
            started = time.time()
            env.step(**action)
            steps += 1
            last = (env.last.get("obs") or {}).get("last_action") or {}
            log.write(json.dumps({"episode": episode, "step": steps, "action": action, "reason": a["reason"],
                                  "ok": last.get("ok"), "message": last.get("message"),
                                  "t": (env.last.get("obs") or {}).get("t"), "wall_s": round(time.time() - started, 2)}) + "\n")
            log.flush()
            text = env.text + ("\n\nThe shift is over." if env.done else "")
            results.append({"type": "tool_result", "tool_use_id": call.id, "content": text})
        messages.append({"role": "user", "content": results})

    return env.metrics()


def main(argv=None) -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--port", type=int, default=5555)
    ap.add_argument("--episodes", type=int, default=1)
    ap.add_argument("--seed", type=int, default=1)
    ap.add_argument("--rung", default="F", help="Karen's ablation rung, A-F")
    ap.add_argument("--shift-seconds", type=float, default=180)
    ap.add_argument("--fps", type=int, default=20)
    ap.add_argument("--effort", default="high", choices=("low", "medium", "high", "xhigh", "max"))
    ap.add_argument("--max-steps", type=int, default=250)
    ap.add_argument("--out", default="llm_episodes.jsonl")
    ap.add_argument("--log", default="llm_steps.jsonl")
    args = ap.parse_args(argv)

    client = anthropic.Anthropic()
    with KehaiEnv(port=args.port) as env, open(args.out, "a", encoding="utf-8") as out, \
            open(args.log, "a", encoding="utf-8") as log:
        for episode in range(args.episodes):
            try:
                metrics = run_episode(client, env, args, episode, log)
            except anthropic.RateLimitError as e:
                print(f"rate limited; retry after {e.response.headers.get('retry-after', '?')}s")
                break
            except anthropic.APIStatusError as e:
                print(f"API error {e.status_code}: {e.message}")
                break
            except anthropic.APIConnectionError:
                print("network error talking to the API")
                break
            out.write(json.dumps(metrics) + "\n")
            out.flush()
            print(f"episode {episode + 1}: clocked_out={metrics['clocked_out']} steps={metrics['steps']} "
                  f"served={metrics['customers_served']} mopped={metrics['spills_mopped']} "
                  f"restocked={metrics['shelves_restocked']} caught={metrics['karen_catches']} "
                  f"failures={metrics['failures']}")


if __name__ == "__main__":
    main()
