# Staff room, office and model library

Built 14 October 2026. These are actual scene furnishings and reusable local Unity assets.
The rest of the measured environment plan remains a proposal.

## Rooms and clearances

| Room | World bounds, metres | Gross size |
|---|---|---|
| Staff | x10–25, z−150 to −136⅔ | 15 × 13⅓ m / 200 m² |
| Office | x10–25, z−136⅔ to −123⅓ | 15 × 13⅓ m / 200 m² |
| Future WC shell | x10–25, z−123⅓ to −110 | 15 × 13⅓ m / 200 m² |

The two furnished rooms have a 3.65 m clear ceiling height. The outer building walls continue
to the original roof. The west lining's inner face is x10.15 and the east face is x24.925.
Cabinet, locker, kitchen and sofa backs meet their assigned wall faces. Model geometry stays
at its authored metre scale; it is not shrunk to fit the room.

The player is 1 m wide and 2 m tall. Door leaves are 1.5 m wide, with working hinges and solid
colliders. The staff room's ten lockers are **1.2 × 1.1 × 2.4 m** each. Their open doors leave
about **1.7 m** before the changing cabins. The main east route stays clear. Small gaps under
chairs and between attached cabinet parts are not intended walking routes.

## Staff room

- Ten individually numbered lockers: hollow interiors, shoe and upper shelves, coat rail,
  hooks, vents, name plate and handles. Aim at a door and press **E** to open or close it.
- Two 2.6 × 2.7 m changing cabins with 1.5 m inward-opening doors, benches, mirrors and hooks.
- Kitchen: fridge, counter, sink, soap, microwave, kettle, wall shelf, coffee machine and radio.
- 4.8 × 1.6 m dining table, eight chairs, cups and a plant; uniform-folding table and folded shirts.
- Sofa, lounge chair, coffee table, wall screen, whiteboard, noticeboard and clock.
- First aid, extinguisher, coat hooks, recycling station, plants and the existing supply shelf.
- Stock_def remains a functional test crate with its snap point. The mop and its home remain.
  CoffeeMachine, MusicBox and Puncher retain their original gameplay components and references.

Staff doors connect to the sales floor at (25, −142.5), office at (23.5, −136⅔), and backstreet
at (17.5, −150). The old partition at z−130.25 is retained inactive for recovery.

## Office

- Karen's L-shaped desk, task chair, two visitor chairs, monitor, keyboard, lamp and document tray.
- Security workstation with six modeled screens and two task chairs.
- Open 4.8 × 1.5 m briefing table, eight chairs, laptop, papers and a plant; no meeting cabin.
- Three filing cabinets, two tall archive cabinets, safe, printer station and document counter.
- Whiteboard, noticeboard, clock, wall screen, sofa, coffee table, water dispenser, recycling,
  first aid, coat hooks, camera housing and plants.

The office entrance is at (25, −130). The WC entrance moves to (25, −116.7) so its old opening
does not straddle the new office/WC partition. WC furnishings are still future work.
The CCTV screens, laptop, printer and safe are models; live feeds and office tasks are not
implemented. Karen's gameplay spawn remains unchanged.

## Models_Island

The floor now covers **x0–120, z0–120**. The island root stays at its original transform because
its existing NavMeshSurface volume covers the supermarket. Moving that root would move the
store's navigation volume.

| Section | Approximate plot |
|---|---|
| Staff furniture: 32 models plus an open-locker display | x4–40, z4–40 |
| Existing shop fixtures | x44–80, z4–40 |
| Products, by category | x84–116, z4–43 |
| Restocking boxes, by category | x84–116, z51–104 |
| Office: 19 additional models | x4–40, z48–78 |
| Reserved WC and lobby | x44–80, z48–80 |
| Reserved exterior, parking and service fixtures | x4–80, z88–114 |

Four-metre main aisles separate the plots. Staff and office displays use 6 m cells and labels
showing name and width × depth × height. Product rows are 3 m apart; box rows are 4.3 m apart.
The small scale ruler marks one-metre intervals and a two-metre height gauge.
Display copies have no gameplay components, colliders or rigidbodies. Their floor remains solid.
Rebuilding the old product/box showcases reapplies this layout instead of growing another
unorganized strip beyond it.

## Building, editing and saving

1. Open `Assets/!_Project/_Core/Scenes/SampleScene.unity`, in edit mode.
2. Use **Kehai → Staff Room → 2. Build Staff Room and Model Island**.
3. Use **Kehai → Office → 2. Build Office and Update Model Island**.
4. Save the scene with **Cmd+S**. Save changed prefab assets separately if Unity prompts.
5. To edit an individual model, open its prefab under
   `Assets/!_Project/_Art/Generated/StaffRoom/Prefabs` or `.../Office/Prefabs`.

Build commands replace their own furnished-room roots and update their generated assets.
Manual changes inside those generated roots/prefabs will be replaced by a rebuild; duplicate a
model to a separate art folder before making a variant. Scene backups are kept in
`Library/Kehai/SceneBackups`; earlier navigation assets are retained for recovery.

The repository remains code and documents only. Generated meshes, materials, prefabs, scene
and navigation assets are saved locally. The editor builders reproduce them from source.

## Engineering decisions and resolved problems

- **Simple meshes, few renderers:** beveled boxes, ten-sided cylinders and faceted leaves are
  combined by material into one mesh per stationary model and one per door. Repeated furniture
  shares mesh and material assets. Handles and vents do not each need a renderer.
- **Solid, independently aimed doors:** locker/cabin doors use the existing E/hover system.
  Their motion advances in small angular steps and stops at an obstruction. Room doors retain
  HingeDoor, so Karen, door events, locking and replays still recognize them. Authored room
  doors have fixed inward swings; existing doors keep their previous behavior.
- **Navigation includes furniture, excludes moving doors:** the bake uses physical colliders.
  The player and door leaves are excluded; open doors carve their current positions at runtime.
- **Ceiling caused a broken map:** the new ceiling initially created an elevated navigation
  island. Sampling halfway up the full navigation bounds then missed most ground-floor cells.
  Ceiling geometry is excluded from baking and StoreMap samples from the ground level instead.
- **Three west rooms need another light layer:** the builder names an additional rendering
  layer; room lighting can allocate it to a third separate interior. Authoring refreshes use
  the rebuilt room map, and replace previous generated floor/ceiling lighting patches. Floors,
  lowered ceilings and furniture use an anchor inside their room: applying a wall's two-sided
  lighting rule to these objects had assigned neighboring rooms' light to them.
- **Keep existing gameplay references:** coffee, radio, punch clock and tools are dressed or
  moved in place rather than replaced by unrelated objects.
- **Island growth stays organized:** fixed category plots, true-scale bounds and reserve space
  prevent the next room's models from spilling into the products or the supermarket approach.

Compiled with the project's Unity C# compiler and inspected in the editor. No Play-mode test
or automated test suite was run for this modeling change.
The final rebuilt navigation map contains 2,776 ground-floor cells across eight rooms.
