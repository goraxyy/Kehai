# The website

Three pages, `index.html`, `playtest.html` and `privacy.html`: plain HTML, CSS and JavaScript, no framework and no build step. Fonts come
from Google Fonts; there are no images, so nothing binary goes in git.

**The idea:** a clean product page in the store's own corporate voice, with Karen as the product.
Light mode is the store with the lights on, dark mode is after hours, and Karen's crimson appears
only where she does.

**What's on it:**

| Part | What it is |
|---|---|
| The console | A live top-down map of the real store (the grid from `docs/agents/store/STORE_MAP.md`, z −106 to −189). Your cursor is the worker. A simplified Karen hears your noise, keeps a belief map, crosses off what she sees empty, checks and hunts, at the game's speeds (walk 3.4, hurry 4.2, run 7.5 m/s against your 4 and 6.5). "Trip the breaker" shortens her sight. |
| The statement | One sentence that lights up word by word as you scroll (CSS scroll timelines; plain text where unsupported). |
| The receipt | Tonight's shift as a till receipt, beside the store's real numbers and aisle labels. |
| Three minds | The body, the director and the ledger, each with a small drawing. |
| The file | An example of what the Ledger keeps on you. |
| The blink | A button stands in for your eyelids: each blink, she closes about 45% of the gap (never nearer than 6 m) or vanishes and waits 8–16 m away. |
| The handbook | The fairness rules. |
| Now hiring | The playtest sign-up. |

**Every claim on the page** comes from `tools/marketing/facts.json` and avoids its banned phrases.
Change the game, then the facts, then this page.

**The other pages** (playtest v2, B4.1, 27 November):
- `playtest.html`: one button for the visitor's Mac or Windows build (the Worker's `/download/<mac|windows>`), the other platform's link, the build from `/latest`, the Open Anyway / Run anyway steps, and what's recorded.
- `privacy.html`: Q5's six months, Q7's deletion email, what is and isn't recorded, downloads, page visits and sign-ups.
- `site.css` is their shared style.
- Pictures (`assets/open-anyway.png`, `assets/run-anyway.png`) aren't in git: they live in `~/TokenLimit/website/assets/` and go up with each deploy. Without them the pages hide the picture and keep the words.

**The service's address:** `config.js` sets `window.KEHAI_API`, empty in git. The deploy writes the
Worker's address (`KEHAI_PLAYTEST_UPLOAD_URL`) into the copy it uploads. Until then, the sign-up and
the download button say they're a preview and send nothing.

**Previewing:** open `index.html` in a browser. It's written to work both as a whole page and as a
Claude artifact (no `<html>`, `<head>` or `<body>` tags; the browser adds them).

**Deploying:** with playtest v2's C4.1 (`wrangler pages deploy`), not yet.
