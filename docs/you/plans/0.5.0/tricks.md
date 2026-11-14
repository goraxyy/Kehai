# More tricks

Part of the [Kehai 0.5.0 plan](../0.5.0-plan.md), item 1. The flash sale is the one you asked
for; the six after it were ideas. **You picked four on 14 November** (Q8): Screen loop, Room
lights, Freezer alarm and The complaint. False trail and Door jam are out.

Every trick keeps the rules of [Karen.md §17](../../../agents/karen/Karen.md#17-karen-030-the-tricks-come-back):
a tell before it lands; a chore that says where it is; hands-on tricks only at her hands (rule 9);
no new chore once the doors close (rule 10); a shift can always be won.

1. [Flash sale (asked for)](#1-flash-sale-asked-for)
2. [Six more ideas](#2-six-more-ideas)
3. [What every new trick costs](#3-what-every-new-trick-costs)

---

## 1. Flash sale (asked for)

| | |
|---|---|
| **What she does** | the PA chimes and she announces a sale: "Attention, shoppers! Two for one in aisle 7, for the next ten minutes." |
| **What changes** | the shopper limit goes from 2 to 4 (Q5: the limit only; they arrive as today). Never above the code's ceiling of 5 |
| **Why it hurts** | a longer queue, more spills and questions, more help to give; and more people who can tell her where you are when she asks the customers |
| **Your chore** | go to the PA microphone in the staff room and **hold E for 20 s** (your answer, Q4): "Hold E to call off the sale (20 s)". You announce the sale is over; the shoppers inside finish their lists, and the limit drops back to 2 |
| **Its tell** | the chime and her voice: the trick announces itself |
| **Kind** | a building trick, like Blackout: she needn't be there. Attacks your work and your cover among people |
| **It can't** | start while the PA is switched off at the PA box, or once the doors have closed (rule 10). Switching the PA off doesn't end a running sale (Q6): you switch it back on and hold the microphone, which in 0.4.1 needs the PA on. A sale running at closing ends there, since nobody comes in after closing |
| **Builds on** | 0.4.1's microphone (`ClosingMic.cs` on `claude/0-4-1-2305a2`) and its 20 s holds; the spawner on `main` |
| **The bots** | a new chore, `call_off_sale`, and a row in the trick check |
| **A combo she could learn** | Flash sale, then Ask the customers: more witnesses, and you're stuck at the microphone for 20 s |

## 2. Six more ideas

Each builds on something 0.4.0 or 0.4.1 added.

### 2.1 False trail

**Not in 0.5.0** (you, 14 November).

| | |
|---|---|
| **What she does** | makes a noise you'll hear on one side of the store (a door, a can knocked off a shelf), then goes the other way and waits on your path |
| **Builds on** | 0.4.0's mirror map: she knows roughly where you think she is, and plants a false picture |
| **Your chore** | none: you have to doubt what you heard |
| **Tell** | the noise itself is the tell; fairness check: she must really have been near it when it was made |
| **Kind** | hands-on (the noise is made at her hands); no chore, so rule 10 doesn't stop it |
| **Why it's good** | the one trick that shows off the mirror map, which is the most "ML" thing she has |

### 2.2 Screen loop

- [x] **Build in 0.5.0** (you, 14 November)

| | |
|---|---|
| **What she does** | freezes one of the office's camera screens on an empty view while she stands in that room |
| **Builds on** | 0.4.1's six store cameras and the screen wall |
| **Your chore** | reset it at the camera box (the 20 s hold), or learn to distrust that screen |
| **Tell** | the frozen screen's clock stops |
| **Kind** | hands-on at the office's screens, like Camera watch |
| **Why it's good** | makes the cameras a thing to read, not only a thing to use |

### 2.3 Room lights

- [x] **Build in 0.5.0** (you, 14 November)

| | |
|---|---|
| **What she does** | switches one room's lights off, not the whole circuit |
| **Builds on** | the lights already stay in their own room for rendering (`RoomLighting.cs`); [IDEAS.md §6.2](../../IDEAS.md#62-room-by-room-lights) |
| **Your chore** | a light switch by that room's door: one press |
| **Tell** | the lights flicker twice first |
| **Kind** | a building trick; leaves a chore, so not after closing |
| **Why it's good** | small, cheap, and you can use the same switch to hide (her darkness floor makes her slower to see you) |

### 2.4 Freezer alarm

- [x] **Build in 0.5.0** (you, 14 November)

| | |
|---|---|
| **What she does** | props a freezer open in the cold room: its alarm beeps and covers every footstep in that room, hers and yours |
| **Builds on** | the cold room's freezers, and sound that she and you both hear |
| **Your chore** | close the freezer (press E) |
| **Tell** | the alarm |
| **Kind** | hands-on; leaves a chore |
| **Why it's good** | fair both ways: she can't hear you either, so it's a gamble for her too |

### 2.5 The complaint

- [x] **Build in 0.5.0** (you, 14 November)

| | |
|---|---|
| **What she does** | a shopper she has spoken to goes to the till and waits to complain |
| **Builds on** | the customers she already talks to (Ask the customers) |
| **Your chore** | hold E at the till for 20 s to hear them out; they leave if you don't, and count as lost |
| **Tell** | the shopper's angry walk to the till |
| **Kind** | social; leaves a chore |
| **Why it's good** | ties you to one spot she knows, without anything breaking |

### 2.6 Door jam

**Not in 0.5.0** (you, 14 November).

| | |
|---|---|
| **What she does** | jams one pair of automatic doors: stuck open (shoppers stream in, the cold comes in) or stuck shut (the lobby becomes a dead end) |
| **Builds on** | 0.4.1's automatic doors, which now open for her too |
| **Your chore** | reset the door's control box by the doors (the 20 s hold) |
| **Tell** | the doors judder and stop with a clunk |
| **Kind** | hands-on; leaves a chore |
| **Why it's good** | changes the map for a while; her chokepoints change with it |
| **Risk** | stuck shut must never block the only way to the clock (rule M12 on the maze page) |

## 3. What every new trick costs

| Part | Where |
|---|---|
| The tactic, its tell, its chore, rule 9 and rule 10 | `Decision/Tactics/` |
| A bot verb and a chore the bots answer | `EnvWorld.Chores`, `SimulatedPlayers`, `KehaiEnv` |
| A row in the trick check | `TrickCheck` |
| A name in her learning records, the brains' list of choices and the model pack's known tricks | 0.4.0's learning code |
| A line in the shift report's "your answers" | `KarenLedger` |
| **No refresh round:** training happens once, after 0.5.0 and the vertical slice (Q1), so every trick is in first | — |
