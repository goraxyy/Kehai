"""Phase 7: the pipeline's state, the owner's Telegram (in outbox mode), the production chain with
its steps stubbed, posting (Buffer mocked), housekeeping, subtitles, the long video's gates, and
the n8n workflow files."""
import datetime as dt
import glob
import json
import os

import httpx
import pytest

from km import alerts, buffer, housekeeping, longform, media, paths, production, publishing, subtitles
from km.approvals import Approvals, send_alerts
from km.settings import settings
from km.store import Store
from km.telegram import Bot, buttons

PIPE_ENV = ("TELEGRAM_BOT_TOKEN", "TELEGRAM_CHAT_ID", "BUFFER_API_KEY", "KEHAI_PUBLIC_REMOTE", "KEHAI_PUBLIC_URL")


@pytest.fixture
def world(tmp_path, monkeypatch):
    for k in PIPE_ENV:
        monkeypatch.delenv(k, raising=False)
    monkeypatch.setattr("km.env.ENV_FILE", tmp_path / "no.env")
    monkeypatch.setattr("km.env._loaded", False)
    root = tmp_path / "marketing"
    store = Store(root)
    bot = Bot(root, store)
    yield root, store, bot, Approvals(root, store, bot)
    store.close()


def ready_video(root, store, vid="2026-w40-one-sprint", status="writing"):
    """A short whose every step is done on disk (placeholder files)."""
    from helpers import ctx, draft
    from km import drafts
    edit, _ = drafts.to_edit(draft(), ctx())
    edit["id"] = vid
    edit["languages"] = ["en", "ru"]
    edit["voice"] = {"en": [], "ru": []}
    folder = root / "edits" / vid
    folder.mkdir(parents=True)
    (folder / "edit.json").write_text(json.dumps(edit))
    (folder / "draft.json").write_text(json.dumps({**draft(), "id": vid}))
    for lang in ("en", "ru"):
        f = root / "drafts" / f"{vid}.{lang}.mp4"
        f.parent.mkdir(parents=True, exist_ok=True)
        f.write_bytes(b"\0" * 1000)
    pkg = {lang: {"youtube": {"title": "She heard one sprint", "description": "One sprint was enough.", "tags": ["indie horror"]},
                  "instagram": {"caption": "One sprint.", "hashtags": ["#indiehorror"]},
                  "tiktok": {"caption": "Would you run?", "hashtags": ["#horror"]},
                  "x": "One sprint. #indiedev", "bluesky": "One sprint.", "pinned_comment": "?", "thumbnail_text": ""}
           for lang in ("en", "ru")}
    (folder / "package.json").write_text(json.dumps({"version": 1, "id": vid, "languages": pkg}))
    store.add_video(vid, "short", status=status, week="2026-W40", pick="one-sprint")
    return vid


def last(bot, method=None):
    sent = [m for m in bot.sent() if method is None or m["method"] == method]
    return sent[-1] if sent else None


def handle_all(bot, approvals):
    out = []
    for u in bot.updates():
        if bot.from_owner(u):
            out.append(approvals.handle(u))
        bot.done(u)
    return out


def press(bot, approvals, data, message_id):
    bot.fake_tap(data, message_id)
    return handle_all(bot, approvals)


# ---- the store -------------------------------------------------------------------------------

def test_jobs_are_taken_once_and_retried_later(world):
    root, store, _, _ = world
    a = store.queue("revise", "v1", note="shorter")
    store.queue("undo", "v2")
    job = store.take_job()
    assert job["id"] == a and job["payload"] == {"note": "shorter"} and job["attempts"] == 1
    assert store.take_job()["kind"] == "undo"
    assert store.take_job() is None
    store.finish_job(a, error="busy", retry_at=(dt.datetime.now() + dt.timedelta(hours=1)).isoformat())
    assert store.take_job() is None, "not before its time"
    store.finish_job(a, error="busy", retry_at="2000-01-01T00:00:00")
    assert store.take_job()["id"] == a


def test_videos_have_known_statuses(world):
    _, store, _, _ = world
    store.add_video("v", "short")
    with pytest.raises(ValueError):
        store.update("v", status="lost")


def test_a_dry_run_report_asks_claude_nothing_and_sends_nothing(world, monkeypatch):
    import run_job
    root, store, bot, _ = world
    seen = []
    monkeypatch.setattr(run_job.production, "script", lambda r, name, *a, dry_run=False, **k: seen.append((name, dry_run))
                        or production.Result(0, ""))
    assert run_job.report(root, bot, dry_run=True) == 0
    assert seen == [("weekly_report.py", True)]
    assert not (root / "state" / "telegram_outbox.jsonl").exists()


def test_trial_mode_covers_every_job_that_spends_or_posts():
    text = (paths.HERE / "n8n" / "job.sh").read_text(encoding="utf-8")
    assert "produce|publish|long|report)" in text


# ---- Telegram, in outbox mode ----------------------------------------------------------------

def test_without_a_token_the_bot_writes_an_outbox(world):
    root, store, bot, _ = world
    assert not bot.live
    m = bot.text("hello", buttons([[("✅", "a:x:1")]]))
    assert last(bot)["message_id"] == m and last(bot)["params"]["reply_markup"]["inline_keyboard"][0][0]["callback_data"] == "a:x:1"
    with pytest.raises(ValueError):
        buttons([[("too long", "a:" + "x" * 70)]])


def test_only_the_owners_updates_are_handled(world, monkeypatch):
    root, store, bot, approvals = world
    bot.fake({"message": {"message_id": 1, "chat": {"id": "stranger"}, "text": "/status"}})
    bot.fake_text("/status")
    handled = handle_all(bot, approvals)
    assert handled == ["command /status"], "the stranger's message is dropped"
    assert bot.updates() == [], "the offset moved past both"


def test_produce_gets_past_a_bad_markers_file(world, tmp_path, monkeypatch, capsys):
    import run_job
    from helpers import FIXTURES, STEM
    root, store, bot, approvals = world
    doc = json.loads((FIXTURES / f"{STEM}.markers.json").read_text())
    doc.update(shift=-1, started=dt.datetime.now().strftime("%Y-%m-%d %H:%M"))
    records = tmp_path / "records"
    records.mkdir()
    (records / "bad.markers.json").write_text(json.dumps(doc))
    monkeypatch.setenv("KEHAI_SHIFT_RECORDS", str(records))
    run_job.produce(root, store, bot, approvals, None, False, True)
    assert "skipped" in capsys.readouterr().out


def test_a_bad_update_is_answered_and_the_rest_still_handled(world, monkeypatch):
    import run_job
    root, store, bot, approvals = world
    bot.fake_tap("pd:x:n", 5)
    bot.fake_tap("a:nothing-here:1", 6)
    monkeypatch.setattr(approvals, "handle", lambda u: (_ for _ in ()).throw(RuntimeError("boom")) if u["update_id"] == 2 else "fine")
    bot.fake_text("/help")
    run_job.telegram(root, store, bot, approvals)
    assert bot.updates() == [], "every update is past the offset, the failing one too"
    assert any("Couldn't handle a Telegram update" in m["params"].get("text", "") for m in bot.sent()), "the owner hears"
    assert Approvals(root, store, bot).handle({"callback_query": {"id": "c", "data": "pd:x:n", "message": {"message_id": 1}}}) == "unknown button pd:x:n"


def test_a_preview_carries_the_video_and_four_buttons(world):
    root, store, bot, approvals = world
    vid = ready_video(root, store)
    approvals.present(store.video(vid))
    sent = last(bot, "sendVideo")
    assert sent["file"].endswith(f"{vid}.en.mp4")
    data = [b["callback_data"] for row in sent["params"]["reply_markup"]["inline_keyboard"] for b in row]
    assert data == [f"a:{vid}:1", f"r:{vid}:1", f"e:{vid}:1", f"ru:{vid}:1"], "no undo before there's a version"
    assert "v1" in sent["params"]["caption"] and store.video(vid)["status"] == "awaiting"


def test_approving_logs_it_and_takes_the_buttons_away(world):
    root, store, bot, approvals = world
    vid = ready_video(root, store)
    approvals.present(store.video(vid))
    msg = store.video(vid)["message_id"]
    assert press(bot, approvals, f"a:{vid}:1", msg) == [f"{vid}: approved"]
    assert store.video(vid)["status"] == "approved"
    assert any(m["method"] == "editMessageReplyMarkup" and m["params"]["message_id"] == msg for m in bot.sent())
    log = [json.loads(l) for l in (root / "state" / "approvals.jsonl").read_text().splitlines()]
    assert log[-1]["decision"] == "approved" and log[-1]["video"] == vid
    assert press(bot, approvals, f"r:{vid}:1", msg) == [f"{vid}: not awaiting (approved)"], "a second tap does nothing"


def test_change_asks_for_a_note_and_the_reply_queues_a_revision(world):
    root, store, bot, approvals = world
    vid = ready_video(root, store)
    approvals.present(store.video(vid))
    press(bot, approvals, f"e:{vid}:1", store.video(vid)["message_id"])
    question = last(bot, "sendMessage")
    assert question["params"]["reply_markup"]["force_reply"] is True
    bot.fake_text("make the hook about hearing", reply_to=question["message_id"])
    assert handle_all(bot, approvals) == [f"{vid}: revise queued"]
    job = store.take_job()
    assert job["kind"] == "revise" and job["payload"]["note"] == "make the hook about hearing"
    assert store.video(vid)["status"] == "revising"


def test_a_revised_video_comes_back_as_the_next_version_and_old_buttons_do_nothing(world):
    root, store, bot, approvals = world
    vid = ready_video(root, store)
    approvals.present(store.video(vid))
    first = store.video(vid)["message_id"]
    assert (root / "edits" / vid / "shown" / "v001" / "edit.json").exists()
    store.update(vid, status="revising")
    approvals.present(store.video(vid))
    video = store.video(vid)
    assert video["version"] == 2 and video["message_id"] != first
    data = [b["callback_data"] for row in last(bot, "sendVideo")["params"]["reply_markup"]["inline_keyboard"] for b in row]
    assert f"u:{vid}:2" in data, "undo once there's an earlier version"
    assert press(bot, approvals, f"a:{vid}:1", first) == [f"{vid}: stale button v1"]
    assert press(bot, approvals, f"u:{vid}:2", video["message_id"]) == [f"{vid}: undo queued"]
    assert store.take_job()["kind"] == "undo"


def test_undo_brings_back_the_version_the_owner_saw_and_renders_it_again(world, monkeypatch):
    import run_job
    root, store, bot, approvals = world
    vid = ready_video(root, store)
    approvals.present(store.video(vid))
    edit_file = production.edit_path(root, vid)
    first = json.loads(edit_file.read_text())
    changed = {**first, "title": {"en": "Changed", "ru": "Изменено"}}
    edit_file.write_text(json.dumps(changed))
    store.update(vid, status="revising")
    approvals.present(store.video(vid))
    store.queue("undo", vid)
    store.update(vid, status="revising")
    rendered = []

    def fake_do(root_, step, video, plan_path, dry_run=False):
        rendered.append(step)
        for f in [*root_.glob(f"drafts/{vid}.*.mp4"), production.edit_path(root_, vid).parent / "package.json"]:
            os.utime(f)
        return production.Result(0, "ok")

    monkeypatch.setattr(production, "do", fake_do)
    run_job.work(root, store, bot, approvals, 1)
    assert json.loads(edit_file.read_text())["title"] == first["title"]
    assert "render" in rendered, "the drafts were of the other version"
    assert store.video(vid)["version"] == 3 and store.video(vid)["status"] == "awaiting"


def test_rejecting_and_the_russian_version(world):
    root, store, bot, approvals = world
    vid = ready_video(root, store)
    approvals.present(store.video(vid))
    msg = store.video(vid)["message_id"]
    assert press(bot, approvals, f"ru:{vid}:1", msg) == [f"{vid}: russian sent"]
    assert last(bot, "sendVideo")["file"].endswith(f"{vid}.ru.mp4")
    assert press(bot, approvals, f"r:{vid}:1", msg) == [f"{vid}: rejected"]
    assert store.video(vid)["status"] == "rejected" and store.video(vid)["rejected_at"]


def test_commands(world):
    root, store, bot, approvals = world
    vid = ready_video(root, store, status="failed")
    store.update(vid, step="voice", error="AZURE_SPEECH_KEY isn't set")
    for text in ("/help", "/status", "/costs", "/report", f"/retry {vid}", "/nope"):
        bot.fake_text(text)
    handle_all(bot, approvals)
    texts = [m["params"]["text"] for m in bot.sent() if m["method"] == "sendMessage"]
    assert "reply to that message" in texts[0]
    assert vid in texts[1] and "failed at voice" in texts[1]
    assert texts[2].startswith("Claude this month")
    assert texts[3] == "No weekly report yet."
    assert "goes again" in texts[4] and store.video(vid)["status"] == "writing"
    assert "/help" in texts[5]


def test_alerts_go_to_telegram_once(world):
    root, store, bot, _ = world
    alerts.alert(root, "llm_budget", "Claude steps stopped: $15 spent.")
    assert send_alerts(root, bot) == 1 and send_alerts(root, bot) == 0
    assert "Claude steps stopped" in last(bot)["params"]["text"] and alerts.pending(root) == []


# ---- the production chain --------------------------------------------------------------------

def test_the_chain_starts_where_the_files_say(world, tmp_path):
    root, store, _, _ = world
    vid = ready_video(root, store)
    video = store.video(vid)
    assert production.todo(root, video, None) == "present"
    edit_file = production.edit_path(root, vid)
    os.utime(edit_file, (2e9, 2e9))                          # the edit changed after the drafts
    assert production.todo(root, video, None) == "render"
    edit = json.loads(edit_file.read_text())
    edit["languages"] = ["en"]
    edit_file.write_text(json.dumps(edit))
    assert production.todo(root, video, None) == "translate"
    plan = {"renders": [{"pick": "one-sprint", "out": "shots/w/one-sprint/top.mp4"}]}
    assert production.todo(root, video, plan) == "shots"
    edit_file.unlink()
    assert production.todo(root, {**video, "pick": "other"}, plan) == "write"


@pytest.mark.parametrize("code, step, expect", [(0, "voice", "ok"), (75, "render", "later"), (3, "shots", "later"),
                                                (3, "voice", "blocked"), (4, "write", "blocked"), (2, "write", "failed"),
                                                (5, "write", "failed"), (1, "voice", "failed")])
def test_what_an_exit_code_means(code, step, expect):
    assert production.outcome(code, step) == expect


def test_advance_runs_the_steps_and_sends_the_preview(world, monkeypatch):
    root, store, bot, approvals = world
    vid = ready_video(root, store)
    edit_file = production.edit_path(root, vid)
    os.utime(edit_file, (2e9, 2e9))
    ran = []

    def fake_do(root_, step, video, plan_path, dry_run=False):
        ran.append(step)
        for f in [*root_.glob(f"drafts/{vid}.*.mp4"), production.edit_path(root_, vid).parent / "package.json"]:
            os.utime(f, (2.1e9, 2.1e9))
        return production.Result(0, "fine")

    monkeypatch.setattr(production, "do", fake_do)
    text = production.advance(root, store, bot, store.video(vid), None, approvals.present)
    assert ran == ["render"] and "sent for approval" in text
    assert store.video(vid)["status"] == "awaiting" and last(bot, "sendVideo")


def test_a_step_that_keeps_failing_waits_for_the_owner(world, monkeypatch):
    root, store, bot, approvals = world
    vid = ready_video(root, store)
    os.utime(production.edit_path(root, vid), (2e9, 2e9))
    monkeypatch.setattr(production, "do", lambda *a, **k: production.Result(1, "render: Chrome crashed"))
    for _ in range(settings()["production"]["retries"]):
        assert "failed" in production.advance(root, store, bot, store.video(vid), None, approvals.present)
        assert store.video(vid)["status"] == "writing"
    production.advance(root, store, bot, store.video(vid), None, approvals.present)
    assert store.video(vid)["status"] == "failed" and "Chrome crashed" in store.video(vid)["error"]
    assert any(a["kind"] == "failed" for a in alerts.pending(root))


def test_a_missing_key_blocks_and_tells_the_owner_once_a_day(world, monkeypatch):
    root, store, bot, approvals = world
    vid = ready_video(root, store)
    os.utime(production.edit_path(root, vid), (2e9, 2e9))
    monkeypatch.setattr(production, "do", lambda *a, **k: production.Result(3, "voice: AZURE_SPEECH_KEY isn't set"))
    for _ in range(3):
        assert "blocked" in production.advance(root, store, bot, store.video(vid), None, approvals.present)
    assert store.video(vid)["attempts"] == 0 and store.video(vid)["status"] == "writing"
    assert len([a for a in alerts.pending(root) if a["kind"] == "blocked"]) == 1


# ---- posting ---------------------------------------------------------------------------------

def test_without_buffer_a_posting_day_sends_everything_to_post_by_hand(world, monkeypatch):
    root, store, bot, approvals = world
    vid = ready_video(root, store, status="approved")
    assert publishing.publish(root, store, bot, force=True) == [
        f"{vid} en: by hand on youtube, instagram, twitter, tiktok (Buffer isn't ready (BUFFER_API_KEY isn't set yet))"]
    texts = [m for m in bot.sent() if m["method"] == "sendMessage"]
    assert "Title: She heard one sprint" in texts[0]["params"]["text"]
    taps = [b["callback_data"] for m in texts for row in m["params"].get("reply_markup", {}).get("inline_keyboard", []) for b in row]
    assert taps == [f"pd:{vid}:{s}:en" for s in ("youtube", "instagram", "twitter", "tiktok")]
    assert store.video(vid)["status"] == "scheduled"
    for data in taps:
        press(bot, approvals, data, 1)
    assert store.video(vid)["status"] == "posted" and "up everywhere" in last(bot, "sendMessage")["params"]["text"]


def test_not_a_posting_day_and_nothing_approved(world):
    root, store, bot, _ = world
    day = dt.date.today()
    if settings()["posting"]["days"] and publishing.day_name(day) not in settings()["posting"]["days"]:
        assert "isn't a posting day" in publishing.publish(root, store, bot)[0]
    assert publishing.publish(root, store, bot, force=True) == ["nothing approved to post"]
    publishing.publish(root, store, bot, force=True)
    assert len([m for m in bot.sent() if "no approved short" in m["params"].get("text", "")]) == 1, "said once a day"


def mock_buffer(monkeypatch, handler):
    real = httpx.Client

    def client(*a, **k):
        k["transport"] = httpx.MockTransport(handler)
        return real(*a, **k)

    monkeypatch.setattr(httpx, "Client", client)


def test_buffer_gets_one_queued_video_post_per_channel(world, monkeypatch):
    root, store, bot, _ = world
    vid = ready_video(root, store, status="approved")
    monkeypatch.setenv("BUFFER_API_KEY", "test-key")
    monkeypatch.setattr(media, "publish", lambda path, rel: f"https://pub.example/{rel}")
    seen = []

    def handler(request):
        assert request.headers["authorization"] == "Bearer test-key"
        body = json.loads(request.content)
        seen.append(body)
        q = body["query"]
        if "organizations" in q:
            return httpx.Response(200, json={"data": {"account": {"organizations": [{"id": "org1", "name": "TokenLimit"}]}}})
        if "channels(" in q:
            return httpx.Response(200, json={"data": {"channels": [
                {"id": "c-yt", "service": "youtube", "isDisconnected": False, "isLocked": False},
                {"id": "c-ig", "service": "instagram", "isDisconnected": False, "isLocked": False}]}})
        if "createPost" in q:
            return httpx.Response(200, json={"data": {"createPost": {"post": {"id": f"p-{body['variables']['input']['channelId']}", "status": "scheduled"}}}})
        raise AssertionError(q)

    mock_buffer(monkeypatch, handler)
    out = publishing.publish(root, store, bot, force=True)
    posts = [b["variables"]["input"] for b in seen if "createPost" in b["query"]]
    yt = next(p for p in posts if p["channelId"] == "c-yt")
    assert yt["assets"] == [{"video": {"url": f"https://pub.example/{vid}/{vid}.en.mp4"}}]
    assert yt["mode"] == "addToQueue" and yt["schedulingType"] == "automatic" and yt["needsApproval"] is False
    assert yt["metadata"]["youtube"] == {"title": "She heard one sprint", "categoryId": "20", "privacy": "public",
                                         "madeForKids": False, "notifySubscribers": True}
    ig = next(p for p in posts if p["channelId"] == "c-ig")
    assert ig["metadata"] == {"instagram": {"type": "reel", "shouldShareToFeed": True}} and "#indiehorror" in ig["text"]
    assert store.post(vid, "youtube", "en")["buffer_id"] == "p-c-yt"
    assert any("by hand on twitter, tiktok" in line for line in out), "no X channel connected; TikTok is by hand"


def test_buffer_statuses_finish_a_post_or_send_it_by_hand(world, monkeypatch):
    root, store, bot, _ = world
    vid = ready_video(root, store, status="scheduled")
    store.set_post(vid, "youtube", "en", "scheduled", buffer_id="p1")
    store.set_post(vid, "instagram", "en", "scheduled", buffer_id="p2")
    monkeypatch.setenv("BUFFER_API_KEY", "k")
    statuses = {"p1": {"id": "p1", "status": "sent"}, "p2": {"id": "p2", "status": "error", "error": {"message": "video too long"}}}
    mock_buffer(monkeypatch, lambda r: httpx.Response(200, json={"data": {"post": statuses[json.loads(r.content)["variables"]["input"]["id"]]}}))
    out = publishing.sync(root, store, bot)
    assert out == [f"{vid} youtube: sent", f"{vid} instagram: failed in Buffer, sent to post by hand"]
    assert store.post(vid, "instagram", "en")["status"] == "manual"


def test_buffer_errors_are_clear():
    with pytest.raises(KeyError):
        buffer.post_metadata("youtube", {})
    assert buffer.text_for("twitter", {"x": "post"}) == "post"


# ---- housekeeping ----------------------------------------------------------------------------

def test_retention_reports_until_it_is_switched_on(world, monkeypatch):
    root, store, _, _ = world
    vid = ready_video(root, store, status="rejected")
    old = (dt.datetime.now() - dt.timedelta(days=30)).isoformat(timespec="seconds")
    store.update(vid, rejected_at=old)
    (root / "audio" / vid).mkdir(parents=True)
    (root / "audio" / vid / "a.wav").write_bytes(b"x" * 2000)
    shots = root / "shots" / "2026-W40" / "one-sprint"
    shots.mkdir(parents=True)
    (shots / "top.mp4").write_bytes(b"x" * 5000)
    lines = housekeeping.Keeper(root, store, apply=False).run()
    assert any(l.startswith("would delete") and "rejected" in l for l in lines)
    assert (root / "drafts" / f"{vid}.en.mp4").exists() and shots.exists()
    lines = housekeeping.Keeper(root, store, apply=True).run()
    assert not (root / "drafts" / f"{vid}.en.mp4").exists() and not shots.exists() and not (root / "audio" / vid).exists()
    assert store.video(vid)["deleted_at"]


def test_nothing_unarchived_is_deleted_and_nothing_outside_is_touched(world, tmp_path):
    root, store, _, _ = world
    vid = ready_video(root, store, status="posted")
    store.update(vid, posted_at="2000-01-01T00:00:00")
    keeper = housekeeping.Keeper(root, store, apply=True)
    keeper.retention()
    assert (root / "drafts" / f"{vid}.en.mp4").exists(), "posted but not archived: kept"
    outside = tmp_path / "precious.txt"
    outside.write_text("keep me")
    keeper.remove(outside, "test")
    assert outside.exists()


def test_without_drive_the_owner_hears_once_a_week(world):
    root, store, _, _ = world
    ready_video(root, store, status="approved")
    for _ in range(2):
        housekeeping.Keeper(root, store, apply=False).run()
    assert len([a for a in alerts.pending(root) if a["kind"] == "archive"]) == 1


def test_storage_limits_stop_production(world, monkeypatch):
    root, store, _, _ = world
    import km.settings as s
    monkeypatch.setitem(s.settings()["storage"], "working_gb", 1e-9)
    assert "the working folder is" in housekeeping.too_full(root)


# ---- subtitles and the long video ------------------------------------------------------------

def test_subtitles_follow_the_words():
    edit = {"voice": {"en": [{"at": 10.0, "duration": 3, "words": [
        {"text": "She", "start": 0.0, "end": 0.2}, {"text": "heard", "start": 0.2, "end": 0.5},
        {"text": "me.", "start": 0.5, "end": 0.8}, {"text": "Then", "start": 1.2, "end": 1.4},
        {"text": "nothing.", "start": 1.4, "end": 2.0}]}]}}
    srt = subtitles.srt(edit, "en")
    assert srt.startswith("1\n00:00:10,000 --> 00:00:10,800\nShe heard me.\n\n2\n00:00:11,200 --> 00:00:12,000\nThen nothing.")
    assert subtitles.srt(edit, "ru") == ""


def test_the_long_videos_gates(world, monkeypatch):
    root, store, bot, approvals = world
    month = "2026-09"
    folder = root / "long" / month
    folder.mkdir(parents=True)
    (folder / "outline.json").write_text(json.dumps({"answer": {"logline": "She listens.", "title_ideas": ["A", "B", "C"], "cta": "Follow.",
        "sections": [{"name": "Cold open", "minutes": 2, "purpose": "Hook.", "beats": ["The catch"], "shots": [{"name": "x"}]}]}}))
    monkeypatch.setattr(longform, "script", lambda *a, **k: production.Result(0, "ok"))
    long = longform.Long(root, store, bot)
    assert long.start(month) == f"{month}: outline sent for approval"
    gate = last(bot, "sendMessage")
    assert "The catch" in gate["params"]["text"]
    data = [b["callback_data"] for row in gate["params"]["reply_markup"]["inline_keyboard"] for b in row]
    assert data == [f"lg:a:{month}:outline", f"lg:e:{month}:outline"]
    press(bot, approvals, f"lg:e:{month}:outline", gate["message_id"])
    question = last(bot, "sendMessage")
    bot.fake_text("more about the blink", reply_to=question["message_id"])
    assert handle_all(bot, approvals) == [f"long {month}: redo outline"]
    job = store.take_job()
    assert job["payload"] == {"month": month, "stage": "redo-outline", "note": "more about the blink"}
    assert long.work(job) == (0, f"long {month}: outline sent for approval")
    gate = last(bot, "sendMessage")
    assert press(bot, approvals, f"lg:a:{month}:outline", gate["message_id"]) == [f"long {month}: outline approved"]
    assert store.take_job()["payload"]["stage"] == "shots"
    assert press(bot, approvals, f"lg:a:{month}:outline", gate["message_id"]) == [f"long {month}: stale outline"]


def test_the_long_video_starts_on_its_day():
    day = settings()["long"]["day"]
    assert longform.due(dt.date(2026, 10, day)) == "2026-09"
    assert longform.due(dt.date(2026, 1, day)) == "2025-12"
    assert longform.due(dt.date(2026, 10, day + 1)) is None


# ---- the n8n workflows -----------------------------------------------------------------------

@pytest.mark.parametrize("path", sorted(glob.glob(str(paths.HERE / "n8n" / "workflows" / "*.json"))))
def test_each_workflow_runs_one_job_on_a_schedule_and_by_hand(path):
    wf = json.loads(open(path, encoding="utf-8").read())
    types = sorted(n["type"] for n in wf["nodes"])
    assert types == ["n8n-nodes-base.executeCommand", "n8n-nodes-base.manualTrigger", "n8n-nodes-base.scheduleTrigger"]
    command = next(n for n in wf["nodes"] if n["type"] == "n8n-nodes-base.executeCommand")["parameters"]["command"]
    job = os.path.basename(path).removesuffix(".json")
    assert command == f'"$KEHAI_TOOLS/n8n/job.sh" {job}'
    assert job in ("produce", "telegram", "work", "publish", "housekeeping", "report", "long")
    assert set(wf["connections"]) == {"Schedule", "Run now"}
    assert wf["settings"]["timezone"] == settings()["timezone"]
    text = json.dumps(wf)
    for secret in ("TOKEN", "API_KEY", "sk-ant", "Bearer"):
        assert secret not in text


# ---- the launchd schedule (setup.sh --launchd) ------------------------------------------------

def _launchd():
    import importlib.util
    spec = importlib.util.spec_from_file_location("launchd", paths.HERE / "n8n" / "launchd.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def test_launchd_runs_the_workflows_schedules():
    launchd = _launchd()
    planned = {job: keys for job, keys, _ in launchd.jobs()}
    assert set(planned) == {"produce", "telegram", "work", "publish", "housekeeping", "report", "long"}
    assert planned["telegram"] == {"StartInterval": 60}
    assert planned["work"] == {"StartInterval": 300}
    assert planned["produce"] == {"StartCalendarInterval": [{"Minute": 0, "Hour": h} for h in (1, 3, 5)]}
    assert planned["report"] == {"StartCalendarInterval": [{"Minute": 0, "Hour": 20, "Weekday": 0}]}


def test_launchd_reads_cron_lists_ranges_and_steps():
    launchd = _launchd()
    assert len(launchd.calendar("*/30 9-10 * * 1,3,5")) == 12
    assert launchd.calendar("0 0 4 * * *") == [{"Minute": 0, "Hour": 4}]       # n8n's form with seconds
    for bad in ("0 24 * * *", "0 1 * *", "0 L * * *", "30 0 1 * * *"):
        with pytest.raises(launchd.BadSchedule):
            launchd.calendar(bad)


def test_launchd_agents_run_job_sh_and_log_outside_the_repo(tmp_path):
    launchd = _launchd()
    doc = launchd.plist("produce", {"StartInterval": 60}, tmp_path)
    assert doc["Label"] == "com.tokenlimit.kehai.job.produce"
    assert doc["ProgramArguments"] == ["/bin/bash", str(paths.HERE / "n8n" / "job.sh"), "produce"]
    assert doc["EnvironmentVariables"]["KEHAI_LOG_DIR"] == str(tmp_path / "scheduled")
    assert "ProcessType" not in doc                               # renders keep the normal priority
    assert launchd.plist("telegram", {"StartInterval": 60}, tmp_path)["ProcessType"] == "Background"


def test_a_pick_day_the_mac_slept_through_still_picks_that_week():
    from km.settings import on_or_after
    sat, sun, mon, fri = dt.date(2026, 10, 3), dt.date(2026, 10, 4), dt.date(2026, 10, 5), dt.date(2026, 10, 2)
    assert on_or_after(sat, "sat") and on_or_after(sun, "sat")
    assert not on_or_after(mon, "sat") and not on_or_after(fri, "sat")


def test_telegram_setup_asks_telegram_before_the_chat_id_is_known(world, monkeypatch, capsys):
    root, store, _, _ = world
    import run_job
    asked = []

    def handler(request):
        asked.append(request.url.path)
        return httpx.Response(200, json={"ok": True, "result": [
            {"update_id": 5, "message": {"message_id": 1, "text": "/start", "chat": {"id": 4242, "first_name": "Owner"}}}]})

    mock_buffer(monkeypatch, handler)
    monkeypatch.setenv("TELEGRAM_BOT_TOKEN", "123:test")
    bot = Bot(root, store)
    assert not bot.live                                   # no chat id yet: everything else stays in the outbox
    run_job.telegram_setup(bot)
    assert asked == ["/bot123:test/getUpdates"]
    assert "TELEGRAM_CHAT_ID=4242" in capsys.readouterr().out
    assert bot.sent() == []
