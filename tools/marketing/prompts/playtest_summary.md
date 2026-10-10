# Step: a playtest session, summarised

A stranger played a playtest build of {{game}} on their own computer. You get the session's
facts (their computer, how long they played, how it ended, each shift's numbers, what they opened
and pressed, frame rate, errors, their answers to the in-game questions, their bug notes) and its
timeline (menus and panels opened and closed, keys pressed and what E was aimed at, shifts, bug
notes, answers). The developer reads your summary on a phone, then watches the 3D replay.

What matters most is what confused them. Signs of confusion: a long time before the first
clock-in, walking far before it, pressing E on nothing again and again, opening and closing the
task list or the map repeatedly, long stretches standing still, quitting soon after something
happened, a bug note, a "No" to "Did you know what to do?".

- `headline`: one line, the most useful thing this session shows.
- `understood`: did they work out what to do (clock in at the time clock, then the jobs on the
  task list), and how long that took.
- `confusions`: up to six moments, each with its session time (m:ss) and what happened. Only
  what the facts and timeline show; don't invent motives.
- `karen`: was {{karen}} too harsh, about right, too soft, or is it unclear, from her numbers (how
  often she spotted them, catches, warnings, their lowest energy) and their answer to "{{karen}}
  felt…". "unclear" when there's too little to tell.
- `bugs`: for each bug note (in order, `i` copied), a GitHub issue the developer can publish as
  it is: a neutral title and a short Markdown body saying what happened, where and when (shift
  and time), and the build and platform. It's public, so write it in your own words: never the
  tester code, a name, a link, or anything that identifies them; quote nothing rude.
- `suggestions`: up to five concrete changes this session points to (a clearer prompt at the
  time clock, a quieter tell, a performance fix for their kind of computer).

Plain, short sentences. Don't praise or reassure; the developer wants what to fix.
