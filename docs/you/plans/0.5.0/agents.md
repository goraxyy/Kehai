# Bots into agents

Part of the [Kehai 0.5.0 plan](../0.5.0-plan.md), item 2. **Your answer (Q9, 14 November):**
better test players, using AI/ML, close to reality, and adaptable. That's option A, made
adaptable (the plan's §7). **No Claude-played players** (Q10): the panel below is kept only as
the record of why it was considered.

1. [What exists](#1-what-exists)
2. [Five meanings of "agent"](#2-five-meanings-of-agent)
3. [What a language-model player costs](#3-what-a-language-model-player-costs)
4. [My recommendation, after your answer](#4-my-recommendation-after-your-answer)

---

## 1. What exists

| Player | Where | How it decides | Speed |
|---|---|---|---|
| Four scripted bots (efficient, skittish, reckless, cautious), with a fear model | `main` | hand-written rules | a 150 s shift in 5–15 s in the test environment ([IDEAS.md §3](../../IDEAS.md#3-an-agent-test-environment)) |
| Trained bots: a mind trained with Unity ML-Agents, a persona of 14 numbers, habits, moods and dice | 0.4.0 branch | a small network, asked at each choice (40–80 choices a shift) | 15–30 s (0.4.0's batch shifts run at 5–9× real time) |
| **Claude playing a shift** | `main`: `tools/eval/llm_agent.py` | reads the store as prose (only what a player could know), calls one tool, `act`, with one of 17 actions; the real player body does it | one model call per action |

The test environment it plays through, with its six failure labels (starvation, thrashing,
interference blindness…), is [IDEAS.md §3](../../IDEAS.md#3-an-agent-test-environment).

**What the 0.4.0 results add:** the bots are what the prediction model learns from, and its
destination models lost; and she almost never loses a bot in a chase. More varied players, and
players that get away, matter more than they did.

## 2. Five meanings of "agent"

| | A. 0.4.0's bots | B. Language-model player | C. Hybrid | D. Language-model "coach" | E. Copies of people |
|---|---|---|---|---|---|
| **What** | trained mind + persona + reflexes | Claude plays every action, as `llm_agent.py` | Claude sets a goal every 20–30 s of play ("get the till, then hide by the freezers"); the trained mind and reflexes carry it out | Claude writes each bot's persona, goals and habits between shifts; the bots play as A | a network trained on people's replays (behaviour cloning) |
| **Speed** | 15–30 s a shift | minutes a shift (2–10 s per call, the game waits) | about a minute | as A | as A |
| **Money** | none | §3 | about a fifth of B | pennies | none |
| **Realism** | varied, but bot habits | reasons about goals like a person; no hand–eye skill; reads prose, not pixels | goals of a person, body of a bot | more varied characters, same bodies | the most human |
| **Needs** | built | an API key and a cap | B plus a new link in the bots' code | B's harness | about 100 players |
| **For training her** (millions of choices) | yes | far too slow and dear | too slow | yes | yes, once there's data |
| **For testing her** | yes | yes: a mind that isn't hers or the bots' | yes | yes | yes |

## 3. What a language-model player costs

**The sum:** cost of a shift = choices in a shift × (input tokens per call × input price + output
tokens per call × output price).

**Assumptions** (to be replaced by C2.4's measurement):
- **60 choices** a shift (0.4.0's bots make 40–80).
- **6,000 input tokens** a call: the rules, the store map and the last few observations. Most of
  it is the same every call, so prompt caching can cut that part's price to a small fraction.
- **400 output tokens** a call, including its reasoning.
- **Prices:** Anthropic's API price list as of 3 November 2026, per million tokens (input /
  output): Haiku 5.5 $0.10 / $0.50, Sonnet 5.5 $2 / $10, Opus 5.5 $4 / $20. The batch discount
  doesn't apply: a player must answer at once.

| Model | Per shift, no caching | With caching (rough) | 384 shifts (one 0.4.0 test arm) |
|---|---|---|---|
| Haiku 5.5 | 60 × (6,000 × $0.10 + 400 × $0.50) / 1,000,000 ≈ **$0.05** | about $0.03 | about $10–20 |
| Sonnet 5.5 | 60 × (6,000 × $2 + 400 × $10) / 1,000,000 ≈ **$0.96** | about $0.50 | about $190–370 |
| Opus 5.5 | 60 × (6,000 × $4 + 400 × $20) / 1,000,000 ≈ **$1.92** | about $1 | about $370–740 |

**Training her against it is out of the question:** 0.4.0's plan needs about 6 million choices,
which is about 100,000 shifts: thousands of dollars even on Haiku, and months of waiting.

**Your plan or an API key?** `claude -p` on your plan costs no extra money, but its limits are
shared with the build sessions that ran out this week. A separate API key with its own monthly
cap keeps the two apart (Q10).

## 4. My recommendation, after your answer

- **A, made adaptable** (your answer): the trained bots get a memory within a shift and their own
  record of her across shifts, and the scorecard checks they adapt like people (the plan's
  C2.6–C2.9). Only A (and E, later) are fast and cheap enough for her to train against.
- **No panel (B)** (Q10, 14 November). The rest of this section is why it was considered.
- **B becomes a held-out panel:** three to five fixed players, each a short written character
  ("careful, hates the dark, always serves the queue first") matched to a persona. Haiku 5.5 for
  most shifts, Sonnet 5.5 for a few. No part of Karen is ever trained or tuned against them; they
  are reported beside the scripted bots, the trained bots and you, like the cautious player.
- **What it buys:**
  - a test of whether she copes with a mind that isn't hers or the bots';
  - escapes the prediction model never trained on;
  - the agent test environment's results table across AI models (IDEAS §3), almost for free;
  - a showpiece for a behaviour-engine company: a fair AI, tested against a language-model agent.
- **C later,** if the panel shows it's worth the work.
- **The existing `llm_agent.py`** asks `claude-opus-5`; it moves to the current models, with a
  cost meter and a hard spending stop (C2.1).
