# The website

One page, `index.html`: plain HTML, CSS and JavaScript, no framework and no build step. Fonts come
from Google Fonts; there are no images, so nothing binary goes in git.

**The idea:** a clean product page in the store's own corporate voice, with Karen as the product.
Light mode is the store with the lights on, dark mode is after hours, and Karen's crimson appears
only where she does.

**What's on it:**

| Part | What it is |
|---|---|
| The console | A live top-down map of the real store (the grid from `docs/store/STORE_MAP.md`, z −106 to −189). Your cursor is the worker. A simplified Karen hears your noise, keeps a belief map, crosses off what she sees empty, checks and hunts, at the game's speeds (walk 3.4, hurry 4.2, run 7.5 m/s against your 4 and 6.5). "Trip the breaker" shortens her sight. |
| The statement | One sentence that lights up word by word as you scroll (CSS scroll timelines; plain text where unsupported). |
| The receipt | Tonight's shift as a till receipt, beside the store's real numbers and aisle labels. |
| Three minds | The body, the director and the ledger, each with a small drawing. |
| The file | An example of what the Ledger keeps on you. |
| The blink | A button stands in for your eyelids: each blink, she closes about 45% of the gap (never nearer than 6 m) or vanishes and waits 8–16 m away. |
| The handbook | The fairness rules. |
| Now hiring | The playtest sign-up. |

**Every claim on the page** comes from `tools/marketing/facts.json` and avoids its banned phrases.
Change the game, then the facts, then this page.

**Sign-ups:** `SIGNUP_URL` in the script is empty until the playtest v2 Worker has its `/signup`
route (`docs/plans/playtest-v2.md`, C1.2). Until then the form says it's a preview and sends nothing.

**Previewing:** open `index.html` in a browser. It's written to work both as a whole page and as a
Claude artifact (no `<html>`, `<head>` or `<body>` tags; the browser adds them).

**Deploying:** with playtest v2's C4.1 (`wrangler pages deploy`), not yet.
