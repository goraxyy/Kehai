# Interactions

Part of the [Kehai 0.5.2 plan](../0.5.2-plan.md), item 2: "Some interactions like 'visible hiding'
in 0.5.2; I will add later." The questions (Q numbers) are in the plan's
[open questions](../0.5.2-plan.md#11-open-questions). There's a table for your own at the end:
[§5](#5-your-interactions).

1. [What an interaction is here](#1-what-an-interaction-is-here)
2. [Visible hiding: what 0.5.1 is building](#2-visible-hiding-what-051-is-building)
3. [More of the same kind](#3-more-of-the-same-kind)
4. [What "done" means for every interaction](#4-what-done-means-for-every-interaction)
5. [Your interactions](#5-your-interactions)
6. [When they land](#6-when-they-land)

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

## 2. Visible hiding: what 0.5.1 is building

**It's 0.5.1's item 2,** being built now by another session on local branch `claude/0-5-1-d01aca`
(the plan is [PR #87](https://github.com/goraxyy/Kehai/pull/87)). Its "done when", in short: if she
sees you go into a hiding place (crouching under stock shelving included), she gets as close as
she can and ends the game within 3 m of you while you're still hiding; hiding unseen gives you
away to nobody; leaving or changing places makes the old sighting useless.

**What the branch does, as read on 17 November** (uncommitted work, still changing; read only,
nothing touched):

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
- **The 16 November review found two fairness slips** in the first version: the 3 m was measured
  to your live position, and the memory sat outside the source scan. The 17 November working copy
  measures to the remembered point and keeps the memory in a file the scan covers. Its tests were
  still being fixed.

| Option | How | Pros | Cons |
|---|---|---|---|
| **A. 0.5.1 finishes and ships it; 0.5.2 extends it (my pick)** | 0.5.2 starts from merged 0.5.1 and adds §3's interactions on top | no two sessions on the same files; it's tested before anything builds on it | 0.5.2 waits for 0.5.1's merge |
| B. 0.5.2 takes it over | 0.5.1 drops item 2; 0.5.2 builds it | one place for all hiding work | throws away work in progress; 0.5.1's other items (her sight, pursuit, stun) touch the same brain code anyway |
| C. Both build parts at once | split by file | quicker on paper | the same brain and sight files: conflicts, and two fairness reviews of one feature |

**What 0.5.2 would add on top of it (with A):**
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
| **I6** | **come out** of a hiding place in her view | sight | your place joins her Ledger's "where you hide", so she checks it earlier later in the career; it fades if you stop using it (rule 7) | nothing new | vary your spots | small. **Note:** today the Ledger learns where you hide from the Director, which is allowed to know where you really are (`KarenLedger.ObservePlayer`). I6 could instead count only what she saw: fairer, but it changes what she learns (Q13) |
| **I7** | hide with your **torch on** | sight: she sees light leaking from a cupboard or under a rack, not you | goes to check the glow | that it's you, until she's there | switch it off before hiding | small to medium: the torch already raises your light level, but hiding places ignore it today |

**My picks:** I2, I3 and I7 first: small, clearly fair, and they make hiding a choice with a
cost. Then I6 (the Ledger question first), then I4 and I5, which need a new sound and the sound
designer's work (the [sound plan](https://github.com/goraxyy/Kehai/pull/86)) to feel right.

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
   end as the rule says, with 0 fairness violations.
5. **Today's shift unchanged where it doesn't apply:** with no hiding in it, a shift plays as
   on `main` (the same seeds, jobs and end).

---

## 5. Your interactions

Add a row whenever you like: one line is enough. The next build fills in the rest and asks if
anything's unclear.

| # | You do | Her sense (if you know) | She does | Your answer | Notes |
|---|---|---|---|---|---|
| I8 | | | | | |
| I9 | | | | | |
| I10 | | | | | |

---

## 6. When they land

**Before any testing or training run** ([TESTING.md](../../TESTING.md), Stage 1 on). Each
interaction changes what she can learn about you and what the bots learn to do, so a run before
them would have to be done again. This fits your rule: build everything first, then test and
train once.
