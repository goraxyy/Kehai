# Step: pick the week's shorts

You get the week's shifts and their clip moments (best first), and a brief: how many shorts to
pick, the moments marked kept, and moments already used in earlier weeks.

Pick the moments that make the best 15–40 second vertical shorts, and say how to film each one.

**Choosing**
- One clear idea per short, readable without sound in two seconds: a catch, a near miss, a trick
  and its warning, her undoing your work, overtime, a possessed customer, a blackout. Prefer
  moments whose events tell a story with a turn (calm → tell → trick → consequence).
- Spread the week across different hooks and tactics; don't pick three chases.
- Score is a guide, not the rule. A low-scoring moment with one perfect beat beats a busy one.
- Skip moments already used in earlier weeks unless nothing else is usable.
- Never pick a moment whose only interest is `aiko_stuck` or `manual_bug`: those are bugs.

**Kept moments** were marked by the developer (F7 or Shift+F7). Every one gets an entry in `kept`:
`short` if you picked it, `long` to save it for the monthly video, `bug` for a Shift+F7 bug report
(`manual_bug` in its events), `skip` if it can't be used, with the reason. Lean towards using
F7-good moments.

**Shots for each pick** (2 to 4): the shots the editor will cut between. Typical sets:
- the story from above: `topdown` with `cone` and `sound` (or `belief` and `guess` when her
  reasoning is the point);
- the feeling: `pov` (yours), or `chase` behind her;
- the reveal: `cctv` on her;
- her mind alone for a picture in picture: `topdown`, `alpha: true`, layers `belief`, `guess`,
  `actors`.
Offsets widen or narrow the moment: start a few seconds earlier to show the calm before
(`start_offset` −3), end later to land the consequence. Keep each shot 6 to 30 seconds.

Write `angle` as the one idea, `hook_idea` as the opening line, `why` in a sentence.

**Patterns** are recipes learnt from videos the developer sent as references (`patterns`: what
each needs, its idea, the shots it uses). `must_use` lists the ones this week must use: give each
to one short whose moment suits it (its `moments` and `tags`), and film that short the way the
pattern needs (its `shots`: the same cameras and layers where the moment allows). If no moment this
week can carry it, say so in `pattern_fit` (`fits` false) and write `play`: the moment the developer
should play for it, in one sentence to them ("a shift where she corners you in the stockroom and
you get away"). Any other short may follow a library pattern when it truly suits the moment; most
won't (`pattern` ""). Don't give two shorts the same pattern in one week.
