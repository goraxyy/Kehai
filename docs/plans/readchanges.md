# Redesign: Karen's behaviour and tricks

**Status:** draft plan, 2 November 2026. Nothing is built yet. Once you approve it, it gets built
as one PR, the way Karen work is done here.

**What's in it:** a short reminder of how things work today, the four changes you asked for (each
with what the code does now, the design, its risks and open questions), overlaps I found between
goals and tricks, and a list of questions only you can answer.

---

## 1. How it works today, on one screen

Twice a second Karen scores 11 goals and keeps the best one. The **goal** is *why*. Then she
picks a **trick**, which is *how*:

1. The gate removes tricks that aren't allowed right now.
2. The bandit scores what's left.
3. The planner turns the winner into steps.

| Goal | Wins when | Tricks |
|---|---|---|
| Patrol | Nothing else scores | Patrol |
| Investigate | She's fairly sure and the clue is fresh | Investigate, Ask the customers |
| Sweep | Her guess is spread out | Sweep, Ask the customers |
| Flush | She has lost you for 15 s or more | 12 tricks: Blackout, Mirror-black, Fog, Shelf sweep, the PA tricks, Phantom chime, Mimicry, Witness, Understudy |
| Deny | Always a little, more as you near the end | 9 tricks: Shelf sweep, Spill, Bin tamper, Tool theft, Task falsification, Overtime, PA task list, PA countdown, Camera bolt-on |
| Herd | She's at least 25% sure where you are | 8 tricks: Crate wall, Door lock, Shelf relocation, Funnel, Fog, Spill, Witness, Camera bolt-on |
| Ambush | The Ledger knows one of your routes | Ambush |
| Stalk | You're calmer than the Director wants | Stalk, Mirror-black, PA footsteps, Silence |
| Pursue | She can see you | The chase, Follow |
| Withdraw | You're more scared than the Director wants | Withdraw |
| Assist | Your energy is under 15% | The favour |

The guide explains each goal and trick in detail ("How Karen Learns", sections 4, 5b and 5c).

---

## 2. The changes you asked for

### 2.1 The PA lives in the staff room

**Today**

- `PaSystem` plays her voice through the ceiling speaker nearest you, after a 1.3 s chime, with
  subtitles on screen.
- She can use it from anywhere, instantly. Nothing about it is physical.
- Your counter is to jam it at the breaker box, which costs you a light circuit.

**The change**

- There's a PA console (a desk microphone) in the staff room.
- **Both of you use it there** (your answer, 2 November). Karen has to walk to it and stand at it
  to speak, and so do you.
- The console gets a model and a script.
- The staff room already holds the time clock and the coffee machine. With the PA there too, it
  becomes the one room you both need, so expect run-ins there.

**Example.** Today, PA decoy fires the moment she picks it, from wherever she is. After the
change, she walks about 35 m to the staff room (about 10 s at walking pace), the chime plays, she
speaks, and she's now far from the shop floor. That walk is your safe window.

**Your side of the console: you talk to the shoppers** (decided 2 November). The jammer stays at
the breaker box, as it is today.

- **What you get:** an announcement that moves the customers. That clears her witnesses away from
  where you're about to go.
- **What it costs you:** the building tells her the console was used, so she learns you're in the
  staff room.
- **Why it's a good trade:** fewer witnesses for her, in exchange for revealing where you are.
- **Jamming:** unchanged at the breaker box. While the PA is jammed, neither of you can use it.

**Proposed announcements** (proposals, not decided):

| You say | The shoppers | The catch |
|---|---|---|
| "Free coffee at the tills" | Walk to the checkouts | Some of them queue, so you have more customers to serve |
| "Aisle N is closed for cleaning" | Leave that aisle, and don't go in for a while | It names where you're headed |

- **A cooldown** stops you using it every few seconds. About 60 s, to be tuned in play.
- **The shoppers keep what they've already seen.** They're just further from where she'll look, so
  asking them costs her a longer walk, and they don't see you cross.

**Example of B.** You need to cross the shop floor, but two shoppers have seen you, and she can
walk up and ask them. You slip into the staff room and announce "Free coffee at the tills". The
shoppers head for the tills, and she now knows you're in the staff room. You have a few seconds
before she gets there.

**Design**

| Piece | What | Where |
|---|---|---|
| Model | A desk mic and amp, real-world metre scale | New prop. Materials or geometry only? (question 3) |
| Placement | Placed from C#, not by hand in the scene | `StoreLayout.cs` |
| On her map | A new landmark, `PaConsole`, found like the time clock | `StoreMap.cs` near line 860, plus a new value in `LandmarkKind` |
| The recipe | Every PA trick starts with "walk to the console", then chime and speak | `PaTactic` in `TacticsFacility.cs` |
| Risk score | Higher when she believes you're near the staff room, because she'd be seen | `ExposureRisk` on `PaTactic` |
| Your use | The console is something you interact with, like the breaker switches | `PaConsole` as an `IInteractable`, like `BreakerSwitch` in `LightsAndBreakers.cs` |
| Her sense of your use | Using the console is an event the building reports, like the time clock. It gives her a clue in the staff room. Rule 1 allows it, because it's the building reporting, not her reading your position. | A new observation through `Sensorium` |
| Jamming | Unchanged: jammed at the breaker box means no voice, for both of you | `PaSystem.Jammed` |

**Who uses the PA today.** This decides how far the change reaches (question 2).

| Uses the PA | How | Needs the console? |
|---|---|---|
| PA decoy, PA position, PA task list, PA countdown, The understudy | Spoken lines | Yes |
| PA footsteps | Her footsteps through one speaker | Probably yes |
| The favour | A chime as its warning, then a spoken line | Your call |
| Withdraw | Hold music as its warning | Your call |
| The overtime | "Thank you for your flexibility", played when you punch out | Probably no: it's the time clock talking |
| The Director | Occasional scrape or silence sounds through a speaker | Probably no |

**Risks**

- PA tricks get slower and rarer. The bandit may learn they're weak only because of the walk, so
  their cost and cooldown may need retuning.
- Fairness is unaffected: the chime still plays for 1.3 s before any speech.
- The test bots never use the PA, so the ablation won't exercise your side of the console. Tests
  have to cover it instead.

**Tests**

- Every PA trick's plan starts by walking to the console.
- A PA trick is refused when there's no console in the scene.
- Jamming still blocks the PA, for her and for you.
- Using the console yourself gives her a clue in the staff room, and nothing else about you.
- Each announcement moves the shoppers it should, and the cooldown holds.

### 2.2 An FX switch: no footsteps, no cone

**Today**

- **Footsteps** (`KarenBody.Footsteps`) play on every step while she moves:

  | Pace | Speed | A step every | Loudness |
  |---|---|---|---|
  | Run | 6.8 m/s | 0.3 s | 1.0 |
  | Hurry | 4.2 m/s | 0.4 s | 0.6 |
  | Walk | 3.4 m/s | 0.55 s | 0.6 |
  | Sneak | 2 m/s | 0.8 s | 0.25 |

- They're off while she's set to silent, which is how Ambush and Silence work.
- Each step is also posted to the noise bus. **The test bots hear it within 12 m and get scared**
  (`EnvWorld.cs:356`).
- **The cone** (`KarenFloorCone`) paints her view on the floor. Esc → Settings → "Floor cone"
  already turns it off. Her spotlight shows the same view and has no switch.

**The change, as I read it:** one FX switch that turns off her footstep sounds and her cone
together. You also floated a rule: her steps make noise only when she sprints. Those are two
different things:

| | A. FX switch (a setting) | B. Steps only when sprinting (a rule) |
|---|---|---|
| What it is | How the game looks and sounds for you | How the game works for everyone |
| The test bots | Unchanged: they still "hear" her | They hear her only when she runs, so old test numbers stop being comparable |
| Warnings | Unaffected: warnings are separate sounds (`KarenWorld.PlayTell`), not her steps | Unaffected, for the same reason |
| Tier-0 movement | Unchanged | Patrol, Investigate and Sweep become silent approaches, because they have no warning step and her steps are your only cue |
| The Silence trick | Still works | Loses its point, since she's already silent when walking. Redesign or draft it. |
| Difficulty | Harder only for players who choose it | Harder for everyone |

**Design for A**

- One setting, `KarenFx`, saved like the floor cone. It goes in Settings under the KAREN heading,
  next to or instead of "Floor cone".
- Optionally a hotkey. F3 is free: F1, F2 and F7–F10 are taken.
- When it's off, skip the step sound and hide the cone, and possibly the spotlight (question 4).
- Keep posting steps to the noise bus either way, so the bots and the shift record don't change.

**Design for B**

- In `KarenBody.Footsteps`, play and post the step only when her pace is Run.
- Her blind sprint (change 2.3) then becomes audible, which is a fair warning.

**Tests**

- With the switch off, no step sound plays and the cone is hidden, but noise-bus events still fire.
- With rule B, walking posts no step and running does.

### 2.3 Keep sprinting after she loses sight

**Today**

1. She sees you. Pursue scores 1.05, and she starts the chase (if tier 4 is allowed) or Follow.
2. You break her line of sight. Her awareness drains by 0.45 a second, so it falls from 0.85 to
   under 0.5 in about a second.
3. Pursue's score drops to 0. At her next decision (they run twice a second, once the chase is at
   least 2.5 s old), another goal wins. Usually it's Investigate, because she's still confident and
   the clue is fresh.
4. The chase plan is dropped, and she slows from a run (6.8 m/s) to a hurry (4.2 m/s).

Two details:

- The chase step itself (`Pursue` in `Primitives.cs`) would keep running toward her best guess for
  5 s. The goal switch cuts it short.
- Follow is her most-used trick in tests, and it never runs at all: it hurries.

**The change:** after losing sight she keeps sprinting toward where the grid says you're going to
be, not toward where you were.

**Example.** You sprint out of Aisle 3 toward the stockroom, and she loses you at the end of the
aisle. Today, about a second later, she slows to a hurry and checks Aisle 3. After the change, she
keeps running toward the stockroom door, where the chance in her grid is flowing.

**Design**

- **Don't let the goal switch kill a running chase.** The chase ends only by its own rules:
  nothing seen for a while, low confidence, or 25 s in all. This is one change in one place
  (`KarenBrain.Decide` or `GoalSet.Choose`).
- **Aim ahead, not behind.** In order of effort:
  1. Today's fallback: the peak of her grid, which is where you most likely are now.
  2. Better: run the grid's predict step forward on a copy, by the time it takes her to get there
     (distance ÷ 6.8 m/s), and aim at that peak.
  3. Intercept: head for the chokepoint you'd pass next on the way to your likely next job
     (`LikelyNextJobRegion`), if she can get there first.
  4. Later: the learned motion model (draft PR #68) gives a better forecast. Plug it in when it
     lands.
- **Giving up:** she stops when she reaches the aim point and sees nothing (that empty look is
  evidence too), when confidence falls under about 0.2, or after 25 s.
- Recovery after a chase stays as it is: 45 s of quiet.

**Fairness:** she still uses only her belief grid, never your real position (rule 1).

**Balance risk:** she's 5% faster than your sprint (6.8 m/s against about 6.5). If she sprints
blind to the right spot, breaking line of sight stops being an escape. Options:

- Run blind at your sprint speed rather than hers.
- Cap the blind part at 3–5 s.
- With rule 2.2B, you'd hear her coming.

**Tests**

- With a confident grid, a chase keeps running for at least N s after sight is lost.
- It ends when the grid spreads out.
- Rerun the ablation, with primary metrics written down first (section 5).

### 2.4 Draft tricks

**Today:** `TacticLibrary.All` (`Tactic.cs:87`) lists all 34 tricks, and any of them can be chosen
once it unlocks.

**The change:** a draft list. Drafted tricks stay in the code and the tests, but she can never
choose them. Shelf relocation goes first, and you'll send the rest.

**Design**

- **One list in one place,** written in C#, for example
  `TacticLibrary.Drafts = { "shelf_relocation" }`. Drafting or restoring a trick is a one-line edit.
- **The gate refuses drafted tricks** with the reason "draft", so the thought log and the debug
  overlay say why.
- **Drafted tricks stay in `TacticLibrary.All`.** Three things depend on that list staying put:
  - trick ids;
  - saves, because the Ledger keys what it has learned by trick id;
  - the policy's action list, which is sized from `All` (`PolicyMask.Size`).

  The mask simply never opens a drafted trick.
- **Goals look after themselves.** A goal whose tricks are all drafted scores 0 automatically
  ("no tactic available"). Drafting all four blockers would leave Herd with Fog, Spill, Witness and
  Camera bolt-on.
- The guide and the docs then say "34 tricks, N drafted".

**Tests**

- New: a drafted trick never appears as a candidate for any goal.
- Existing tests read `All`, so they keep passing.

**What shelf relocation does today**, for the record:

- It picks a sales-floor shelf at least 15 m from where she thinks you are, with 5 m of open floor
  beside it.
- She walks there, it grinds for 2 s, and she slides it 5 m over 4 s (`KarenWorld.SlideBay`).
- Then it checks that you can still reach every job and the clock, and rolls it back if not.
- You said its logic, model and scripts aren't finished. I didn't check whether the shelf's stock,
  drawn as GRD render objects since #35, moves with it.

**The list**

| Trick | Why it's a draft | Back when |
|---|---|---|
| Shelf relocation | Logic, model and scripts not finished | – |
| *(your list)* | | |

---

## 3. Overlaps between goals and tricks (proposals: tell me which you want)

| Overlap | Where | Proposal |
|---|---|---|
| Same name, two things | Patrol, Investigate, Sweep, Withdraw, Ambush and Stalk are each a goal and a trick. Logs read "Stalk(stalk)". | Rename the tricks' titles to verbs, for example the Stalk trick to "Peek" and the Ambush trick to "Lie in wait". Keep the ids, because saves are keyed by them. |
| A trick that contains another goal | Funnel and Blackout both end with a silent wait, which is an ambush, with their own numbers (60 s or 45 s against 90 s). | One shared "lie in wait" step with one set of rules. |
| Tricks competing for one spot | Crate wall and Funnel use the same min-cut. Funnel is several crate walls plus a wait. | Make Funnel a variant of Crate wall, or draft one of them. Two near-twins split the bandit's few samples. |
| One trick, two goals | 9 tricks, such as Spill (Deny and Herd). The bandit keeps one score per trick, not per goal. | Score per trick and goal pair. This fits the approved capture plan's contextual bandit, so it belongs there rather than here. |
| Two goals, the same movement | Investigate and Follow both hurry toward her best guess. | Fixed by change 2.3. |

---

## 4. Known fairness gaps to fold in

From the 1 November notes. This PR touches warnings and the PA, so it's the natural place to fix
them:

- **Overtime will raise a false alarm on shift 10.** Arming it has no warning step, so the auditor
  will count it. Its real warning is the 0.9 s buzz when you punch out.
- **Rule 6 is weaker than written.** `Karen.md` §9 says you must be able to finish in the time
  left. The code only checks that a route exists.
- **Rules 2, 5 and 6 have no direct test.**
- **Nothing checks you could actually hear a warning** (rule 3).

---

## 5. How we'll test it

- **Unity rules:**
  - batch runs only with your editor closed;
  - one heavy job at a time, holding the lock;
  - silent runs;
  - work in a worktree.
- **EditMode tests** as listed under each change.
- **Ablation:**
  - seeded and paired, with standard errors;
  - the cautious bot is held out of all tuning;
  - primary metrics written down before any run.
- **Proposed primary metrics:**
  - catches per shift, and escapes per chase (change 2.3);
  - PA tricks per shift, and their effect on panic (change 2.1);
  - fairness violations, which must stay at 0.
- **Note on rule 2.2B:** if it lands, the bots hear her less, so compare new runs only with new runs.

## 6. Order of work, inside the one PR

1. **Draft tricks (2.4):** small, and it unblocks the rest.
2. **Keep sprinting (2.3):** it decides how loud her running needs to be.
3. **FX switch (2.2).**
4. **PA in the staff room (2.1):** it needs the model and the layout.

## 7. Questions for you

1. **PA, answered 2 November:** you both have to be at the console, and you use it to talk to the
   shoppers. Still open: are the two proposed announcements in section 2.1 the right ones, and
   should the cooldown be about 60 s?
2. **PA:** which uses need the console: only spoken lines, or the chime cues too (the favour,
   withdraw's hold music, PA footsteps)?
3. **PA:** which staff room (the map has three), and should the console model have materials or
   be geometry only?
4. **FX:** a Settings switch, a hotkey, or both? Should it hide her spotlight too?
5. **FX:** should "steps only when sprinting" be the rule for everyone, or only when the switch is
   on?
6. **Sprint:** when she runs blind, at her full speed or yours, and for how long?
7. **Sprint:** should Follow also break into a run?
8. **Drafts:** which tricks besides shelf relocation?
9. **Overlaps:** which of the section 3 proposals do you want?

---

## Terms

| Term | What it is | In Kehai | In real life |
|---|---|---|---|
| Belief grid | A probability for every 1.5 m square of where you might be | She aims at its peak when she can't see you | A weather map's chance of rain, square by square |
| Awareness | A sight score that builds while she sees you and drains when she doesn't | 0.5 is "seen" and 0.85 "confirmed". It drains 0.45 a second. | A smoke alarm that needs a moment of smoke before it goes off |
| NavMesh | Unity's map of walkable floor, used for pathfinding | Her body walks on it, and the shelf move checks it to roll back | The road network a maps app routes you along |
| Min-cut | The fewest connections that split one area from another | Herd blocks these openings between you and the exits | Closing the fewest roads that cut a town off |
| Draft | A trick that exists in code but can't be chosen | Shelf relocation, until it's finished | A feature behind a switch that's off, like an app's beta toggle |
