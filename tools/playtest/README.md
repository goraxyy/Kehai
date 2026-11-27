# tools/playtest

Everything outside the game for running playtests. The design and the loop are in
[`PLAYTEST.md`](../../docs/agents/game/PLAYTEST.md).

| | |
|---|---|
| `.env.example` | The settings, copied to `.env` (git ignores it): the round, the upload service's address and keys, the form and download links |
| `worker/` | The upload service (playtest v2): a Cloudflare Worker in front of a private R2 bucket (session zips, builds) and a D1 database (rows). A file per route in `src/` (the list is at the top of `src/index.js`), the tables in `migrations/`, tests in `test/`, `wrangler.toml` |
| `dataset.py` | For the Windows training PC (any computer with Python 3.10+, nothing to install): `list`, `fetch <folder>` and `reconcile <folder>`, a fixed snapshot of the sessions that may train Karen, with the read-only key. It checks each zip's hash and paths, carries on after a stop, removes what the cloud removed, and never trains or deletes anything in the cloud. Tests: `python -m unittest discover -s tools/playtest/tests` |
| `make_codes.py` | Legacy v1 tester-code and invitation generator. New builds create tester IDs on first launch |
| `TESTER_MESSAGE.md` | The legacy v1 invitation |
| `ITCH_PAGE.md` | What to put on the itch.io page |

Your Mac's side (pulling sessions, the report, Telegram, Claude's summaries, the issues) lives
with the marketing pipeline it shares Telegram and Claude with:
`tools/marketing/km/playtest/` and `tools/marketing/playtest.py`.

## Setting up the upload service (once)

You need a Cloudflare account (the same one the marketing plan wants for R2, Q8). R2 is free up
to 10 GB and may ask for a payment method; Workers are free up to 100,000 requests a day.

1. **Three keys.** Run `openssl rand -hex 24` three times. In `tools/playtest/.env`, put the first after
   `KEHAI_PLAYTEST_UPLOAD_KEY=` (it goes into every playtest build and can only add sessions),
   the second after `KEHAI_PLAYTEST_ADMIN_KEY=` (it stays on your Mac: it reads everything, and
   deletes a tester's data on a deletion request), and the third after
   `KEHAI_PLAYTEST_DATASET_KEY=` (for the Windows PC: it can only read the sessions that may train Karen).
2. **Deploy**, from `tools/playtest/worker` (`npx` fetches Cloudflare's `wrangler` the first time):

   ```bash
   npx wrangler login
   npx wrangler r2 bucket create kehai-playtests
   npx wrangler d1 create kehai-playtest          # put the id it prints in wrangler.toml
   npx wrangler d1 migrations apply kehai-playtest --remote
   npx wrangler secret put UPLOAD_KEY
   npx wrangler secret put ADMIN_KEY
   npx wrangler secret put DATASET_KEY
   npx wrangler deploy
   ```

   Paste each key when `secret put` asks. `deploy` prints the address
   (`https://kehai-playtest.<your account>.workers.dev`): put it after
   `KEHAI_PLAYTEST_UPLOAD_URL=`.
3. **Check:** opening that address says *Kehai playtest uploads*, and
   `uv run playtest.py status` (in `tools/marketing`) says the upload service is set up.

Then every playtest build you make sends there. Nothing on arrival needs your Mac or the PC:
the files stay in the cloud for six months from collection (Q5, a daily run in the Worker), and
a deletion request (`DELETE /admin/tester/<id>`) removes one tester's files and rows at once.

To try it on this Mac first: `npx wrangler d1 migrations apply kehai-playtest --local`, then
`npx wrangler dev` (not yet run: wrangler isn't installed here; C0.1 does it with the account).

## Running the tests

```bash
cd tools/playtest/worker && node --test          # the upload service, against stand-ins for R2 and D1 (a real SQLite with the migrations)
cd tools/marketing && uv run pytest tests/test_playtest.py   # your Mac's side
```
