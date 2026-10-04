"""The owner's Telegram: previews to approve, notes to act on, alerts and reports.

With TELEGRAM_BOT_TOKEN and TELEGRAM_CHAT_ID in .env, this is the Bot API (polled with
getUpdates: no webhook, nothing listening on the internet). Without them it is an **outbox**:
everything that would be sent is appended to <root>/state/telegram_outbox.jsonl, and updates are
read from <root>/state/telegram_inbox.jsonl (what `run_job.py fake …` and the tests write), so the
whole approval flow runs without a bot.

Only the owner's chat is listened to; anything else is ignored. Videos the owner sends (reference
videos) are fetched with getFile, which bots may only do up to 20 MB.
"""
from __future__ import annotations

import datetime as dt
import html
import json
import shutil
import time
from pathlib import Path

from . import env

API = "https://api.telegram.org/bot{token}/{method}"
FILES = "https://api.telegram.org/file/bot{token}/{path}"
DOWNLOAD_MB = 20                 # the Bot API's getFile limit
CAPTION = 1024
TEXT = 4096


class TelegramError(Exception):
    """The Bot API refused a call."""


def esc(text: str) -> str:
    return html.escape(str(text), quote=False)


def buttons(rows: list[list[tuple[str, str]]]) -> dict:
    """An inline keyboard from rows of (label, callback data)."""
    for row in rows:
        for _, data in row:
            if len(data.encode("utf-8")) > 64:
                raise ValueError(f"callback data over 64 bytes: {data!r}")
    return {"inline_keyboard": [[{"text": t, "callback_data": d} for t, d in row] for row in rows]}


def clip(text: str, limit: int) -> str:
    return text if len(text) <= limit else text[: limit - 1] + "…"


class Bot:
    def __init__(self, root: Path, store):
        self.root = root
        self.store = store
        self.token = env.get("TELEGRAM_BOT_TOKEN")
        self.chat = env.get("TELEGRAM_CHAT_ID")
        self.live = bool(self.token and self.chat)
        self.outbox = root / "state" / "telegram_outbox.jsonl"
        self.inbox = root / "state" / "telegram_inbox.jsonl"
        if self.token:
            import httpx
            self.http = httpx.Client(timeout=120)

    # ---- the transport ---------------------------------------------------------------------

    def call(self, method: str, params: dict, file: tuple[str, Path] | None = None, token_only: bool = False) -> dict:
        """A Bot API call; in outbox mode, a line in the outbox. `token_only`: the real API as soon as
        the token is set, before the chat id is known (telegram-setup finds it)."""
        if not (self.live or (token_only and self.token)):
            return self._outbox(method, params, file)
        url = API.format(token=self.token, method=method)
        for attempt in range(4):
            if file:
                field, path = file
                data = {k: (json.dumps(v) if isinstance(v, (dict, list)) else str(v)) for k, v in params.items()}
                with path.open("rb") as f:
                    r = self.http.post(url, data=data, files={field: (path.name, f)})
            else:
                r = self.http.post(url, json=params)
            body = r.json() if r.headers.get("content-type", "").startswith("application/json") else {}
            if body.get("ok"):
                return body["result"]
            if r.status_code == 429 and attempt < 3:
                time.sleep(int(body.get("parameters", {}).get("retry_after", 5)) + 1)
                continue
            raise TelegramError(f"{method}: {body.get('description') or r.status_code}")
        raise TelegramError(f"{method}: still rate limited")

    def _outbox(self, method: str, params: dict, file) -> dict:
        n = int(self.store.get("telegram.fake_message_id", "1000")) + 1
        self.store.set("telegram.fake_message_id", str(n))
        entry = {"time": dt.datetime.now().isoformat(timespec="seconds"), "method": method, "message_id": n,
                 "params": params}
        if file:
            entry["file"] = str(file[1])
        self.outbox.parent.mkdir(parents=True, exist_ok=True)
        with self.outbox.open("a", encoding="utf-8") as f:
            f.write(json.dumps(entry, ensure_ascii=False) + "\n")
        return {"message_id": n} if method.startswith("send") else {}

    # ---- what the pipeline sends -----------------------------------------------------------

    def text(self, text: str, keyboard: dict | None = None, force_reply: str | None = None) -> int:
        params = {"chat_id": self.chat or "owner", "text": clip(text, TEXT), "parse_mode": "HTML",
                  "link_preview_options": {"is_disabled": True}}
        if keyboard:
            params["reply_markup"] = keyboard
        elif force_reply:
            params["reply_markup"] = {"force_reply": True, "input_field_placeholder": force_reply[:64]}
        return self.call("sendMessage", params)["message_id"]

    def video(self, path: Path, caption: str, keyboard: dict | None = None) -> int:
        params = {"chat_id": self.chat or "owner", "caption": clip(caption, CAPTION), "parse_mode": "HTML",
                  "supports_streaming": True}
        if keyboard:
            params["reply_markup"] = keyboard
        return self.call("sendVideo", params, ("video", path))["message_id"]

    def document(self, path: Path, caption: str = "") -> int:
        params = {"chat_id": self.chat or "owner", "caption": clip(caption, CAPTION), "parse_mode": "HTML"}
        return self.call("sendDocument", params, ("document", path))["message_id"]

    def set_buttons(self, message_id: int, keyboard: dict | None) -> None:
        try:
            self.call("editMessageReplyMarkup", {"chat_id": self.chat or "owner", "message_id": message_id,
                                                 "reply_markup": keyboard or {"inline_keyboard": []}})
        except TelegramError:
            pass                          # an old message; nothing to fix

    def answer(self, callback_id: str, text: str = "") -> None:
        try:
            self.call("answerCallbackQuery", {"callback_query_id": callback_id, "text": text[:200]})
        except TelegramError:
            pass

    def download(self, file_id: str, out: Path) -> Path:
        """A file the owner sent, saved to `out` (in outbox mode, `fake:<path>` is copied)."""
        out.parent.mkdir(parents=True, exist_ok=True)
        if file_id.startswith("fake:"):
            shutil.copyfile(file_id.removeprefix("fake:"), out)
            return out
        if not self.live:
            raise TelegramError("no bot to download from")
        info = self.call("getFile", {"file_id": file_id})
        with self.http.stream("GET", FILES.format(token=self.token, path=info["file_path"])) as r:
            if r.status_code != 200:
                raise TelegramError(f"download: {r.status_code}")
            with out.open("wb") as f:
                for chunk in r.iter_bytes():
                    f.write(chunk)
        return out

    # ---- what the owner sends --------------------------------------------------------------

    def updates(self) -> list[dict]:
        """New updates since the last poll (everyone's: `from_owner` tells them apart). The caller
        moves the offset past each one it has dealt with (`done`), so a crash loses nothing."""
        offset = int(self.store.get("telegram.offset", "0"))
        if self.live:
            got = self.call("getUpdates", {"offset": offset, "timeout": 0, "allowed_updates": ["message", "callback_query"]})
        else:
            got = []
            if self.inbox.exists():
                for line in self.inbox.read_text(encoding="utf-8").splitlines():
                    if line.strip():
                        u = json.loads(line)
                        if u["update_id"] >= offset:
                            got.append(u)
        return sorted(got, key=lambda u: u["update_id"])

    def done(self, update: dict) -> None:
        self.store.set("telegram.offset", str(update["update_id"] + 1))

    def from_owner(self, update: dict) -> bool:
        msg = update.get("message") or (update.get("callback_query") or {}).get("message") or {}
        chat = str((msg.get("chat") or {}).get("id", ""))
        return chat == str(self.chat or "owner")

    # ---- the fake owner (outbox mode and tests) --------------------------------------------

    def fake(self, update: dict) -> dict:
        """Puts an update in the inbox as if the owner had sent it."""
        n = int(self.store.get("telegram.fake_update_id", "0")) + 1
        self.store.set("telegram.fake_update_id", str(n))
        update = {"update_id": n, **update}
        self.inbox.parent.mkdir(parents=True, exist_ok=True)
        with self.inbox.open("a", encoding="utf-8") as f:
            f.write(json.dumps(update, ensure_ascii=False) + "\n")
        return update

    def fake_tap(self, data: str, message_id: int) -> dict:
        chat = {"id": self.chat or "owner"}
        return self.fake({"callback_query": {"id": f"cb{message_id}-{data}", "from": chat, "data": data,
                                              "message": {"message_id": message_id, "chat": chat}}})

    def fake_text(self, text: str, reply_to: int | None = None) -> dict:
        chat = {"id": self.chat or "owner"}
        msg = {"message_id": int(time.time() * 1000) % 10**9, "chat": chat, "from": chat, "text": text}
        if reply_to:
            msg["reply_to_message"] = {"message_id": reply_to, "chat": chat}
        return self.fake({"message": msg})

    def fake_video(self, path: Path, caption: str = "", seconds: int = 0) -> dict:
        chat = {"id": self.chat or "owner"}
        msg = {"message_id": int(time.time() * 1000) % 10**9, "chat": chat, "from": chat,
               "video": {"file_id": f"fake:{path.resolve()}", "file_unique_id": path.name, "duration": seconds,
                         "mime_type": "video/mp4", "file_size": path.stat().st_size}}
        if caption:
            msg["caption"] = caption
        return self.fake({"message": msg})

    def sent(self) -> list[dict]:
        """Everything in the outbox (outbox mode)."""
        if not self.outbox.exists():
            return []
        return [json.loads(l) for l in self.outbox.read_text(encoding="utf-8").splitlines() if l.strip()]
