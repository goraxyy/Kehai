# Reference: the game, the footage, the editor

## The game

A first-person night shift in a convenience store, built in Unity. You clock in at the time
clock; customers come in, browse, make messes and queue at the till. You can't clock out until the
shelves are full, the spills are mopped and the rubbish is out.

- **Jobs:** restock (carry the stock crate to a shelf), mop (hold E with the mop), bag the bins and
  take the bag to the skip out back, serve at the till, walk lost customers to the shelf they
  asked for. The store is a 150 × 150 m maze: 189 shelf units, 13 sections, 80 products.
- **Burnout:** your energy drains over the shift and when you sprint; coffee restores it. At zero
  you can only walk.
- **The building:** mains power with breakers, 240 ceiling lights, a store radio, automatic
  doors, a flashlight, and procedural sound for everything {{karen}} does.
- **Being caught** isn't death: a written warning, thirty seconds of lecture, lost shift time.
  The real threat is the clock. **Overtime** is her signature move: she refuses your clock-out,
  adds minutes, and thanks you over the PA for your flexibility.

## {{karen}}

The store's management AI. She hunts you with only what she can see and hear.

- **Senses:** graded sight (distance, angle, light, movement); a noise bus where every action has a
  loudness (sprinting and dropping things are loud); customers who tell her they saw you; building
  sensors (doors, the till).
- **Belief:** a probability map of where you might be. It sharpens when she sees or hears you,
  spreads out over time, and clears where she looks and doesn't find you. Her **guess** is the
  centre of that map.
- **Decisions:** utility-scored goals and a planner over 35 tactics. **Learning:** a bandit picks
  the tricks that work on you; a persistent ledger remembers where you dwell and hide and the routes
  you take, across shifts. It fades if you change your habits.
- **Fairness, enforced by tests:** her body never reads your true position; every trick has a
  warning at least 0.8 s ahead (a flicker, a chime, a PA crackle, a ballast whine); she can never
  outrun a sprint.
- **Her gaze** is painted on the floor, cut short by shelves: blue on her rounds, orange when she's
  noticed something, red while she hunts or sees you.
- **Her tactics:** blackout (breakers trip after a flicker and a whine; you fetch the flashlight and
  reset them in the dark) · mirror-black (one light dies right above you) · fog (a sabotaged freezer
  floods an aisle) · camera bolt-on (a camera installed over your favourite hiding spot) · shelf
  sweep (a shelf you filled is emptied; the loose stock is a noisy carpet) · spill (the mop bucket
  kicked over at a chokepoint; walking through leaves a wet trail she can follow) · bin tamper ·
  task falsification (your task list lies, with a flicker and a tick as the tell) · tool theft (the
  mop hidden where you go last) · overtime · the PA (announces your position "for customer
  convenience", a cleanup decoy, fake footsteps, a countdown of your shift, your tasks read back
  slightly wrong) · phantom chime (the doors open for nobody) · silence (her footsteps stop) ·
  crate wall · door lock · shelf relocation (the maze changes; you can hear it) · funnel (every way
  out blocked but one) · mimicry (she possesses a customer: it stops shopping, walks one aisle over
  at your speed, and turns to face you) · the witness (a customer herded into your hiding aisle) ·
  the understudy (a polite "new hire" who follows you to learn the job) · stalk (seen at the end of
  an aisle, then gone) · ambush (waiting, silent, where you always pass) · the chase (rare) · the
  favour (when you're exhausted she brings you coffee and means it) · patrol, sweep, investigate,
  follow, withdraw.

## The blink channel

Opt-in webcam blink tracking (Apple Vision or MediaPipe), all on the computer, nothing recorded.
{{karen}} can move inside the ~300 ms of your blink. B blinks from the keyboard.

## Clip markers (what the game noticed in a shift)

`catch` she caught you · `escape` a chase you got away from · `near_miss` she passed within 3 m and
didn't see you · `found_blind` she found you without seeing you (from her belief alone) ·
`blink_move` she moved during your blink · `learned` she learned something about you · `blackout`
the power went (value: seconds) · `possessed` mimicry · `undone_work` she undid work you'd just done
(value: seconds between) · `pa_call` the PA spoke · `prop_trick` a planned trick (crate wall, fog,
door lock, camera, shelf relocation, mimicry) until it took effect · `clock_refused` overtime ·
`loud_mistake` you sprinted or dropped something, she heard it and closed in · `tell_then_trick` a
warning, then the trick · `shift_review` clock-out · `karen_stuck` she got stuck (a bug: don't use
for marketing) · `customer_chaos` a customer gave up at the till or got lost · `manual_good` the
developer pressed F7: worth a clip · `manual_bug` Shift+F7: a bug, not marketing.

A **moment** merges markers less than 8 s apart (at most 45 s), scored by their weights (times 1.5
with a chase or a catch); `kept` moments hold a manual marker. Each comes with the narrator's lines
(the game's own text log of what she did) and events timed from its start.

## Shots (rendered from the shift's 3D replay)

- **Cameras:** `pov` (your eyes, as played) · `cctv` (a high corner of the store that can see the
  subject, cutting between corners) · `chase` (behind and above the subject) · `orbit` (circling
  the subject) · `topdown` (from above, the roof cut away: the maze, both of you, the layers read
  best here).
- **Subject:** `karen` or `you`.
- **Her mind** (layers drawn into the shot): `belief` (her probability map, red to yellow) ·
  `guess` (a ring where she thinks you are, tighter the surer she is) · `cone` (her view, red
  while she sees you) · `sound` (rings for every noise: you blue, her red, others grey) ·
  `thoughts` (her last three thoughts as text) · `actors` (dots for where everyone is).
- **Alpha:** her mind alone on a transparent background, for a picture in picture over another
  shot ("what she knows" over "what happened").
- Every shot carries the game's own sound (footsteps, PA, doors, her procedural sounds).

## The editor (what a draft can use)

- **Scenes** play in order; a transition other than `cut` overlaps two scenes by 0.4 s, so the
  video is the sum of the scenes minus 0.4 s per transition, plus the end card.
- **A shot** can be trimmed, slowed or sped up (one rate, or a ramp through keys: slow motion on
  the catch), zoomed and panned (keys ease between framings: a punch-in on a face, a slow push).
  `game_volume` is the game's sound; it dips under the voice by itself.
- **Split:** two shots stacked (`column`) or side by side (`row`), each with a tag: "what I see" /
  "what she sees", expectation / reality.
- **Picture in picture:** a shot small in a corner (her mind with alpha sits on a dark glass panel).
- **Overlays:** `hook` (the big line at the top) · `label` (a pill of text) · `lower-third` (title
  and subtitle) · `arrow` and `circle` (they draw themselves; point at her, at you, at the guess) ·
  memes (`pov`, `top-bottom`, `nobody` ("Nobody:" then "{{karen}}: <her line>"), `caption-bar`,
  `expectation-reality` on a split) · `image`, `gif`, `lottie` from the asset library.
- **Sound:** sound effects at scene times; one music track (it ducks under the voice); the voice
  track is the script, spoken by text-to-speech, with word-timed captions (the spoken word lit
  crimson, {{karen}}'s words in her colour).
- **The end card** shows {{game}}, {{game_jp}}, the tagline, your call to action and the handles.
- **Geometry:** x and y run 0 to 1 from the top left. On 9:16 the platforms' buttons cover the
  edges: keep text and marks between x 0.07 and 0.93 and y 0.10 and 0.70. Captions sit around
  y 0.74 to 0.82, the hook at the top: keep labels and memes clear of both. On 16:9 keep 5% from
  the edges; the lower third and captions share the bottom fifth.
- **Where things are in a shot:** you don't see the frames. Each shot's `events` say what happens
  and when (seconds into the shot), and `where` says where {{karen}} and you are in its picture once
  a second: `[t, karen, you]`, each `[x, y]` or null when off screen. To point at someone, give an
  arrow or circle a `target` (`karen` or `you`) and the code places it from the shot's exact track
  at that moment, allowing for trim, speed and zoom; it only works on a single-shot scene, while
  that person is on screen. Keep marks short-lived (1 to 2 s).
- **Speaking pace:** the narrator says about 2.6 words a second, {{karen}} about 2.2. A line needs
  its words' time before the next line starts.
