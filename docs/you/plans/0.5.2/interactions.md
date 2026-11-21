# Interactions

Part of the [Kehai 0.5.2 plan](../0.5.2-plan.md), item 2: "Some interactions like 'visible hiding'
in 0.5.2; I will add later." The questions (Q numbers) are in the plan's
[open questions](../0.5.2-plan.md#11-open-questions). There's a table for your own at the end:
[§5](#5-your-interactions).

1. [What an interaction is here](#1-what-an-interaction-is-here)
2. [Visible hiding: what 0.5.1 shipped](#2-visible-hiding-what-051-shipped)
3. [More of the same kind](#3-more-of-the-same-kind)
4. [What "done" means for every interaction](#4-what-done-means-for-every-interaction)
5. [Your interactions](#5-your-interactions)
6. [When they land](#6-when-they-land)
7. [The four you named (21 November)](#7-the-four-you-named-21-november)

---

## 1. What an interaction is here

**An interaction is something you do that Karen notices and answers,** through one of her senses
([Karen.md §3](../../../agents/karen/Karen.md#3-perception-the-sensorium)): sight, hearing,
traces, the customers' testimony, the building's sensors and cameras, or a place found empty.
Visible hiding is one: she *sees* you go into a hiding place, so she comes for you.

Every interaction answers the same six things:

| | Visible hiding, as the example |
|---|---|
| **You do** | go into a locker while she can see you |
| **Her sense** | her eye: she saw you beside the locker a moment before you vanished |
| **She does** | runs to the nearest point she can reach, and catches you within 3 m if you're still in there |
| **She never learns** | where you are once you're hidden; whether you're still inside, until she's there |
| **Your answer** | hide where she can't see you go in; leave before she arrives |
| **The bots' answer** | the test players hide only out of her sight, so training sees the same choice a person makes |

**The fairness contract still rules** ([Karen.md §9](../../../agents/karen/Karen.md#9-the-fairness-contract)):
her body and brain never read where you are (rule 1, checked by the source scan
`KarenTests.BodyBeliefAndDecisions_NeverReadThePlayer`), and no interaction may make a shift
unwinnable (rule 6) or ratchet without decay (rule 7).

---

## 2. Visible hiding: what 0.5.1 shipped

Merged in 0.5.1. Her eye remembers the last visible position beside the entrance. Leaving or changing hiding places cancels the catch, even if the player returns unseen. [PR #89](https://github.com/goraxyy/Kehai/pull/89)

- **Hiding places are empty spaces set in code,** not the furniture's outline: the cold hiding
  cabinets, the staff lockers, the WC's empty cupboard, the toilet and changing cabins (standing),
  and under the stockroom's racks (crouched). You count as hidden when most of your body is inside.
- **Her eye remembers a witnessed entry** when it saw you right beside that place a moment before
  you were hidden. A sighting a metre away behind a wall isn't one.
- **She runs to the closest point she can reach** to where her eye last saw you, and the 3 m is
  measured to that remembered point, never to where you are.
- **"Still in there?" is a yes or no** from her eye's own check. It can take a catch away, never
  start one. If you slipped out, she looks for a moment, finds nothing and goes back to deciding.
- **The memory expires** after a while, and a second entry counts as new.
- **Hit stun in the build branch** also suppresses sight and hearing. The eye can still cancel a witnessed catch when the player leaves.

Q12 chose option A.


| Option | How | Pros | Cons |
|---|---|---|---|
| **A. 0.5.1 finishes and ships it; 0.5.2 extends it (your answer)** | 0.5.2 starts from merged 0.5.1 and adds §3's interactions on top | no two sessions on the same files; it's tested before anything builds on it | 0.5.2 waits for 0.5.1's merge |
| B. 0.5.2 takes it over | 0.5.1 drops item 2; 0.5.2 builds it | one place for all hiding work | throws away work in progress; 0.5.1's other items (her sight, pursuit, stun) touch the same brain code anyway |
| C. Both build parts at once | split by file | quicker on paper | the same brain and sight files: conflicts, and two fairness reviews of one feature |

**What 0.5.2 adds on top of it:**
- the same memory filled by her cameras (I2) and by a customer (I3), with less certainty;
- her search when a catch fails: which nearby places she checks next, in an order that doesn't
  depend on where you are;
- the test players taught to hide out of her sight, and a row in the trick check that forces a
  seen and an unseen hide.

---

## 3. More of the same kind

Each is something she notices **through a sense she already has.** The costs are *estimates* of
agent work, before tests in Unity.

| # | You do | Her sense | She does | She never learns | Your answer | Cost |
|---|---|---|---|---|---|---|
| **I1** | go into a hiding place in her view | sight | comes for you; catch within 3 m (0.5.1) | where you are inside | hide unseen | in 0.5.1 |
| **I2** | go into a hiding place in a **store camera's** view **while she's watching the screens** | the building: the six store cameras (0.4.1) see only while she stands at the office screens ([Karen.md §19.2](../../../agents/karen/Karen.md#192-the-stores-cameras)), and her bolt-on camera reports the same way | the same as I1, but she has to walk there from the office, so it's slower | the same | hide where no camera looks, or while she's away from the screens | small: it reuses I1's memory |
| **I3** | go into a hiding place while a **customer** watches | testimony: a customer remembers seeing you (`CustomerMemory`), and she has to walk up and ask them | goes to the place the customer names; less sure, and the memory fades | the same, and whether the customer saw right | hide when no one's looking; she loses time finding the customer first | small to medium |
| **I4** | **shut a locker or cupboard door** hard | hearing: a door noise, carried along the aisles, not through walls | goes to the sound and checks the hiding places near it, nearest first | which one you're in | close it slowly (holding E longer is quiet) | medium: a new noise and a search order |
| **I5** | hide **out of breath** right after a sprint | hearing: you breathe hard for a few seconds, heard only within about 2–3 m | if she passes close, she stops and checks that place | anything from further away | walk the last stretch, or wait before hiding | medium: needs your stamina, a quiet sound, a test of the distance |
| **I6** | **come out** of a hiding place in her view | sight | your place joins her Ledger's "where you hide", so she checks it earlier later in the career; it fades if you stop using it (rule 7) | nothing new | vary your spots | small. **Note:** today the Ledger learns where you hide from the Director, which is allowed to know where you really are (`KarenLedger.ObservePlayer`). **Your answer (Q13):** keep every source, and let what she saw herself count for more |
| **I7** | hide with your **torch on** | sight: she sees light leaking from a cupboard or under a rack, not you | goes to check the glow | that it's you, until she's there | switch it off before hiding | small to medium: the torch already raises your light level, but hiding places ignore it today |

**Your picks (21 November, Q13):** **I2, I3 and I7** are in 0.5.2. **I6 is in as a weight:**
her Ledger keeps learning where you hide from all its sources (the Director included, as today),
and what she saw herself counts for more. **I4 and I5 wait** for the sound designer's work (the
[sound plan](https://github.com/goraxyy/Kehai/pull/86)), since they need new sounds to feel right.

**Not suggested:** anything that tells her where you are without a sense, such as "she knows
you're in the stockroom because you always are". Habits already work through the Ledger, with decay.

---

## 4. What "done" means for every interaction

Each interaction is one task, done when all of these hold:

1. **Her sense, written down:** which sense tells her, and what it may carry (a place, a sound's
   spot, a customer's word), in [Karen.md](../../../agents/karen/Karen.md).
2. **Fair by test:** the source scan still passes, and any new file joins it; a test shows she
   never acts on what her sense didn't carry (for I2: a camera that can't see the place gives her nothing).
3. **The bots' answer:** the test players and the Employee's options know the counter (hide
   unseen, close quietly, wait before hiding, torch off), so training meets the same choice.
4. **A trick-check row:** the check forces the interaction once seen and once unseen, and both
   end as the rule says, with 0 fairness violations. The row is written during the build and run
   in the recorded test pass after 0.5.2 merges (your answer to Q1).
5. **Today's shift unchanged where it doesn't apply:** with no hiding in it, a shift plays as
   on `main` (the same seeds, jobs and end).

---

## 5. Your interactions

Add a row whenever you like: one line is enough. The next build fills in the rest and asks if
anything's unclear.

| # | You do | Her sense (if you know) | She does | Your answer | Notes |
|---|---|---|---|---|---|
| I8 | **Fridge check:** hide in a fridge | sight, by looking in | checks the fridge; if you're inside, you lose at once | stay out of the fridges she has reason to check | added 21 November; [§7](#7-the-four-you-named-21-november) |
| I9 | **Kneel check:** crouch under a rack | sight, by kneeling to look | kneels at the rack; if you're under it within reach, you lose at once | pick a rack she has no reason to check | added 21 November; [§7](#7-the-four-you-named-21-november) |
| I10 | **Hit stun:** hit her with a thrown item | touch | freezes, and stops seeing and hearing, for a time set by how hard it hit | aim, and run | added 21 November; extends 0.5.1's stun; [§7](#7-the-four-you-named-21-november) |
| I11 | | | | | |
| I12 | | | | | |

---

## 6. When they land

**Before any testing or training run** ([TESTING.md](../../TESTING.md), Stage 1 on). Each
interaction changes what she can learn about you and what the bots learn to do, so a run before
them would have to be done again. This fits your rule: build everything first, then test and
train once. On 21 November you set the same rule for 0.5.0's and 0.5.1's played tests: they
wait for 0.5.2, and every test shift is recorded (Q1).

---

## 7. The four you named (21 November)

You listed four interactions and asked for names and more suggestions. Short names, then what
0.5.1 already built of each (read in merged `main` on 21 November), and what 0.5.2 adds.

| Name | Your words | Built in 0.5.1 | What 0.5.2 adds |
|---|---|---|---|
| **Seen hiding** (I1) | "seeing the process of the player hiding" | yes: her eye remembers seeing you go in; she runs to the closest point she can reach and catches you within 3 m of where she last saw you, if you're still in | the sandbox's "saw you go in" light; her search when the catch fails; the bots hiding out of sight (C9.1) |
| **Fridge check** (I8) | "checking if fridge has someone inside; once she checks and succeeds, instant lose" | no: she has no search action; hiding places conceal you whatever she does | a new action: she walks to a fridge, opens it and looks (about a second); if you're inside, you lose at once |
| **Kneel check** (I9) | "'kneeling' to check if player is under a rack, if yes and within a reachable distance, instant lose" | no | a new action: she kneels at a rack and looks under it; if you're under it within reach (Q24), you lose at once |
| **Hit stun** (I10) | "her being hit with objects freezes her and her perception for some time based on the 'velocity' of the hit" | **half:** a thrown item stuns her for 0.25–3 s, set by its speed and weight; while stunned she can't move, plan, catch or dash. **But her eyes and ears keep working:** nothing in her senses checks the stun | she stops seeing and hearing for the stun's length too, and the sandbox shows the stun timer |

**What keeps the two checks fair:**
- **She picks which place to check only from what she sensed:** a seen entry, a sound from it, a
  customer's word, or her Ledger's habits for you (with what she saw counting for more, Q13), never
  from where you really are. The source scan covers the new code.
- **The check itself is the sense:** she has to walk there and look, and you can see her do it (the
  door opens, she kneels). This is a deliberate step past 0.5.1's rule that her "still in there?"
  answer can only take a catch away: a check she physically makes may start one.
- **It's never certain she'll check:** a place she has no reason to suspect stays safe.

**More of the same kind (my suggestions, Q26):**

| Name | You do | Her sense | She does | Your answer |
|---|---|---|---|---|
| **Locker check** | hide in a locker, a cupboard, a toilet cabin or a changing cabin | sight, by opening it | the fridge check, for every closed hiding place | the same as the fridge |
| **Decoy throw** | throw an item away from you | hearing: it lands with a noise | goes to where it landed and looks | lure her away, then move |
| **Swinging door** | go through a door just before she looks | sight: a door still moving is a trace that someone just passed | heads through that door | close doors gently, or wait |

The earlier picks stay: the camera (I2), the customer (I3) and the torch (I7), and what she saw
counting for more (I6). Each new one is tested in the [interactions sandbox](sandboxes.md#4-the-four-sandboxes).
