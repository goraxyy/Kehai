"""Client for the Kehai eval environment (IDEAS.md §1).

The game runs the server — in the editor (Kehai → Eval → Start Env Server) or a build:

    Kehai.app/Contents/MacOS/Kehai -batchmode -nographics -kehai-env 5555

and this speaks its protocol: one JSON object per line over TCP on localhost, one reply per
request.

    from kehai_env import KehaiEnv
    with KehaiEnv() as env:
        obs = env.reset(seed=7, rung="F", shift_seconds=180)
        while not env.done:
            obs = env.step("mop", "spill_1")
        print(env.metrics())

Standard library only.
"""
from __future__ import annotations

import json
import socket
from typing import Any

VERBS = {
    "move_to": "walk to a place, landmark or object id (target)",
    "pick_up": "pick up a tool or item: mop, crate, flashlight, bag_n",
    "restock": "restock a shelf bay (target bay_n) — needs the crate",
    "mop": "mop a spill (target spill_n) — needs the mop",
    "serve": "serve a customer waiting at the till (target cust_n)",
    "help": "answer a customer asking where something is (target cust_n; accept=false to decline)",
    "bag_trash": "bag a full bin (target bin_n)",
    "dispose": "carry the bag you hold to the skip outside and throw it in",
    "drink_coffee": "walk to the coffee machine and drink (restores energy)",
    "clock_in": "punch in at the time clock",
    "clock_out": "punch out at the time clock (ends the shift if the work is done)",
    "flip_breaker": "flip a breaker at the panel (target breaker_n) during a blackout",
    "equip": "switch the active inventory slot (slot)",
    "toggle_flashlight": "turn the flashlight on or off (must be held)",
    "return_tool": "put a tool back at its home spot (target mop/crate/flashlight)",
    "drop": "drop what you're holding",
    "wait": "do nothing for a few seconds (seconds)",
}


class KehaiError(RuntimeError):
    pass


class KehaiEnv:
    def __init__(self, host: str = "127.0.0.1", port: int = 5555, timeout: float = 600.0):
        self.sock = socket.create_connection((host, port), timeout=timeout)
        self.reader = self.sock.makefile("r", encoding="utf-8")
        self.writer = self.sock.makefile("w", encoding="utf-8")
        self.last: dict[str, Any] = {}

    # ---- protocol ---------------------------------------------------------------------

    def request(self, message: dict[str, Any]) -> dict[str, Any]:
        self.writer.write(json.dumps(message) + "\n")
        self.writer.flush()
        line = self.reader.readline()
        if not line:
            raise KehaiError("the game closed the connection")
        reply = json.loads(line)
        if not reply.get("ok", False):
            raise KehaiError(reply.get("error", "unknown error"))
        if "obs" in reply:          # map/metrics replies don't replace the last observation
            self.last = reply
        return reply

    # ---- the environment ------------------------------------------------------------------

    def reset(self, seed: int = 1, rung: str = "F", shift_seconds: float = 180, fps: int = 20,
              render: bool = False, **config: Any) -> dict[str, Any]:
        config.update(seed=seed, rung=rung, shift_seconds=shift_seconds, fps=fps, render=render)
        return self.request({"cmd": "reset", "config": config})["obs"]

    def step(self, verb: str, target: str | None = None, **kwargs: Any) -> dict[str, Any]:
        action = {"verb": verb, **kwargs}
        if target is not None:
            action["target"] = target
        return self.request({"cmd": "step", "action": action})["obs"]

    def observe(self) -> dict[str, Any]:
        return self.request({"cmd": "observe"})["obs"]

    @property
    def done(self) -> bool:
        return bool(self.last.get("done", False))

    @property
    def text(self) -> str:
        """The last observation as prose — what a language-model agent reads."""
        return self.last.get("text", "")

    def map(self) -> dict[str, Any]:
        """The store map: rooms, regions, doorways, chokepoints, landmarks, bays, and an
        ASCII floor plan. STORE_MAP.md is the same thing for people."""
        reply = self.request({"cmd": "map"})
        return {"map": reply["map"], "ascii": reply.get("ascii", "")}

    def metrics(self) -> dict[str, Any]:
        return self.request({"cmd": "metrics"})["metrics"]

    def close(self) -> None:
        try:
            self.request({"cmd": "close"})
        except (KehaiError, OSError):
            pass
        self.sock.close()

    def __enter__(self) -> "KehaiEnv":
        return self

    def __exit__(self, *exc) -> None:
        self.close()
