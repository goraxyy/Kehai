# Where the usable things go

Part of the [Kehai 0.5.2 plan](../0.5.2-plan.md), focus item 5 (21 November): "make sense of which
interactable items go where: camera, PA, clock, etc; their position should make sense, and should be
reachable by the player (maximum on eye level and not above), buttons should make sense, like power
box having 3 buttons that idk what each does." The tasks are the plan's
[Phase 12](../0.5.2-plan.md#phase-12-where-the-usable-things-go-c-with-you--sonnet-55--this-mac).

## 1. The rule

- **Within reach:** every usable thing is at **eye level or lower**. Your eyes are at 1.6 m
  (`PlayerMotor.standCameraY`), so a thing's centre sits between about 0.6 m and 1.6 m. **A shelf
  is measured at its board** (your call, 23 November): a tall pack on a 1.5 m board is in reach.
- **Reachable:** you can stand right in front of it; no shelf or table is in the way.
- **Where it makes sense:** a thing sits where a real store would put it (the time clock by the staff
  room door, the breaker box in the back).
- **Every button says what it does:** a label, or a prompt that names it.

## 2. Every usable thing, from the code (21 November)

| Thing | Where | Height | What it does | To check or fix |
|---|---|---|---|---|
| **Power box** (`ElectricBox`, `BreakerPanel`) | the backstreet | measured by C12.1 | **three switches, one per light circuit: East, West and Back.** Hold E for 20 s and every circuit and the mains come back; the switches light up one by one to show how far the hold has got. A tap on a lit switch turns that circuit off | **Labelled (Q25, 23 November):** EAST, WEST and BACK printed above the switches, and the prompt names the lights, e.g. "WEST: the sales floor's west side. Throw its lights off" |
| Fog boxes (`FogBox`) | wall boxes beside the fog rooms | 1.45 m (`KarenFixtures.BoxHeight`) | hold E to clear the fog | — |
| Camera box (`CameraBox`) | a wall box | 1.45 m | hold E to repair the cameras after her Camera break trick | — |
| The six store cameras (`StoreCameras`) | 3 m up on walls | 3 m | they watch; you never touch them | fine: not something you use |
| The office screens (`CctvWall`) | the office | standing screens | show the six cameras | — |
| PA microphone (`ClosingMic`) | on its own small table | table top 0.75 m | hold E to announce closing, or to end the flash sale | — |
| PA box (`PaBox`) | the staff room wall by the time clock | measured by C12.1 | a lever: the store's PA off and on | check it isn't above 1.6 m |
| Time clock (`Puncher`) | the staff room, by the door | about 1.2 m | hold E to clock in and out | — |
| Room light switches (`RoomLightSwitch`) | by room doors | measured by C12.1 | a room's lights | check height and labels |
| Radio (`MusicBox`) | measured by C12.1 | measured by C12.1 | the store's music on and off | check where it is |
| Coffee machine (`CoffeeMachine`) | the staff room | about 1.0 m | energy | — |
| Bins, tool spots, shelves, dirt, furniture doors | across the store | various | chores and hiding | the shelves' top boards may be above 1.6 m: check that nothing *needed* sits there |

## 3. The audit

1. **A check in code** lists every usable thing in the scene with its height above the floor in
   front of it, whether a standing spot in front of it is reachable on the NavMesh, and its prompt.
   It flags anything above 1.6 m, unreachable, or with no prompt.
2. **Each flag is fixed** in the authoring code that places the thing (the rules keep placements in
   C#), and the check is run again until nothing is flagged.
3. **The power box's switches** get what Q25 decides.
4. **The same check runs on every layout,** since six layouts move the shelves.

**Example:** the check reports "Radio: centre 2.1 m, above eye level". The radio's placement in
code moves it to 1.4 m on the same wall, and the next run reports nothing.

## 4. Where it stands (26 November)

**The audit flags nothing** in the store or any of the six layouts (171,407 rows, run
`20261126-015305-f4e19a`); it was 110 on 23 November, 29,244 before that. Every product keeps a
slot that isn't display, in every layout. The display check is cautious: of the store's 842
display slots the audit can reach 260, and 22 to 61 more a generated layout (they just stay
stocked).

| What changed | Why | Your call |
|---|---|---|
| Shelves measured at the board | a tall pack on a 1.5 m board is in reach; ~2,400 store slots were flagged for the pack's middle | yes, 23 November |
| Blocked faces are **display only** | ~950 store slots face a wall, a chiller or another bay: they stay stocked, and shoppers, her knock-offs, her bolt-on camera and old sabotage never empty them, so no task asks for them | yes, 23 November |
| A slot restocked through its bay counts | a product's box fills every empty slot of it on the bay, the crate the whole bay, so a slot behind a reachable one is restocked from it | how the game already works |
| The audit opens each chiller slot's own door, and a cupboard in the way | it opened the first door only, which swung across its neighbours; the WC mop sits in a cupboard | — |
| Stock boxes behind scenery aren't used | three working boxes stood behind a wall panel or a prop; their products moved to clear spots | — |
| The power box labelled | EAST, WEST, BACK, and the prompt names the lights | Q25, yes |
| **26 Nov:** a slot directly behind a display one is display too | you'd reach it past the one in front; the check had stood the deeper slot's body inside its own bay (the small end caps, 8 to 12 slots a layout) | — |
| **26 Nov:** a chiller whose every slot is display keeps its doors shut | nobody can stand at freezer 5's door, and nothing behind it is needed; Karen doesn't check it for you either | — |
| **26 Nov:** the till counter's mints face a side with room, else display | in today's layout the facing stands among a shelf bay's decks, with no room on either side, so there it's display (mints stay buyable on their shelves); in the other layouts it faces the shoppers | — |
| **26 Nov:** the staff room's mop and rack on the east wall | they stood in the south-west corner, boxed in by the counter, the stock crate and the walls; now where the mini store has them | — |
| **26 Nov:** things off her map checked with physics | the parking lot's six bins are off her NavMesh: the audit checks the floor, the room to stand and the aim there, but can't check a route | — |

**For your walk (Y12.1):**
- **The parking lot's bins:** walk out to them once; the audit couldn't check the way there.
- **The staff room's mop** on the east wall: say if it's in a bad spot.
- **Freezer 4 and 5** are shut displays; **the till counter's mints** are display in today's layout.
