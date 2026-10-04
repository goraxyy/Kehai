# tools/playtest

Everything outside the game for running playtests. The design and the loop are in
[`PLAYTEST.md`](../../PLAYTEST.md).

| | |
|---|---|
| `.env.example` | The settings, copied to `.env` (git ignores it): the round, the upload service's address and keys, the form and download links |
| `worker/` | The upload service: a Cloudflare Worker in front of a private R2 bucket (`src/index.js`, tests in `test/`, `wrangler.toml`) |
| `make_codes.py` | Tester codes for a round, and a ready-to-send message for each |
| `TESTER_MESSAGE.md` | That message's text |
| `ITCH_PAGE.md` | What to put on the itch.io page |

Your Mac's side (pulling sessions, the report, Telegram, Claude's summaries, the issues) lives
with the marketing pipeline it shares Telegram and Claude with:
`tools/marketing/km/playtest/` and `tools/marketing/playtest.py`.

## Setting up the upload service (once)

You need a Cloudflare account (the same one the marketing plan wants for R2, Q8). R2 is free up
to 10 GB and may ask for a payment method; Workers are free up to 100,000 requests a day.

1. **Two keys.** Run `openssl rand -hex 24` twice. In `tools/playtest/.env`, put the first after
   `KEHAI_PLAYTEST_UPLOAD_KEY=` (it goes into every playtest build and can only add sessions)
   and the second after `KEHAI_PLAYTEST_ADMIN_KEY=` (it stays on your Mac: it reads and deletes
   them).
2. **Deploy**, from `tools/playtest/worker` (`npx` fetches Cloudflare's `wrangler` the first time):

   ```bash
   npx wrangler login
   npx wrangler r2 bucket create kehai-playtests
   npx wrangler secret put UPLOAD_KEY
   npx wrangler secret put ADMIN_KEY
   npx wrangler deploy
   ```

   Paste each key when `secret put` asks. `deploy` prints the address
   (`https://kehai-playtest.<your account>.workers.dev`): put it after
   `KEHAI_PLAYTEST_UPLOAD_URL=`.
3. **Check:** opening that address says *Kehai playtest uploads*, and
   `uv run playtest.py status` (in `tools/marketing`) says the upload service is set up.

Then every playtest build you make sends there, and your Mac pulls from there every five minutes.

## Running the tests

```bash
cd tools/playtest/worker && node --test          # the upload service, against a stand-in for R2
cd tools/marketing && uv run pytest tests/test_playtest.py   # your Mac's side
```
