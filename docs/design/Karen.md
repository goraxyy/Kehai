# Karen

**Karen** — the store's management AI, and the thing that is hunting you.

> *"Employee wellbeing is a tracked metric. I am optimising it."*

This document specifies the adaptive antagonist for **Kehai**. It is a design + architecture
document, not an implementation. It describes what Karen perceives, how it thinks, how it adapts
across a single shift and across a career, the full library of things it can do to you, and how
all of that maps onto the systems that already exist in this project.

---

## 0. TL;DR

Karen is not a monster with a patrol route. It is a **facility** that happens to have a body.

It runs three minds at once:

| Mind | Knows | Controls | Timescale |
|---|---|---|---|
| **The Body** | only what it can sense | where it walks, what it does to you | frames |
| **The Director** | everything | pacing, tension, environmental events | seconds–minutes |
| **The Ledger** | everything you have ever done | which tactics get chosen at all | shifts–career |

The Body is honest and beatable. The Director keeps the shift from being boring. The Ledger is
the part that makes players say *"it learned me."*

The core loop it plays against you:

```
     you have jobs to do  ──────────────►  the jobs force you into the maze
              ▲                                        │
              │                                        ▼
     Karen adds jobs / breaks jobs  ◄────────  the maze is where Karen lives
```

Karen almost never wins by catching you. It wins by making the shift take longer than you have
energy for. **Kehai is the fail state. Karen's real weapon is overtime.**

---

## 1. Design pillars

Five rules everything below has to obey.

**1. The horror is administrative.**
An Alien kills you. Karen *assigns you more work*. Every scare should end with the player having a
new chore, a longer route, or less light — not just a raised heart rate. Fear that converts into
a task is fear the player carries for the next ten minutes.

**2. The Body never cheats, the Director never touches the Body.**
Alien Isolation's split. The Director may spawn a noise, cut the lights, or nudge a *search
region* — it may never teleport the Body, and it may never hand the Body your coordinates. If a
player reviews a replay, every single thing the Body did must be explainable from something it
could actually have sensed. This is a hard architectural boundary, not a guideline.

**3. Adaptation must be two-way.**
Hello Neighbor's failure mode is that the AI learns and the player just loses. Every Karen
counter-strategy must have a player counter-counter: cameras can be unplugged, the PA can be
jammed, traps can be spotted, the maze can be re-learned. If a player can't fight back against
the adaptation, it isn't adaptation, it's a difficulty slider with a story.

**4. Regulate dread, don't maximise it.**
Karen targets a *band* of player stress, not the top of it. A permanently terrified player goes
numb in ninety seconds. The quiet stretches are what make the loud ones work — so quiet is
something Karen actively schedules, not something that happens when it fails.

**5. Every decision must be legible.**
Karen keeps a running natural-language trace of its own reasoning. It is a debugging tool, it is
a design tool, and at the end of a run it is a *feature* — the post-shift screen shows you what
it was thinking. Being outsmarted is only satisfying if you can see how.

---

## 2. Anatomy: three minds

```
┌──────────────────────────────────────────────────────────────────────┐
│  THE LEDGER            persistent · cross-shift · knows you          │
│  player model · tactic values · route priors · difficulty calibration│
└───────────────┬──────────────────────────────────────────────────────┘
                │  biases (which tactics are even on the table)
                ▼
┌──────────────────────────────────────────────────────────────────────┐
│  THE DIRECTOR          omniscient · per-shift · knows the truth      │
│  panic estimate · pacing setpoint · tension budget · event scheduling│
└───────────────┬──────────────────────────────────────────────────────┘
                │  pressure, permissions, search hints (bounded)
                ▼
┌──────────────────────────────────────────────────────────────────────┐
│  THE BODY              honest · per-frame · knows only what it sensed│
│  sensorium → belief grid → appraisal → goal → plan → act             │
└──────────────────────────────────────────────────────────────────────┘
```

### 2.1 The Body

A NavMeshAgent with a probabilistic model of where you are and a planner that turns that model
into behaviour. It has no reference to `playerTransform` at all — that reference is the single
biggest thing separating the current `EnemyAI.cs` from Karen. It has a *belief*, and it acts on it.

### 2.2 The Director

Sees everything, controls nothing directly. Its job is the shape of the shift: when it's quiet,
when it isn't, and whether the Body is allowed to spend the big tactics right now. Its levers:

- **Tension budget** — a currency that regenerates over time and is spent on tactics. Big scares
  cost a lot; the budget is why Karen can't blackout-blackout-blackout.
- **Search bias** — may bias the belief grid toward the player's true region, capped hard (see
  §9). Used only when the player has been unthreatened for too long.
- **Environmental events** — noises with no author, a flickering light, a door chime. Free dread
  that costs the Body nothing and reveals nothing.
- **Permissions** — gates whole classes of tactic by shift number and by panic level.

### 2.3 The Ledger

A serialised profile of the player that survives death, quitting, and new shifts. It does not
decide *what happens*; it decides *what Karen is inclined to try*. Details in §7.

---

## 3. Perception: the Sensorium

Six sense channels. Each produces `Observation` records that feed one shared belief update.
Nothing in the game grants Karen a boolean "sees player" — everything is evidence with a
confidence and a timestamp.

```csharp
public readonly struct Observation
{
    public readonly SenseChannel Channel;   // Sight, Hearing, Trace, Testimony, Infrastructure, Absence
    public readonly Vector3 Position;
    public readonly float  Confidence;      // 0..1
    public readonly float  Timestamp;
    public readonly int    NodeId;          // aisle-graph node, -1 if off-graph
    public readonly bool   IsNegative;      // "I looked and there was nothing here"
}
```

### 3.1 Sight — graded, not binary

The existing cone-plus-raycast check becomes a **detection score** accumulated over time, the way
Alien Isolation and Splinter Cell handle awareness:

```
detect  =  angularFalloff · distanceFalloff · lightLevel · motionSalience · exposure
```

| Term | Meaning | Player counter |
|---|---|---|
| `angularFalloff` | 1.0 dead centre → 0 at cone edge | stay peripheral |
| `distanceFalloff` | inverse-square, clamped | keep distance |
| `lightLevel` | sampled from the lighting at your feet | stay out of lit aisles, kill the lights |
| `motionSalience` | sprint 1.0 · walk 0.55 · crouch 0.2 · still 0.05 | slow down |
| `exposure` | fraction of your capsule unoccluded, 3 rays not 1 | break silhouette behind a shelf |

Detection integrates upward while the score is positive and decays while it isn't, so a glimpse
through a gap between two `ShelfUnit`s does not instantly promote to a chase. **Crucially, the
flashlight raises your own `lightLevel`** — light is the resource you trade for safety.

### 3.2 Hearing — an event bus, not a physics query

Every noisy thing in the store already exists in code; it just needs to announce itself. One
static bus, cheap, no per-frame scanning:

```csharp
NoiseBus.Emit(new NoiseEvent {
    position  = transform.position,
    loudness  = 0.8f,          // metres of nominal carry
    kind      = NoiseKind.DroppedItem,
    authored  = NoiseAuthor.Player
});
```

| Source | Existing hook | Loudness | What it tells Karen |
|---|---|---|---|
| Sprinting | `PlayerMotor.IsSprinting()` | 0.9 | you are moving, and you have energy |
| Walking | `PlayerMotor` | 0.35 | rough bearing only |
| Crouch-walk | `PlayerMotor` | 0.1 | almost nothing |
| Dropping an item | `PickupInteractable`, `Q` | 0.7 | exact point, and *that you were carrying* |
| Mopping | `Dirt` hold-interaction | 0.5, sustained | you are stationary for 3s — free ambush window |
| Stocking a shelf | `ShelfSlot.FillWithNewItem` | 0.45 | which task you're on |
| Trash bag rustle | `TrashBag` while carried | 0.3, continuous | a *moving* noise — it can follow you by ear alone |
| Coffee machine | `CoffeeMachine.Interact` | 0.6 | you were low on energy, and where you go to fix that |
| Auto doors | `AutoDoubleDoor` | 0.55 | region transitions |
| Time puncher | `Puncher` | 0.8 | you are trying to leave |

Loudness attenuates **along the nav graph, not through walls** — a shout two aisles away around a
corner is quieter than one straight down the aisle. This is what makes the maze matter acoustically.

### 3.3 Traces — the world remembers you

Physical evidence with a decay clock. This is the sense that makes the store feel like it's
watching even when nothing is.

| Trace | Left by | Lifetime | Read as |
|---|---|---|---|
| Wet footprints | walking through a spill or a mopped patch | 45 s, fading | a **directed trail** — Karen gets a bearing, not just a point |
| A gap on a shelf | `ShelfSlot.TakeItem` | until refilled | someone worked here recently |
| A *filled* shelf it sabotaged | `ShelfUnit.IsFull` flipping back | permanent | you undid its work — it knows you were here and it knows when |
| Item on the floor | `Q` drop | until picked up | your carry state, and your panic (dropped things = fleeing) |
| The mop away from its `ToolSnapPoint` | `ItemHome` | until returned | where you're working now |
| Bagged bin | `Trashcan.UsageCount` reset | until re-filled | task progress |
| An open door held open | `HingeDoor` | seconds | you went through *and were in a hurry* |

Traces are the reason Karen can be terrifying without ever seeing you: it walks into an aisle,
finds a bagged bin and wet footprints heading north, and now has a hard prior on half the store.

### 3.4 Testimony — the customers are its sensor network

**This is the mechanic the maze exists for.**

The store is full of `CustomerNPC`s doing their own routine. Each carries a tiny short-term
memory: `lastSawEmployeeAt`, `when`, `confidence`. Karen doesn't read their minds — the Body has
to physically pass near a customer to "ask", which costs it time and puts it in the open.

```csharp
// Body, on approaching a customer within 2m
foreach (var c in CustomerNPC.NearbyOf(transform.position, 2f))
    if (c.Memory.HasSighting && !alreadyPolled.Contains(c))
        sensorium.Submit(c.Memory.ToObservation(decayedConfidence: c.Memory.Age));
```

Consequences that fall out of this for free, without writing a line of special-case code:

- **The store is more dangerous when busy.** A packed shift is a dense sensor grid.
- **Serving customers is a risk decision.** Standing at the till puts you in front of the most
  reliable witness in the building, for as long as it takes.
- **Hiding *among* customers works** — motion salience and exposure both drop in a crowd.
- **Emptying the store helps you and hurts your score.** Ignore the queue and you're harder to
  find, but `TaskManager.AllCustomersServed` is false and you can't clock out. Real dilemma.
- **A customer who was near a scare is a jumpy witness** — higher confidence, wider recall radius.

### 3.5 Infrastructure — it owns the building

Karen is the *store*. Static sensors it always has, and which the player can attack:

| Asset | Gives Karen | Player counter | Cost of the counter |
|---|---|---|---|
| CCTV nodes | permanent low-confidence sighting in a cone | unplug it (hold-interact, 4 s) | Karen notices the dead camera and *investigates the blind spot* |
| Door sensors | region-transition events, storewide | prop a door open | that door now never reports — and never closes behind you |
| The POS terminal | knows when a customer was served | serve away from the till (not possible yet) | — |
| PA system | it can *speak* (§8) | jam it from the powerbox | costs a breaker slot you might need for lights |
| Powerbox | knows which breakers are on | it's yours to control too | the panel is deep in the maze |

Unplugging a camera is a beautiful trap: it buys silence and it *announces that you exist*. A
dead sensor is information.

### 3.6 Absence — the sense that most AIs skip

When Karen sweeps a cone and finds nothing, that is an observation with `IsNegative = true`. It
subtracts probability. Without this, search AI re-checks the same three aisles forever and the
player learns to stand still. With it, Karen *clears* the map methodically and closes in — which
is the single most frightening property a searcher can have.

---

## 4. Belief: where it thinks you are

Karen never stores `lastPlayerPosition`. It stores a **probability distribution over the whole
store**, and updates it like a Bayes filter. This is the single change that turns a stalker into
an investigator.

### 4.1 The grid

The nav mesh is voxelised into cells of ~1.5 m (tunable), one float each. For a 60×60 m store
that's ~1600 live cells — trivial. Cells are linked by the **aisle graph** (§5), so probability
flows the way a body can actually walk.

```
b(c)  =  P(player is in cell c)          Σ b(c) = 1
```

### 4.2 Predict — probability leaks the way you can move

Every 100 ms, mass diffuses along graph edges:

```
b'(c) = Σ  T(c | c') · b(c')
        c'
```

`T` is not uniform. It is weighted by:

- **Traversal cost** — you flow down open aisles faster than through a blocked one.
- **Your movement budget.** Karen knows whether it forced you into a sprint, and it knows the
  store's coffee machine usage. If it believes you're out of energy, `CanSprint == false`, and
  the diffusion radius it uses **shrinks accordingly**. Getting burnt out doesn't only stop you
  running — it *narrows the search area*. That is the mechanic and the theme in one equation.
- **Goal attraction.** Karen knows the shift's task list (it wrote it). Mass flows preferentially
  toward unfinished work: understocked `ShelfUnit`s, live `Dirt`, a full `Trashcan`, the
  `TrashContainer` out back if a bag is active. **You are predictable because you are employed.**
- **Ledger priors.** Your historical route habits bias `T` (§7.6).

### 4.3 Correct — evidence, including the absence of it

```
b(c)  ∝  b'(c) · P(observation | player in c)
```

- Positive sighting → a tight Gaussian bump, `Confidence` sets the width
- Noise → a bump smeared along the nav graph at the attenuated radius
- Trace → a bump plus a **directional push** (footprints have a heading)
- Testimony → a wide, age-decayed bump
- **Negative** → `b(c) *= (1 - detect(c))` for every cell it just swept

Then renormalise. If total mass collapses (everything was ruled out), reset to the graph-wide
prior — Karen "loses the scent" and starts over, and the player can *feel* that happen.

### 4.4 What the Body reads off the grid

| Quantity | Formula | Drives |
|---|---|---|
| **Peak** | `argmax b(c)` | where to go |
| **Confidence** | `max b(c)` | whether to hunt or to sweep |
| **Entropy** | `H = -Σ b log b` | how lost it is; high H → tactics that flush you out |
| **Containment** | mass inside the region it has blocked | whether the trap is worth springing |
| **Staleness** | time since the last positive observation | when to give up and go back to sabotage |

Entropy is the interesting one. When Karen doesn't know where you are, it doesn't wander — it
**does something that makes you make a noise**. High entropy is the trigger for a blackout, a
shelf sweep, or a PA announcement. It is deliberately creating evidence.

---

## 5. The maze as a graph

The store is not a room; it's a topology. Karen reasons about it as one.

### 5.1 The aisle graph

Authored once (or baked from the nav mesh) as nodes = aisle segments / junctions / rooms, edges =
walkable connections with a width and a door flag.

```
        [BACK ROOM]───[STORE ROOM]───[POWERBOX]
             │              │
        [AISLE 7]──────[JUNCTION C]──────[AISLE 6]
             │              │                │
        [AISLE 8]──────[JUNCTION B]──────[AISLE 5]───[COFFEE]
                            │
        [TILLS]────────[JUNCTION A]────────[ENTRANCE]
```

### 5.2 Chokepoints and articulation points

On graph build, Karen precomputes the **articulation points** — nodes whose removal disconnects
the graph. These are the places where standing still, or dropping a crate wall, cuts the store in
half. Standard DFS lowpoint algorithm, computed once, recomputed when the maze mutates.

This is why Karen's blockades feel intelligent rather than random: it isn't picking a corridor, it
is picking *the* corridor, and it can tell you why.

### 5.3 Herding by min-cut

To push you toward a region (the back room, the dark half, the dead end near the tills), Karen
solves a small max-flow/min-cut on the aisle graph: source = your belief peak, sink = everywhere
you'd rather be. The **min-cut edges are its shopping list of things to block** — with its own
body, a crate wall, a locked door, or a spill you won't want to cross.

It rarely blocks all of them. Blocking all but one and standing near the last is how you build a
funnel, and a funnel the player walks into voluntarily is far better than a scripted corridor.

### 5.4 Influence maps

A cheap secondary layer painted over the graph: *danger* (where Karen has been recently, where the
noise came from) and *desire* (where the player's unfinished tasks are). Karen can read the
difference to predict your route, and — for herding — it can raise danger somewhere without ever
going there, using a PA burst or a light flicker.

### 5.5 Maze mutation

The shelving is on castors. Between shifts (later, *during* them), Karen may relocate a
`ShelfUnit`, seal a door, or open a staff passage. Effects:

- Player route memory is invalidated on a schedule, so mastery decays and the store stays a maze
- Articulation points move, so the player's learned safe pockets stop being safe
- It's a **legible** escalation: you walk in on shift 7 and the store is *wrong*

The rule that keeps this fair: mutation never lengthens the shortest path from the entrance to
any required task object by more than a factor (≈1.4), and never creates a dead end with no
second exit while a chase is possible.

---

## 6. Thinking: the deliberation loop

### 6.1 The tick

Nothing here runs every frame except the cheap parts. Everything is amortised and budgeted, so
one Karen costs less than the ~40 customers already in the scene.

| Stage | Rate | Budget | What it does |
|---|---|---|---|
| **Sense** | every frame | 0.05 ms | cone test, drain the `NoiseBus`, trace triggers |
| **Believe** | 10 Hz, sliced | 0.2 ms | predict + correct over ⅓ of the grid per tick |
| **Appraise** | 5 Hz | 0.05 ms | recompute threat/entropy/containment, read Panic Index |
| **Decide** | 2 Hz **or on event** | 0.3 ms | score every goal, pick one |
| **Plan** | on goal change only | 1–3 ms | HTN decomposition, anytime |
| **Act** | every frame | 0.05 ms | run the behaviour tree leaf, steer the agent |

"On event" matters: a gunshot-loud noise re-decides immediately rather than waiting up to 500 ms.
That's the difference between reactive and sluggish.

### 6.2 Appraisal

Turn raw state into the handful of scalars the decision layer actually uses:

```csharp
struct Appraisal
{
    public float Confidence;     // max belief         → hunt vs search
    public float Entropy;        // how lost            → flush vs sweep
    public float Staleness;      // seconds since sight → give up?
    public float Containment;    // is the funnel closed?
    public float Panic;          // Director's estimate of the player (§7.4)
    public float Pressure;       // Director's setpoint error (§7.5)
    public float Tension;        // budget available for big tactics
    public float TaskLoad;       // how close the player is to clocking out
}
```

`TaskLoad` is read straight from the existing `TaskManager` — `MopQuotaMet`, `ShelvesStocked`,
`TrashEmpty`, `AllCustomersServed`. **When you are one task from freedom, Karen's incentive to
break something spikes.** That's a one-line utility term and it produces the whole late-shift
panic.

### 6.3 Goal selection: utility, not states

The current `AIState` enum becomes a set of **goals**, each scored continuously. Highest score
wins, with hysteresis so it doesn't flap.

| Goal | Roughly wants | Peaks when |
|---|---|---|
| `Patrol` | maintain coverage, refresh negative info | nothing else scores |
| `Investigate` | resolve a specific observation | confidence mid, staleness low |
| `Sweep` | reduce entropy systematically | entropy high, no leads |
| `Flush` | *create* evidence (blackout, sweep a shelf, PA) | entropy high, staleness high |
| `Deny` | break the task the player is closest to finishing | `TaskLoad` high |
| `Herd` | close min-cut edges, shrink the player's world | containment achievable |
| `Ambush` | pre-position and go silent at a place they must come to | route prior confident |
| `Stalk` | be seen, then leave | panic *below* setpoint |
| `Pursue` | close distance | confidence very high |
| `Withdraw` | deliberately disengage and let them breathe | panic *above* setpoint |
| `Assist` | help the player, sincerely, near burnout | energy < 0.15 (§10.3) |

```
U(g) = w_conf ·Confidence(g)
     + w_stress·ExpectedPanicDelta(g)      ← from the Ledger, per-player
     + w_task  ·TaskLeverage(g)
     + w_novel ·Novelty(g)                 ← anti-repetition
     - w_cost  ·(TensionCost(g) / Tension)
     - w_risk  ·ExposureRisk(g)            ← being seen is a real cost
     × Permission(g)                       ← Director gate: 0 or 1
```

`Withdraw` scoring highly is not a bug. An antagonist that knows when to leave is the rarest and
most memorable thing in the genre.

### 6.4 Planning: HTN over a tactic library

A goal is abstract. An HTN planner decomposes it into primitive tasks the Body can execute:

```
Goal: Deny(Restock)
  └─ Method: SabotageShelvesFarFromPlayer            [pre: Confidence < 0.4]
       ├─ SelectShelf(maximise walk time from belief peak, minimise own exposure)
       ├─ NavigateTo(shelf, cover-weighted path)
       ├─ Wait(until player noise is not adjacent)   ← don't get caught doing it
       ├─ EjectSlots(count = 4, spacing = 0.4 s)     ← ShelfSlot.Eject(), already exists
       └─ RetreatTo(nearest low-visibility node)
```

Two properties worth having:

- **Anytime.** If the planning budget expires, use the best partial plan. Karen never stalls.
- **Reactive replanning.** Plans carry preconditions; a violated precondition (you walked into
  the aisle it was sabotaging) aborts to the BT's interrupt branch, not to a frozen agent.

Every method and primitive is a **ScriptableObject asset**. Adding a new scare is authoring an
asset with preconditions, cost, tension price, cooldown, and expected-panic prior — not editing
a switch statement. That's the extensibility story, and it's what makes the tactic library in §8
open-ended rather than fixed.

### 6.5 Execution

A small behaviour tree per primitive, with a global interrupt branch (`SeeingPlayerAtRange`,
`LoudNoiseAdjacent`, `PlanPreconditionViolated`). The BT is deliberately dumb; all the
intelligence lives upstream. That separation is what keeps it debuggable.

### 6.6 The thought log — explainability as a feature

Every decision appends one human-readable line to a ring buffer:

```
[212.4] BELIEF   peak=Aisle6 p=0.34 H=2.81 stale=19.2s  (noise: DroppedItem@Aisle6, 0.7)
[212.4] GOAL     Deny(0.71) > Sweep(0.62) > Investigate(0.55) > Patrol(0.10)
[212.4]   why    TaskLoad=0.75 (3/4 tasks clear) · tension=0.8 · novelty(Deny)=0.9
[212.5] PLAN     SabotageShelvesFarFromPlayer → Shelf_A2 (walk 22 s from peak, exposure 0.12)
[229.1] SENSE    NEGATIVE sweep Aisle6 → belief redistributed to Aisle7/BackRoom
[231.0] GOAL     Ambush(0.80) > Deny(0.66)
[231.0]   why    route prior: player reaches TrashContainer via BackRoom in 81% of shifts
[231.0] PLAN     Ambush(BackRoom door) · go silent · abort if panic > 0.85
```

Three uses, in increasing order of value:

1. **Debug.** An on-screen overlay with the belief grid rendered as a heatmap and the top three
   goal scores. You can watch it think.
2. **Design.** Tuning a utility weight is guesswork until you can read why a choice lost.
3. **Player-facing.** The post-shift screen prints a redacted version as Karen's *performance
   review of you*: "Employee took 4 min 12 s to restore lighting. Employee's preferred
   concealment: Aisle 8, north end. Noted." Being outplayed is only fun when you can see the play.

---

## 7. Adaptability

"Adaptive AI" usually means one of these three things and pretends to mean all of them. Karen does
all three, at explicitly different timescales, with different mechanisms.

| Timescale | Mechanism | Feels like |
|---|---|---|
| **Seconds** — reactive | Bayes filter + utility + replanning | *"it's actually looking for me"* |
| **Minutes** — a shift, online | multi-armed bandit + panic setpoint control | *"it changed tactics on me"* |
| **Shifts** — persistent, offline | the Ledger: player model, route priors, calibration | *"it learned me"* |

### 7.1 The player model

Serialised to disk, updated continuously, never reset by death. Roughly 40 floats.

| Group | Features |
|---|---|
| **Movement** | sprint fraction · crouch fraction · mean speed · look-behind rate (yaw reversal frequency) · time-to-flee after a sighting |
| **Space** | per-node dwell histogram · concealment spots used (node + count) · preferred route between each task pair · panic-flight destinations |
| **Work** | task completion order · mean time per task type · whether they batch or interleave · how far they carry the mop before dropping it |
| **Economy** | coffee usage timing · burnout level at clock-out · whether they hoard energy or run it to zero |
| **Response** | measured panic delta per tactic · habituation curve per tactic · recovery time after a chase · whether they investigate noises or avoid them |
| **Skill** | shifts survived · caught count · mean shift duration vs. par · counter-play used (cameras unplugged, PA jammed, traps spotted) |

The two that matter most in play: **preferred route** and **concealment spots**. Those two turn
`Ambush` from a gimmick into the scariest goal in the list.

### 7.2 Tactic selection as a bandit

Each tactic in §8 is an arm. Karen doesn't know in advance whether *you* find darkness scarier
than being followed — it finds out, cheaply, and stops wasting the ones that don't land.

```
score(a) = Q̂(a)                                  ← learned mean panic delta
         + c · sqrt( ln(t) / n(a) )               ← UCB exploration: try the untried
         - λ · exp( -(t - tLast(a)) / τ )         ← habituation: recency is a penalty
         + prior(a, shiftNumber)                  ← authored pacing prior
```

Three notes on why this specific shape:

- **`Q̂` is per-player, not global.** Its update is `Q̂ ← Q̂ + α(observedPanicDelta - Q̂)`, with the
  observation window starting at the tactic's execution and running for ~20 s.
- **The habituation term is the horror design.** A jumpscare works once. A tactic used two minutes
  ago is penalised heavily and decays back over `τ ≈ 4 min`. Variety is *emergent* from this term
  rather than enforced by a shuffle.
- **The authored prior is not optional.** Pure bandits produce week-one chaos. The prior carries
  the intended escalation curve (§10.2) and its weight decays as `n(a)` grows. Designed early,
  learned late.

Optionally: Thompson sampling instead of UCB, keeping a Beta posterior per arm. Smoother, less
jittery early, and it makes the "Karen is uncertain about you" phase read better.

### 7.3 The Panic Index — the reward signal

You cannot learn what scares a player without measuring fear. You can't measure fear. You *can*
measure its motor consequences, and horror players leak them constantly.

| Proxy | Read from | Weight |
|---|---|---|
| Sprint burst rate | `PlayerMotor.IsSprinting()` transitions/min | 0.20 |
| Yaw jitter | camera angular velocity variance — looking behind you | 0.20 |
| Path inefficiency | actual path ÷ optimal path to your apparent goal | 0.15 |
| Task abandonment | starting a `Dirt` hold-interaction and releasing early | 0.15 |
| Freeze | standing still, not interacting, > 2 s | 0.10 |
| Drop events | `Q` presses that aren't near a snap point | 0.10 |
| Burnout slope | `BurnoutSystem.energy` derivative | 0.10 |

Normalised per-player against their own baseline (measured in the first calm 60 s of shift 1 —
some people just play twitchy), combined, then run through an asymmetric EMA: **fast attack,
slow decay**, because fear spikes and drains slowly.

```
Panic ∈ [0,1],  τ_attack ≈ 0.4 s,  τ_decay ≈ 25 s
```

This single scalar is the reward for the bandit, the input to the pacing controller, and the
best telemetry the project will have.

### 7.4 Pacing as control theory

The Director holds a **setpoint**, not a maximum:

```
        panic
          1 ┤                    ╭─╮
            │         ╭─╮       ╱   ╲        ╭──╮
   setpoint ┼ ─ ─ ─ ─╱─ ─╲─ ─ ─╱─ ─ ─╲─ ─ ─ ╱─ ─ ╲ ─ ─ ─
            │   ╭───╯     ╰───╯       ╰────╯      ╰──╮
          0 ┤───╯                                    ╰──
            └──────────────────────────────────────────► time
              settle    build    spike   recover   build
```

```
e        = setpoint(phase, shiftNumber) - Panic
pressure = Kp·e + Ki·∫e dt
```

- `pressure > 0` → raise `Permission` on escalating tactics, shorten cooldowns, allow search bias
- `pressure < 0` → gate the big tactics off, boost `Withdraw`'s utility, let the store go quiet

The integral term is what stops Karen from letting a *consistently* calm player coast: a long
stretch below setpoint accumulates and eventually forces an event. And a player pinned at 0.95 for
two minutes gets genuine silence, which is when the next scare gets its power back.

The setpoint itself rises across the career and dips right after any chase — a guaranteed
recovery window (§9).

### 7.5 Route denial and spot blacklisting

The two most personal-feeling adaptations, both straight reads of the Ledger:

**Route denial.** Your preferred path from tills → back room is stored as a node sequence. Once
Karen has ≥5 samples with low variance, `Herd` starts scoring the min-cut against *that specific
path*. Blocking the corridor you always use, on the shift after you established the habit, is
the moment players say the AI is learning. It is also completely explainable in the thought log.

**Concealment blacklisting.** Every time you break line of sight and stop, the node is recorded.
Frequently-used spots get:
1. a permanent bump to the belief prior (Karen checks there first),
2. `Sweep` ordering that visits them early rather than by distance,
3. eventually, physical denial — a crate stack, or a camera bolted to that exact shelf.

The counter has to exist, and it does: spots decay out of the blacklist if unused, so *changing
your habits actually works*. The player learns that the correct response to an adaptive AI is to
stop being predictable. That's the whole game in one loop.

### 7.6 Counter-adaptation — the player's side of the table

Non-negotiable per pillar 3. Every Karen capability has an attack surface:

| Karen capability | Player counter | Its cost |
|---|---|---|
| CCTV network | unplug nodes (4 s hold) | dead cameras draw investigation |
| PA system | jam it at the powerbox | uses a breaker slot you may want for lights |
| Customer testimony | serve and clear the store | but `AllCustomersServed` is also required to leave |
| Trace: footprints | mop your own trail | costs mop time and counts against nothing |
| Trace: dropped items | tidy up behind you | slow, and standing still is loud |
| Route priors | vary your route | slower routes cost burnout |
| Concealment blacklist | rotate hiding spots | you have to *find* new ones, in the dark |
| Ambush | listen — an ambushing Karen is silent, and silence is a tell | requires noticing absence |
| Traps | spot the tell and disarm | time, always time |

Everything costs time, and time costs energy, and energy is the fail state. That's the pressure
that holds the whole design together.

---

## 8. The tactic library

Every entry is a ScriptableObject: preconditions, tension cost, cooldown, expected-panic prior,
the tell it must emit, and the *chore it generates*. Grouped by which sense it attacks.

### 8.1 Attacking sight

**Blackout** — trips breakers at the powerbox; the store goes to emergency lighting or fully dark.
- **The chore:** get the flashlight from the break room, cross the maze to the powerbox, reset
  three breakers in sequence, each with a distinct hum (audio puzzle in the dark).
- **The trade:** the flashlight is a battery-limited cone that raises your own `lightLevel` term.
  Light means seeing and being seen. Karen's sight is unaffected by darkness — it *prefers* it.
- **Escalation:** partial blackouts (one wing), rolling blackouts, and eventually killing the
  lights **behind you as you walk**, so the dark is a wave you're staying ahead of.
- **Tell:** a ballast whine and a two-second flicker before the cut.

**Fog** — sabotages a freezer, flooding an aisle with cold vapour. Kills your visibility, not its.

**Camera bolt-on** — physically installs a camera covering your favourite hiding spot. Silent,
permanent, and unmistakably personal.

**Mirror-black** — kills a single light directly above you, leaving the rest of the store lit.
Cheap, purely psychological, no chore. The one it uses when panic is just *slightly* under target.

### 8.2 Attacking your work

**Shelf sweep** — the existing `ShelfSlot.Eject()` scaled up: a whole `ShelfUnit` stripped, four
items at a time.
- **The chore:** `ShelvesStocked` flips false, the unit lights up yellow, you fetch the crate.
- **The double purpose:** loose items on the floor are a **noise carpet**. Walking over them
  emits a `NoiseEvent`. It just built itself an alarm across the aisle you use most.
- **Learned targeting:** the sweep goes where you *aren't*, so the fix costs maximum walking.

**Spill** — kicks over the mop bucket at a chokepoint.
- **The chore:** a new `Dirt` instance and a 3-second stationary hold to clear it.
- **The trap:** walking through it un-mopped leaves a **wet trail** Karen can read for 45 seconds.
  You either spend the time, or you wear a tracking beacon.

**Bin tamper** — re-fills a `Trashcan` you just emptied, or splits a bag so it must be re-bagged.
- Targets `TrashEmpty` — the task with the longest travel time, out back, away from everything.

**Task falsification** — *the best one.* Karen writes to your HUD.
- Adds a task that doesn't exist, un-checks a completed one, or greys one out as done when it
  isn't. `TaskListUI` is fed by Karen, not by `TaskManager`, and can disagree with it.
- **The chore:** you have to physically verify. The HUD stops being ground truth.
- **The tell (mandatory):** a one-frame flicker and a faint CRT tick when a line is falsified,
  so an attentive player can catch it. Without a tell this is unfair; with one it's a skill.
- **The escalation:** it starts falsifying *correctly* — adding a task you really do have to do.
  Now you can't dismiss the HUD either.

**Tool theft** — takes the mop from its `ToolSnapPoint` and hides it. Learned placement: it goes
in the node you visit *last*.

**The overtime** — refuses the `Puncher`. The clock-out is rejected, `TimeRemaining` gets minutes
added, and the PA thanks you for your flexibility. This is the thesis of the game as a mechanic.

### 8.3 Attacking sound and trust

**The PA system.** Karen owns the tannoy, and it is not obliged to be truthful.
- Reads your task list back at you, slightly wrong
- Calls you by the name you entered at the start
- Announces a cleanup in an aisle it is not in (audio decoy — it wants you to go and look)
- Plays footsteps, its own, from somewhere else
- Announces your position "for customer convenience" — and the customers all turn to look
- Counts down your remaining shift time when you're behind

**Phantom chime.** Triggers the `AutoDoubleDoor` with nobody there. You have to check. It's free.

**Silence.** The Body stops emitting footstep audio entirely for a stretch. Players habituated to
tracking it by ear lose their tracker, and only notice the absence after several seconds.

### 8.4 Attacking space

**Crate wall** — stacks crates across a min-cut edge. Not permanent, but clearing it is loud and
takes 6 seconds standing still.

**Door lock** — seals a door on the aisle graph. Recomputed articulation points make one blockade
worth three.

**Shelf relocation** — moves a `ShelfUnit` and changes the maze. Loud, slow, and you can hear it
happening somewhere you can't see (§5.5).

**Funnel** — the composite: block all min-cut edges but one, position near the survivor, and let
you walk into it. No scripting, just §5.3 executed properly.

### 8.5 Attacking the social layer

**Mimicry.** Karen takes over a `CustomerNPC`. That shopper stops shopping. It doesn't queue. It
walks at exactly your speed, one aisle over, and faces you when you look. Everything else in the
store is a real customer, which is what makes this work — the maze is full of ambiguity and Karen
just weaponised it.
- **The tell:** possessed customers don't have a `IsWaitingToBeServed` state and never generate a
  till queue. A player who's paying attention can prove it. A player who isn't just feels wrong.

**The witness.** Herds a real customer into the aisle you're hiding in — free testimony, and you
can't tell it to leave.

**The understudy.** From shift ~8: a "new hire" NPC that follows you "to learn the job". It is a
mobile sensor with a name badge. It is unfailingly polite.

### 8.6 Attacking the player directly

**Stalk-and-withdraw.** Appears at the end of an aisle, holds for two seconds, and leaves. Does
not pursue. Cheapest, most re-usable dread in the library, and the bandit will find out fast
whether it lands on this particular player.

**Ambush.** Pre-positions at a place the route prior says you must come to, then goes fully
silent — no pathing, no audio, no highlight. Waits up to 90 s. Aborts if panic exceeds 0.85
(pillar 4). This is where the Ledger cashes out.

**The chase.** Rare and expensive. Costs almost the whole tension budget, has a guaranteed
recovery window afterwards, and drains burnout via the existing `SetChaseState(true)`. Being
caught is not death — it's **a written warning, thirty seconds of lecture, and lost shift time.**
Kehai's fail state is the clock, not the claw.

**The favour.** Below `energy < 0.15`, Karen turns helpful: it brews you a coffee, it mops a spill,
it tells you where the mop is, and it means it. A burnt-out employee who stays is the outcome it
was optimising for the whole time. Nothing about this is a trick, and that's the horror.

---

## 9. The fairness contract

An adaptive antagonist is one bad decision away from feeling like a cheater. These are hard
constraints, enforced in code, asserted in tests.

1. **No omniscient body.** The Body may not read the player transform. Ever. Enforce it by not
   giving `KarenBody` a reference — perception writes to the belief grid and nothing else can.
2. **Bounded Director hinting.** Search bias may shift belief mass toward the player's true
   region by at most `+0.15` per minute, may never exceed `0.5` of total mass, and may never
   trigger while the player is in line of sight of the Body's current path.
3. **Every tactic has a tell**, ≥ 0.8 s of lead time, perceivable through at least one sense the
   player currently has. If the lights are out, the tell must be audible.
4. **Recovery windows are guaranteed.** After a chase or a caught event: ≥ 45 s at reduced
   setpoint, no major tactics, no ambush.
5. **Never deny a nearly-finished task.** No sabotage of a task above 80% completion — that reads
   as spite, not tension. (Exception: the final shift of the career, deliberately, once.)
6. **Never make a shift unwinnable.** Before executing, every tactic checks that a path to
   `AllComplete` still exists within `TimeRemaining` plus a margin. Blocking the last route to
   the last task is a bug, not a difficulty setting.
7. **Adaptation is reversible.** Every learned prior decays without reinforcement. Changing your
   behaviour must visibly work within two shifts, or the learning is a punishment ratchet.
8. **Determinism on demand.** Seeded RNG and a replayable decision log, so any "that was
   bullshit" moment can be replayed and adjudicated. This is a QA feature and a trust feature.

---

## 10. Game flow

### 10.1 Anatomy of a shift

```
  CLOCK IN ──► SETTLE ──► BUILD ──► SPIKE ──► RECOVER ──► CRUNCH ──► CLOCK OUT
   Puncher      ~60s      2-3min    ~30s      ~45s       last 90s     Puncher
      │           │          │        │          │           │           │
      │      baseline   tactics    the big   guaranteed   TaskLoad   blocked until
      │      panic      escalate   one       quiet        spikes     AllComplete
      │      measured   with       (§8)      (§9.4)       utility    (+ maybe
      │                 pressure                                     refused, §8.2)
      ▼
   customers begin arriving (CustomersAllowed = true)
```

The phases are not scripted timings — they're **setpoint segments** for the controller in §7.4.
A player who is calm through BUILD gets a bigger SPIKE; a player who panics in SETTLE gets a
gentler one. Same curve shape, different amplitude, every time.

Meanwhile the honest pressure never stops: `ShiftManager.TimeRemaining` runs down, customers keep
queueing, `BurnoutSystem.energy` drains (twice as fast when you run), and `TaskManager` refuses
to let you leave until the store is clear. Karen doesn't have to create the stress. It only has to
lean on it.

### 10.2 The career

Escalation is carried by the authored `prior(a, shiftNumber)` term (§7.2), so it's a designed
curve that the bandit is free to deviate from once it knows you.

| Shift | Introduced | Intent |
|---|---|---|
| 1 | Patrol, Sabotage (existing behaviour) | teach the job; measure baseline panic |
| 2 | Stalk-and-withdraw, Phantom chime | it is *aware* of you |
| 3 | **Blackout** + flashlight + powerbox | the first real chore-scare; teaches light↔visibility |
| 4 | Spill traps, wet-footprint traces | teaches that the world remembers you |
| 5 | PA lies, audio decoys | teaches that information is not trustworthy |
| 6 | **Mimicry** — a customer that isn't | teaches that *people* are not trustworthy |
| 7 | **Maze mutation** — the store is wrong today | destroys route mastery |
| 8 | Ambush from route priors, the understudy | it has learned you specifically |
| 9 | **Task falsification** — the HUD lies | destroys the last trusted surface |
| 10 | The overtime — clock-out refused | the thesis |
| 11+ | Full library, learned weights, rising setpoint | it is just better at this than you |

Each row is a **new verb for the player**, not just a bigger number. That's the Hello Neighbor
lesson done right: escalation that teaches instead of escalation that grinds.

### 10.3 The endgame

Three ways a career ends, and the AI is complicit in all three:

- **You quit.** Clock out enough shifts, walk out. Karen's performance review is your ending text.
- **You burn out.** Energy hits zero repeatedly and the shifts stop being survivable. Karen's
  `Assist` goal ramps up as you decline — it becomes *kind*. The last thing that happens is that
  it makes you a coffee. This is the title of the game.
- **You break it.** Full counter-play: cameras down, PA jammed, routes randomised, traps read.
  Karen's confidence collapses, its thought log goes to high entropy, and it starts making
  visibly worse decisions. **A player who defeats the AI should be able to watch it lose.**

---

## 11. Scenarios

Four traces of the system running, with the thought log interleaved. These are the acceptance
criteria — if the implementation can't produce these, it isn't done.

### Scenario A — "The lights, and the long way back"

Shift 4. Two tasks left: three spills and a half-empty aisle. Panic sits at 0.28, below setpoint
0.45, and the integral term has been accumulating for ninety seconds.

```
[188.0] DIRECTOR pressure=+0.31 (below setpoint 92s) → permit tier-2 tactics
[188.2] BELIEF   peak=Aisle3 p=0.51 H=1.9  (mop noise, sustained, 0.5)
[188.2] GOAL     Flush(0.74) > Deny(0.61) > Stalk(0.44)
[188.2]   why    player stationary & audible; Blackout novelty=0.8, Q̂=+0.24, last used 6m ago
[188.3] PLAN     Blackout(full) · tell=ballast whine 1.2s · then Ambush(Breakroom door)
```

The lights flicker, hum, and die. You are mid-mop in aisle 3 with no flashlight — it's in the
break room, and the break room is four junctions away through a maze you now can't see.

Panic jumps to 0.71. Karen doesn't chase. It walks to the break room door and stops making noise.

```
[201.4] SENSE    NEGATIVE sweep Aisle3 → mass redistributed
[201.4] GOAL     Ambush(0.83) > Sweep(0.51)
[201.4]   why    route prior: Breakroom reached via Junction B in 7/8 prior shifts (conf 0.88)
[201.5] PLAN     Ambush(Junction B) · silent · abort if panic > 0.85
```

You take Junction B, because you always take Junction B. It's standing in the dark.

You didn't get caught — it withdrew at 0.85 as the contract requires. But you now know that the
route you always use is *known*, and the rest of that shift you take the long way, which costs
you forty seconds and a chunk of energy you needed. **The scare converted into overtime.**

### Scenario B — "The shelf that fixed itself"

Shift 9. You're one task from clocking out: restock. You fix aisle 6, and the HUD ticks over to
all-clear. You walk to the puncher.

```
[402.1] APPRAISE TaskLoad=0.94 (1 task remaining, 88% complete)
[402.1] GOAL     Deny(0.88) > Herd(0.40)
[402.1]   why    denying the final task maximises expected shift extension (+2.4 min est.)
[402.1] CHECK    fairness rule 5 — task at 88% > 80% → Deny(Restock) VETOED
[402.2] GOAL     Deny(0.62) [alt target] > Herd(0.40)
[402.2] PLAN     TaskFalsification(Restock → show complete) · tell=1 frame flicker + tick
```

The HUD says you're done. `TaskManager.ShelvesStocked` says otherwise, because while you were
walking, four items came off aisle 2 — quietly, at the far end, out of earshot.

You press E on the puncher. It refuses. There is no explanation on screen, because the screen is
the thing that's lying.

Rule 5 held: it did not sabotage the task you were finishing. It sabotaged your *knowledge* of a
different one. And the tell was there — one frame — for a player who's learned to watch for it.

### Scenario C — "The customer who didn't shop"

Shift 6, busy. Eleven customers in the maze, three queued at the till, and Karen has no idea where
you are — entropy 2.9, staleness 40 s.

```
[95.0]  BELIEF   H=2.91 stale=40.2s conf=0.09 → LOST
[95.0]  GOAL     Flush(0.69) > Sweep(0.55)
[95.0]    why    high entropy: prefer tactics that generate observations over tactics that consume them
[95.1]  PLAN     Mimicry(Customer_07) — nearest to belief centroid; cost 0.3 tension
```

Customer_07 stops browsing. It doesn't go to the till. It walks the aisles at a constant pace and
turns to face you every time you're in its cone.

You can't be sure. Everything else in the store is genuinely a shopper. If you run, you generate
noise and confirm yourself. If you keep working, it stands three metres away and watches you mop.

```
[131.6] SENSE    Testimony(Customer_07) → sighting Aisle8, conf 0.95
[131.6] BELIEF   peak=Aisle8 p=0.77 → hunting
```

The flush worked, and it worked because the *player* had to decide whether an ambiguity was a
threat. That's the design goal: Karen's best plays are the ones where you defeat yourself.

### Scenario D — "The quiet shift"

Shift 12. Panic has been above setpoint for four minutes — a blackout, a chase, a bagged bin
split open in the back room. Then:

```
[540.0] DIRECTOR panic=0.91 sustained 240s, setpoint=0.55, pressure=-0.36
[540.0] DIRECTOR → habituation risk HIGH · gating tiers 2-4 · setpoint → 0.30 for 90s
[540.1] GOAL     Withdraw(0.77) > Patrol(0.30)
[540.1]   why    diminishing returns: Q̂(Chase) fell 0.31→0.12 over last 3 uses
```

Karen walks to the far end of the store and patrols like it's shift 1. The PA plays hold music.
The lights stay on. Nothing happens for a minute and a half.

It is the worst ninety seconds of the game, and Karen spent tension budget to buy it. Then panic
decays to 0.34, the setpoint climbs back, and everything it does next works again.

---

## 12. Implementation map

Nothing here throws away what's built. `EnemyAI.cs` becomes the execution layer at the bottom of
a stack, and the existing systems become Karen's sensors and levers.

### 12.1 What already exists and what it becomes

| Existing | Role under Karen |
|---|---|
| `EnemyAI.cs` | → `KarenBody` — keeps `NavMeshAgent` handling, loses `playerTransform`, loses the FSM |
| `EnemyAI.CanSeePlayer()` | → `SightSensor`, returns a graded score, not a bool |
| `ShelfSlot.Eject()` / `ShelfUnit.FillAll()` | the `ShelfSweep` tactic's primitives — already correct |
| `ShelfSlot.All` registry | O(1) target selection, no scene scans — already correct |
| `TaskManager` (`MopQuotaMet`, `ShelvesStocked`, `TrashEmpty`, `AllCustomersServed`) | the `TaskLoad` appraisal input and the `Deny` goal's target list |
| `TaskListUI` | the surface `TaskFalsification` writes to |
| `BurnoutSystem.SetChaseState()` | already wired; extend with `Assist` and the movement-budget read |
| `PlayerMotor.IsSprinting()` | motion salience, noise loudness, and a Panic Index proxy |
| `CustomerNPC` | testimony sensors; `Mimicry`'s puppets |
| `Dirt` + `IHoldInteractable` | the spill trap and the wet-trace source |
| `Trashcan` / `TrashBag` / `TrashContainer` | the `BinTamper` tactic |
| `HighlightInteractable` / `OutlineHighlight` | tells and trap affordances |
| `ShiftManager` | phase clock for the pacing setpoint |
| `AutoDoubleDoor` / `HingeDoor` | door control, phantom chime, region-transition sensing |

### 12.2 New files

```
_Game/AI/Scripts/
  Core/
    KarenBody.cs                 execution: agent, BT runner, interrupts
    KarenDirector.cs             panic estimate, setpoint PI controller, tension budget
    KarenLedger.cs               serialised player model, bandit state, save/load
    KarenBlackboard.cs           the appraisal struct, shared read-only snapshot
  Perception/
    Sensorium.cs                fuses channels → Observation stream
    SightSensor.cs              graded detection
    NoiseBus.cs                 static event bus (+ NoiseEvent, NoiseKind)
    TraceRegistry.cs            decaying physical evidence
    TestimonyCollector.cs       polls nearby CustomerNPC memories
    CustomerMemory.cs           on CustomerNPC: lastSawEmployeeAt / when / confidence
  Belief/
    BeliefGrid.cs               occupancy filter: predict / correct / normalise
    AisleGraph.cs               nodes, edges, articulation points, min-cut
    InfluenceMap.cs             danger / desire layers
  Decision/
    GoalSet.cs                  utility scoring + hysteresis
    HtnPlanner.cs               anytime decomposition
    Tactic.cs                   ScriptableObject base
    Tactics/                    one asset + script per entry in §8
  Debug/
    ThoughtLog.cs               ring buffer + formatting
    KarenDebugOverlay.cs         belief heatmap, goal scores, plan tree
```

### 12.3 Phasing

Each phase is shippable and playable on its own. Do not build this in order of interestingness.

| Phase | Deliverable | Why first |
|---|---|---|
| **1** | `NoiseBus` + `SightSensor` + `BeliefGrid`, driving the *existing* FSM | The single biggest felt improvement: search stops being a straight line to `lastPlayerPosition`. Ship this before anything else. |
| **2** | `AisleGraph`, negative information, `ThoughtLog` + overlay | Search becomes systematic and you can finally see why it does things |
| **3** | `GoalSet` utility layer replacing the FSM; `Deny` reading `TaskManager` | Behaviour becomes situational; the "administrative horror" pillar comes online |
| **4** | Tactic ScriptableObjects: Blackout, ShelfSweep, Spill, Stalk | The first content wave; needs the flashlight + powerbox props |
| **5** | `KarenDirector`: Panic Index, setpoint control, tension budget | Pacing stops being random |
| **6** | `KarenLedger`: player model, bandit, route priors, `Ambush` | The part that makes people talk about it |
| **7** | Mimicry, task falsification, maze mutation, the endgame | Requires everything above to land properly |

### 12.4 Budget

One Karen, worst case, on a mid-range machine: **< 0.7 ms/frame**, with belief updates sliced
across three ticks and planning happening only on goal change. For scale, the store already runs
~40 `CustomerNPC` agents; Karen should cost less than three of them. Everything expensive
(min-cut, articulation points, graph bake) is precomputed or amortised.

---

## 13. Tuning, telemetry, evaluation

The claim "adaptive AI" is worth nothing without a way to check it. Three things to build
alongside:

**1. Simulated players.** Three scripted bot profiles — *Efficient* (optimal routes, low panic),
*Skittish* (over-reacts, hides constantly), *Reckless* (sprints everywhere, ignores noise). Run
200 headless shifts each. The Ledger should converge to visibly different tactic distributions
per profile. If it doesn't, the bandit isn't learning anything and the reward signal is broken.
This is the cheapest possible proof that adaptation is real.

**2. Determinism + replay.** Seeded RNG, decision log persisted per shift. Any "that was unfair"
report is reproducible, and rule violations from §9 can be asserted in automated tests.

**3. Live telemetry.** Panic Index over time, setpoint tracking error, per-tactic `Q̂` and usage
counts, time-to-detect, chase frequency, mean shift overrun. The single healthiest metric is
**setpoint tracking error** — if the Director can hold panic near its target across wildly
different players, the whole system is working.

Sanity checks that fail loudly in CI:

- The Body never dereferences the player transform (assembly-level assertion)
- No tactic fires without emitting a tell ≥ 0.8 s prior
- After any chase, ≥ 45 s of reduced setpoint with no tier-2+ tactics
- Every shift remains completable: a path to `AllComplete` exists at every tick

---

## 14. Open questions

- **How much should Karen's learning transfer to a new save?** Fully persistent is scarier;
  fully reset is fairer to a returning player. Probably: persist skill calibration, reset habits.
- **Does the player ever get told there's a learning AI?** Hello Neighbor advertises it up front;
  Alien Isolation says nothing. Saying nothing and letting a player *notice* is stronger — and
  the post-shift performance review is the reveal.
- **Two bodies?** A second Karen unit doubles the sensor coverage and halves the safe space.
  Probably a late-career escalation rather than a base feature.
- **Should mimicked customers be provably distinguishable?** Currently yes (§8.5). Removing the
  tell late in the career is tempting and probably a mistake.
- **How far does `Assist` go?** A Karen that genuinely does your job for you, so that you stay
  another shift, is the darkest version of this game and possibly the best one.

---

## 15. Implementation status

Everything in §§2–13 is built. This section maps the design onto the code and says where the
code departs from the plan above. Results of the ablation ladder are in
[`KAREN_RESULTS.md`](../results/KAREN_RESULTS.md); how to run things is in [`tools/eval/README.md`](../../tools/eval/README.md).

### Where each part lives (`Assets/!_Project/_Game/`)

| Design | Code |
|---|---|
| §2 three minds | `AI/Scripts/Core/KarenBody.cs`, `KarenDirector.cs` (+ `PanicIndex`), `KarenLedger.cs`, tied together by `KarenBrain.cs` |
| §3 Sensorium | `AI/Scripts/Perception/` — `SightSensor`, `NoiseBus`, `TraceRegistry`, `CustomerMemory` (testimony), `Sensorium` (+ infrastructure feed, absence sweeps, `EnergyBelief`), `PlayerPresence` (what the senses can pick up) |
| §4 belief | `AI/Scripts/Belief/BeliefGrid.cs` — predict/correct over the store map's cells, desire/habit/danger layers (the §5.4 influence maps live here) |
| §5 the maze as a graph | `Map/Scripts/StoreMap.cs` — cells, regions, doorways, articulation points, Edmonds–Karp min-cut; readable export in [`STORE_MAP.md`](STORE_MAP.md) |
| §6 thinking | `AI/Scripts/Decision/` — `Goals.cs` (utility + hysteresis, HTN planner), `Tactic.cs` (anytime `PlanBuilder`, `PlanTree`), `Primitives.cs`, `KarenContext.cs` (the blackboard) |
| §6.6 thought log | `AI/Scripts/Diagnostics/ThoughtLog.cs` (ring buffer → `karen_logs/shift_###_seed.jsonl`), `KarenDebugOverlay.cs`, `PerformanceReview.cs` |
| §7 adaptation | bandit (UCB or Thompson, habituation, authored prior) and player model in `KarenLedger.cs`; Panic Index and PI pacing in `KarenDirector.cs` |
| §8 tactic library | `AI/Scripts/Decision/Tactics/` — 35 tactics across tiers 0–4 |
| §8 the building's levers | `AI/Scripts/World/` — `KarenWorld` (the one place effects are requested), `PaSystem`, `LightsAndBreakers`, `KarenProps`, `HudAndPuppets`, `MazeMutation`, `Consequences`, `ProceduralAudio` (every tell, synthesised) |
| §9 fairness | `AI/Scripts/Core/FairnessGuard.cs` + the rules asserted in `_Tests/Editor/KarenTests.cs` |
| §13 evaluation | `Eval/Scripts/` — `KehaiEnv`, `SimulatedPlayers`, `AblationRunner`, `EnvServer`; see `IDEAS.md` §§1–2 |
| blink channel | `Blink/Scripts/` + `tools/blink/` (see `IDEAS.md` → Blink) |

Karen is installed from code when the store loads (`KarenBootstrap`), since the scene is not in
version control. `-nokaren`, `-karen-rung <A-F>` and `-karen-seed <n>` work on the command line.

### Keys

Every key in the game is in `CONTROLS.md` (and in the game under Esc → Keys). Karen's:

| Key | What |
|---|---|
| Esc | settings: restart the shift, volume by kind of sound (Karen's own slider included), mouse, her floor cone, the webcam |
| F1 | live map of the store (you, Karen, her guess, customers, sounds, jobs) and a plain-words story of what Karen is doing; **H** adds her belief heat map, **T** the technical view |
| F2 | replay of the shift so far on the same map: drag the timeline, Space plays, 1/2/3 speed, ←/→ jump 5 s, O opens the full report |
| F8 | blink channel consent panel (webcam path stays off until you agree); starts the camera helper |
| F9 | guided blink calibration (~12 s): eyes open, closed until the beep, then three blinks it counts |
| F10 | blink test panel: a checklist of consent → helper → signal → calibration, and a live open/closed meter and graph; R restarts the helper, V switches camera, M switches between Apple Vision and MediaPipe |
| B | keyboard blink — hold to keep your eyes shut |
| O | after a shift: open the shift report in the browser |
| Enter / Q | after a shift: continue / hand in your notice (from shift 5) |

### Her speed

Sneak 2, walk 3.4, hurry 5 and run 6.1 m/s (`KarenConfig`). Walking she is a little slower
than a walking employee (4 m/s); hurrying she outpaces one; running she is only just slower
than a sprint. `KarenBootstrap` caps every pace at 94% of the player's actual sprint speed, so a
sprinting employee can always pull away, whatever the scene sets the sprint to. The one
exception is the blink lunge (up to 3 m at 11 m/s while your eyes are shut), which is the
point of the blink channel.

### Her gaze on the floor

`KarenFloorCone` paints her field of view on the floor: the same range and angle as her
`SightSensor`, cut short by shelves and walls, brightest where her eyes weigh a sighting most
(close and straight ahead). Blue while she walks her rounds, orange when she's noticed
something, red when she's hunting; it pulses red while she can actually see you. Her spotlight
says the same, but a spotlight is hard to read in a lit store. Esc → Settings can hide it.

### Shift records

`ShiftRecorder` records every shift: where you, Karen and each customer were five times a
second (and what each customer was doing, including the shelf they asked for), every sound
with how far it carried, every job, and Karen's story in plain words. At the end of a shift it
writes `shift_NN_<date>.json` and a self-contained `shift_NN_<date>.html` report to
`<persistentDataPath>/shift_records/` (on a Mac, `~/Library/Application Support/TokenLimit/Kehai/shift_records/`).
The report replays the shift on the floor plan and adds `ShiftAnalysis`: jobs, time per area,
how often and where Karen spotted you, the closest she got, chases, warnings, and what she
tried most. The floor plan itself (`StoreFloorPlan`) comes from the NavMesh, the shelves and
the wall colliders, so the map is the store as it actually is.

### Where the code departs from this document

- **Tactics are C# classes, not ScriptableObjects.** The repository holds scripts only, so data
  that would live in assets is authored in code. Each tactic is one class with its tell, chore,
  tier, cooldown and prior, registered in `TacticLibrary`.
- **`AisleGraph`, `InfluenceMap` and `KarenBlackboard` were folded** into `StoreMap`, `BeliefGrid`
  and `KarenContext`/`Appraisal` respectively; `TestimonyCollector` is the `poll_witnesses` tactic
  plus `CustomerMemory`.
- **Walls come from physics, not the NavMesh.** The store's walls are 3 cm boxes the NavMesh bake
  missed, so `StoreMap` builds connectivity from physics sweeps, and `NavMeshWalls` carves each thin
  wall into the runtime NavMesh at load. Before that, every NavMesh path — shoppers' and Karen's —
  could run through walls.
- **Doors.** `HingeDoor.Use(from, author)` is the one way a door opens: the E key, Karen (who opens
  doors in her way, audibly, and whose own passage doesn't trip her door sensor) and the eval agent
  all go through it. A door now swings away from whoever opens it, and its panel is carved out of
  the NavMesh while it stands open.
- **Maze mutation rebakes from physics colliders**, not render meshes: the store's floor is a
  statically combined mesh that can't be read at runtime in a build (or headless), and a bake from
  render meshes came back without a floor. People are kept out of the bake, and the few props with
  non-readable mesh colliders stand in as boxes while it runs.
- **Tell accounting.** The fairness guard checks each effect against its own plan's latest tell, so
  an unrelated PA chime can't count for (or against) it; a PA line is a tell in itself (the chime
  always plays 1.3 s before the words), and tells are timed on the clock, not by counting frames.
  Across the final ablation (216 shifts, `KAREN_RESULTS.md`) the guard recorded none.
- **A written warning always comes with a release window** (lecture + 15 s with no second catch), at
  every rung — without it the simplest rungs could catch you again the moment a lecture ended.
- **Learning scope per rung** follows `IDEAS.md` §2 exactly: C uses the authored prior, D learns
  within a shift, E keeps what it learnt (with the decay that makes changed behaviour visible within
  two shifts), F adds the blink channel.
- **Not built:** the infinite maze (`IDEAS.md`), a second Karen body (§14), and the Sentis in-engine
  blink model is written but only compiles once the `com.unity.ai.inference` package is added and
  `KAREN_SENTIS` is defined.

### The CI sanity checks from §13

`_Tests/Editor/KarenTests.cs` (21 edit-mode tests; `Unity -batchmode -runTests -testPlatform EditMode`):
the body, belief and decision code never read the player (source scan), every tactic has a tell of
at least 0.8 s and a chore, the bandit learns and habituates and explores, the Ledger forgets within
two shifts, the Director gates big tactics while settling, seeds are deterministic, the map reaches
every region from the time clock, belief collapses on a sighting and spreads again, sweeps remove
mass, the search bias is capped, and the thought log, eval wire format and blink packets parse.
