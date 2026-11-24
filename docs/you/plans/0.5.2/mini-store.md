# The mini store sandbox (your call, 24 November)

You tried the island and called it horrible: grey boxes, a walk across the car park, a fake Karen
in each room, buttons that didn't work and labels you couldn't read. This replaces it.

## What you get

**One sandbox, Kehai → Sandbox → Mini store.** A small copy of the store with three rooms, made
from the store's own models, with the real Karen in it.

```
  STOCKROOM          |  COLD ROOM
  racks and boxes,   |  chillers you can hide in,
  breaker box, PA,   |  fog, the freezer alarm
  time clock, mop    |
  -------door--------+--------door-------
  SALES FLOOR: tall and short shelves, a till, bins,
  two cameras and the screens, the shoppers' auto doors
  about 30 × 25 m. You start here.
```

- **Real models:** copies of the store's own tall and short shelves, racks, chillers, doors and
  lamps, made when Play starts. Nothing is saved to the scene.
- **The real Karen:** her real brain, body and senses, on a private shift (your save is never
  touched). A catch freezes everything and shows CAUGHT instead of ending the shift.
- **You start inside,** by the till. No walk.

## The controls

A panel on screen (Tab shows or hides it and frees the mouse). Plain keys, no F keys, so a MacBook
needs no Fn.

| Part | What you choose |
|---|---|
| **Karen** | Start · Freeze · Restart (a fresh shift, her memory cleared) |
| **Tricks she may use** | **None** (she only hunts) · **Only one** (pick it from the list; she uses it herself when it fits) · **All** |
| **Fire now** | Make her do the chosen trick at once, to see it without waiting |
| **What she can sense** (the "interactions") | Each on or off: her eyes and ears always; the cameras, the shoppers' reports, your torch's light, sounds you make |
| **Show** | Her cone and meters on or off |

The panel also says what she's doing and why she isn't using a trick right now (for example
"blackout refused: the lights are already out").

## Steps

Each step ends with screenshots I check myself and send you, a commit and a note.

- [ ] **M1 The building:** the three rooms from the store's models, doors that open with E, lamps.
  You start inside. Screenshots.
- [ ] **M2 Her map and the real Karen:** her map of the three rooms with their real names (sales
  floor, cold storage, stockroom); she patrols and hunts there.
- [ ] **M3 The panel:** Start, Freeze, Restart; tricks None / Only one / All, and Fire now; the
  senses switches.
- [ ] **M4 The tricks' things:** the breaker box and light circuits, cameras and screens, fog box,
  PA, time clock, mop rack, bins, till and shoppers, freezer alarm, stock boxes, in the three rooms.
- [ ] **M5 End-to-end checks:** a bot plays it the way you would: walks in, opens every door, uses
  the panel. Then, for each of the 24 tricks with only that one allowed, she uses it herself within
  a time limit, and Fire now works. Results in a table. What fails is reported as it is.
- [ ] **M6 Clean-up:** the old island rooms and trick stalls come out of the menu; the automatic
  sight and pursuit checks stay (the recorded test pass uses them). Docs and tests.

**Done when:** you open Kehai → Sandbox → Mini store, everything on this page works, and you say
yes or what to change.
