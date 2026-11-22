# Where the usable things go

Part of the [Kehai 0.5.2 plan](../0.5.2-plan.md), focus item 5 (21 November): "make sense of which
interactable items go where: camera, PA, clock, etc; their position should make sense, and should be
reachable by the player (maximum on eye level and not above), buttons should make sense, like power
box having 3 buttons that idk what each does." The tasks are the plan's
[Phase 12](../0.5.2-plan.md#phase-12-where-the-usable-things-go-c-with-you--sonnet-55--this-mac).

## 1. The rule

- **Within reach:** every usable thing is at **eye level or lower**. Your eyes are at 1.6 m
  (`PlayerMotor.standCameraY`), so a thing's centre sits between about 0.6 m and 1.6 m.
- **Reachable:** you can stand right in front of it; no shelf or table is in the way.
- **Where it makes sense:** a thing sits where a real store would put it (the time clock by the staff
  room door, the breaker box in the back).
- **Every button says what it does:** a label, or a prompt that names it.

## 2. Every usable thing, from the code (21 November)

| Thing | Where | Height | What it does | To check or fix |
|---|---|---|---|---|
| **Power box** (`ElectricBox`, `BreakerPanel`) | the backstreet | measured by C12.1 | **three switches, one per light circuit: East, West and Back.** Hold E for 20 s and every circuit and the mains come back; the switches light up one by one to show how far the hold has got. A tap on a lit switch turns that circuit off | **nothing says which switch is which,** and the tap-off is hidden: Q25 |
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
