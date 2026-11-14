# How Karen Learns: the four 0.4.0 pages

Four new pages for the How Karen Learns guide, one idea each, in the guide's order: after "+3
learned motion". Written on 11 November 2026 with the 0.4.0 build. **The published guide isn't
updated yet:** that's a publish, and it waits for your say. Nothing here is trained yet, so the
pages describe what's built and how it will be judged, not results.

---

## Page +5: two Karens, one body

**The idea.** Karen gets two trained brains. They share everything else: her eyes and ears, her
belief map, her tricks, her body, and every fairness rule.

| | Vital | Basic |
|---|---|---|
| Paid for | catching you, sooner is better | keeping your fear near its target, and a catch at the right moment |
| The Director's pacing | off | on |
| Reads your Ledger | no | yes |
| For | a benchmark, and a sparring partner for the bots | the game |

**What the brain decides.** Today Karen decides in two steps: which action (by hand-set scores),
then which trick. A brain answers both at once: one pair, like "Flush, with Blackout". The walk
there and the doing stay the planner's.

**When it's asked.** Only when the planner would re-decide anyway: a plan ended, something
interrupted it, or enough time passed. She still finishes what she starts.

**What it may pick.** Only pairs that can really run now. That's the **action mask**.

> **Action mask.** *What:* greying out the moves that aren't allowed. *In Kehai:* only legal
> (action, trick) pairs can be picked; for Basic the Director has already removed what pacing
> forbids. *Elsewhere:* a chess app that only lets you make legal moves.

*Try it:* she has lost you by the freezers, 90 s before closing. Basic may pick Search, Check, or
Flush with the phantom chime. Vital may also pick Hunt, and Flush with Blackout, because her pacing
is off. Neither may ever chase you outside the store.

---

## Page +6: everyone, and you

**The idea.** Karen keeps two stores of knowledge, and they never mix.

| | Everyone | You |
|---|---|---|
| What | the trained brain, the prediction model, her senses' numbers, the starting Ledger | your Ledger: what her tricks did to you, your routes, your hiding spots |
| Made | offline, from many players' shifts | while you play, on your computer |
| Changes | with a new build or a new model pack | after every trick |

**The blend.** Her guess for you mixes everyone's number with yours, by how much she's seen of you:

```
her guess = (n × yours + 2 × everyone's) ÷ (n + 2)        n = times she has seen it on you
```

*Try it:* everyone jumps at a blackout (+0.12). Three blackouts barely moved you (+0.01). Her
guess: (3 × 0.01 + 2 × 0.12) ÷ 5 = **+0.05**. She tries it less. And because a blackout attacks
your sight, her guesses for the other sight tricks drop a little too.

> **Blend by evidence (shrinkage).** *What:* mixing a group's average with one person's record, by
> how much of it there is. *In Kehai:* her guess for you, between everyone and you. *Elsewhere:* a
> new restaurant's star rating, pulled toward the average until it has many reviews.

**The model pack.** A better "everyone" can reach players without a new build: one signed data
file. The game checks the signature, that it's made for this build, and that every trick it names
exists. It's only sent out after it beats the current one on players it never saw, and only on
your yes. The fairness rules are code, so a bad pack can make her dull, never unfair.

---

## Page +7: the players

**The idea.** To train Karen you need opponents who behave like people: varied, with habits, never
the same shift twice. The bots get four layers.

| Layer | Made how | Example |
|---|---|---|
| Body | as today | walks, mops, opens doors |
| Reflexes | scripted, so fear always shows | a jumpy one freezes on a big startle |
| Choices | **trained** (ML-Agents) | "there's a queue, but she was just in aisle 4: go round, and hurry" |
| Habits and mood | a memory across six shifts | hides in Aisle 12 again; avoids the freezers after dying there |

**The persona: 14 numbers.** Diligence, caution, pace, habit, consistency, attention, curiosity,
grudge, calming down, and five fears (the dark, sounds, seeing her, her dashes, being chased). The
four old bots are four points among them, and play exactly as before.

**Never the same twice.** The bot draws from its chances instead of taking the top one, and its
consistency sets how loaded the dice are.

**Kept away from Karen.** The cautious bot and the careful corner (very cautious, slow players) are
never shown to any part of Karen before the final test. That's what makes the test fair.

---

## Page +8: where she thinks you're going

**The idea.** When she loses sight of you, she now guesses your **destination** first (a job, one
of her chores, the clock, a hiding spot), then the walk. Her belief's chances flow along the
shortest ways to those places.

> **Destination model.** *What:* guess where someone is going, then how they'll get there. *In
> Kehai:* "you're probably off to reset the fog box, 30 m away, at a walk." *Elsewhere:* Google Maps
> offering "Home?" when you start driving.

**Two helpers under it.**
- **Her senses, fitted.** About 20 hand-set numbers say how far to trust what she sees, hears and
  finds. They'll be fitted to recorded shifts, so when she says "70% you're in the stockroom", you
  are, 70% of the time.
- **The mirror map: where you think she is.** People steer by where they *last* saw or heard her,
  not where she is. So she keeps a guess at your guess.

> **Opponent modelling.** *What:* keeping a model of what the other side knows. *In Kehai:* the
> mirror map. *Elsewhere:* a poker bot tracking what its opponent believes it holds.

**How it's learned.** Supervised learning: every moment she can't see you is an example, and the
answer is where you really were. It's judged first on the moment she loses you: is your real
destination in her top three, and how sure is she of your cell 5, 10 and 20 s later?

> **Supervised learning.** *What:* learning from examples that come with the right answer. *In
> Kehai:* "you were really here". *Elsewhere:* a spam filter trained on mail people marked as spam.
