# Shelf remodel: low and tall pieces that actually join

Proposed for [0.5.0](../0.5.0-plan.md), 14 November. Plan only. The owner asked us to work out
construction from the scene rather than ask them to explain the kit.

## What I inspected

Read the saved `SampleScene.unity`, its Ready/Models shelf prefabs, and
`Level/Editor/ShelfPrefabBuilder.cs` directly. The Unity bridge reported no connected editor;
these are file measurements, not a live viewport inspection or a complete world-space collision
survey. Prefab root offsets must be resolved before calling any nearby pieces a verified joint.

| Evidence | What it shows |
|---|---|
| `ShelfTwoside_3_grey` | boards 4 m long, 1 m deep, 0.02 m thick; backing 4 × 2 × 0.04 m |
| `ShelfTwoside_short_2_grey` | same board footprint; backing 1.25 m tall |
| Tall board centres | 0.225, 0.816667, 1.408333 m |
| Low board centres | 0.225, 0.7375 m |
| L pillar price strips | follow tall board levels; the connector geometry has large local X/Z offsets around 2–3 m |
| Short I pillar strips | follow low levels; also offset from the prefab origin |
| Short b pillar strips | includes strips at both 0.675 and 0.7375 m; inspect whether the lower strip belongs to the structural plinth before removing anything |
| Scene examples | low double-sided shelves near (40, −142.5), (35, −142.5), (30, −142.5), repeated at z ≈ −147.5; low connector and tall run references occur nearby |

**Definite defect:** `Restructure` chooses separate board spacing for tall and low pieces.
Their second boards differ by **79.2 mm**. A height change therefore leaves an obvious step
unless each side is deliberately finished. Matching a pillar's footprint cannot fix that.

**Further risks, not yet proven visible defects:** offsets make origin snapping unreliable;
connector letters alone don't describe height on each side; a tall connector can cover the top
of a low run or leave exposed geometry; the b piece's price strips need an individual audit.

## Recommendation: shared lower frame, upper extension, explicit finished joints

A **connection point** is a named position, direction, width and height on a model. Kehai's
placer joins those points instead of guessing from origins; like LEGO studs defining where two
pieces attach.

1. Keep the measured 4 m run length and about 1 m double-sided depth for compatibility. Measure
   the one-sided and connector pieces independently; don't force them to be the same width.
2. Centre every new module's origin on its floor footprint. Put connection points at the actual
   ends. Rotating or mirroring a module must preserve the same join dimensions.
3. Use a shared lower structure up to 1.25 m. Proposed board centres: **0.225 and 0.7375 m for
   both heights**, with a third board at **1.408333 m on tall units**. This changes tall board
   spacing and stock capacity: regenerate slots and check product clearance before accepting it.
4. Add a 0.75 m backing/upright extension to make the tall version 2 m. Don't stretch the entire
   low mesh to make the tall one: that also stretches its boards, rails and texture scale.
5. At a tall-to-low straight join, keep the lower frame continuous. End the upper backing with
   a deliberate vertical edge and cap. No floating upper shelf projecting over a low run.
6. For L, T and cross joints, give each outgoing arm its own low/tall setting. The lower corner
   is shared; upper backing exists only on tall arms. Don't use one tall block for every joint.
7. One-sided/two-sided transitions get their own depth adapter and finished rear panel. An
   adapter bridges a dimensional change: Kehai uses one where a wall shelf joins a gondola,
   like a kitchen filler panel closing a gap between cabinets.
8. Add front-facing price strips and usable stock faces only where shoppers can actually stand.
   Remove hidden duplicate stock surfaces at joints; don't populate shelves facing a backing.
9. Plain painted steel, capped ends and a separate price rail; materials are wanted already.
   Models, materials, scenes and reference renders stay in private `kehai-assets`.

| Alternative | Benefit | Cost / reason against |
|---|---|---|
| Keep each height separate, insert a capped gap | quickest safe repair | no continuous lower shelves; visibly separate furniture |
| **Shared lower frame + upper extension** | clean joins with a small reusable kit | remesh joints and regenerate tall slots |
| Model every possible full L/T/cross height combination | precise individual control | too many models to maintain |

**Fallback:** until a mixed-height joint passes inspection, end-cap both runs and leave a clear
intentional gap. Never hide an invalid join by overlapping meshes.

## Fold into existing tasks C4.1–C4.3 and C8.1 (no game code in this PR)

- [ ] **[C] Audit the kit at full transforms:** resolve scene instance → Ready → Model offsets;
  produce dimensioned top/front views for straight, L, T, cross, one/two-sided and b/S/R pieces.
  - Done when: join coordinates, board heights, thicknesses and stock faces are recorded, and a
    viewport screenshot verifies one low-to-tall example in the actual scene.
- [ ] **[C] Prototype the proposed shared frame** privately: low-low, tall-tall, low-tall,
  mixed-height corner and T, and one-to-two-sided adapter.
  - Done when: no gap, doubled upright, protruding board, overlapping stock or broken price strip
    is visible from either walking side; crouched and standing sight agree with geometry.
- [ ] **[C] Validate physical placement**, then bake navigation and rebuild Karen's map.
  - Done when: actual 1 m shelf depth drives collision and clear aisle widths; the six retained
    layouts pass traversal, stocking and trick checks. A 2.5 m drawing square is never a collider.
- [ ] **[You] Review the prototype contact sheet** before the whole kit is rebuilt.
  - Done when: the chosen join treatment is approved. This later art review need not block other
    0.5.0 code once the design is approved.

Opus for the join design and map implications; Sonnet for the agreed placer and model-export
checks. Mac for small prototypes, Windows for repeated baking and full-layout checks.
