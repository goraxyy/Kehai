# The visual style

Part of the [Kehai 0.5.0 plan](../0.5.0-plan.md), item 8. A quick pass, like everything before
the redesign: the choice made here is a direction for the style guide of
[Milestone 7](../../RELEASE_PLAN.md#milestone-7-redesign-before-launch), not the final art.

1. [Six styles](#1-six-styles)
2. [Light, shadow and filters, style by style](#2-light-shadow-and-filters-style-by-style)
3. [How you'll see them and play with them](#3-how-youll-see-them-and-play-with-them)
4. [What makes a game look good and still run fast](#4-what-makes-a-game-look-good-and-still-run-fast)
5. [The island: a scene, a project or a branch](#5-the-island-a-scene-a-project-or-a-branch)
6. [Sources](#6-sources)

---

## 1. Six styles

Named the way the games' makers or reviewers name them. Where a claim isn't backed by a source
I found, it says so.

| # | Style (its proper name) | The game | What it is | Fits Kehai? |
|---|---|---|---|---|
| S1 | **Stylised cartoon** ("Pixar-style visuals", its publisher's words) | *Hello Neighbor* (Dynamic Pixels, tinyBuild; Unreal Engine 4) | bright, chunky, exaggerated shapes; the horror sits under a friendly surface. It began more realistic and became more cartoony; its lead designer put it "between Tim Burton and Pixar" [1][2] | the contrast suits a cheerful convenience store; reviewers of its sequel felt the cartoon look undercut the fear |
| S2 | **Lo-fi film realism**, sometimes called "lo-fi sci-fi" or cassette futurism | *Alien: Isolation* (Creative Assembly, 2014) | not hyperrealism: a world built to look like one film, the 1979 *Alien*. Chunky analogue technology (tube TVs, tape, no LEDs), the set's own lighting, colour and grain. As its creative lead put it, "lo-fi didn't necessarily mean crappy" [3][4] | a strong match: a 1990s night-shift store, buzzing strip lights, CRT security screens (0.4.1's camera wall) |
| S3 | **Anime style (cel or toon shading)** | *MiSide* (AIHASTO, 2024; made in Unity) | flat bands of colour instead of smooth shading, outlines, anime proportions. MiSide starts as a chibi 2.5D phone game and becomes first-person 3D; it's listed among cel-shaded games [5]. **Not sourced:** how its shading is done | an unusual choice for a store; strongest if Karen herself is a character on screen |
| S4 | **PS1-era low poly ("PSX style")** | *Mouthwashing* (Wrong Organ, 2024; Unity) | few polygons, tiny unsmoothed textures, wobbling vertices, warped textures, short draw distance in fog or dark. Its art designer took the style from Puppet Combo's horror games [6][7][8] | cheap to make and to run; very popular in indie horror, so it reads as "horror" at a glance, and as "one of many" |
| S5 | **Analog horror / VHS found footage** | *The Backrooms* (Kane Pixels, 2022; rendered in Blender) | an ordinary place, as if taped on a 1990s camcorder: soft focus, colour bleed, tape noise, timestamps. Whether it counts as "analog horror" is argued over [9] | made for Kehai's CCTV: her cameras, the replays and the trailer could all wear it |
| S6 | **Today's look, tidied** (physically based realism) | — | the current materials and lights, with a colour grade, fog and a consistent palette ([LEVEL_DESIGN.md §7](../../../agents/store/LEVEL_DESIGN.md#7-palette-and-materials)) | the cheapest step; the baseline the others are judged against |

**A correction to the brief:** *Alien: Isolation* isn't hyperrealism (photographs turned into
models, as in *Unrecord* or *Bodycam*). It's a faithful period look. Hyperrealism isn't on the
list: on an 8 GB Mac it costs far more than it gives.

## 2. Light, shadow and filters, style by style

| | Lighting | Shadows | Filters (post-processing) | Materials |
|---|---|---|---|---|
| S1 cartoon | bright, warm, soft fill so nothing is pitch black | soft, coloured (blue or purple), not black | a little bloom, saturated grade | plain colours, few textures |
| S2 lo-fi film | strip lights and practical lamps you can see; cool green-white fluorescents, warm spill from signs | hard, long, from few lights; deep blacks | film grain, a slight lens blur at the edges, a green-teal grade, light bloom on lamps, scan lines on screens | worn, dirty, scuffed floors; reflections on the lino |
| S3 anime | a key light that sets two or three bands of colour | a crisp shadow band, often a colour, not black | outlines, a soft glow, pastel grade | flat colour per surface, hand-painted accents |
| S4 PSX | baked into the textures, or per-vertex | none, or blobs under feet | a low resolution (320×240 to 640×480, scaled up), pixelated filtering, colour dithering, fog that ends the world 15–25 m out | 64–256 px textures, about 1,000 triangles a prop |
| S5 VHS | flat, a little overexposed, camcorder auto-exposure | soft, smeared | tape noise, colour bleed, a timestamp, interlace lines, a 4:3 frame for camera views | as today; the filter does the work |
| S6 tidied | baked light plus a few real-time lamps | real-time only from those few | a colour grade, fog, a vignette | as today, with one palette |

*An example, one slice:* the backstreet at night, the breaker box under a wall lamp.
- S2: the lamp buzzes and flickers, a hard shadow of the skip across wet tarmac, grain over
  everything, the box's LEDs replaced by a dial.
- S4: the lamp is a yellow blob, the fog swallows the street at 20 m, the box is twelve polygons
  and wobbles as you walk.
- S5: you see it as her camera does: a timestamp in the corner, the lamp blooming white, the
  colours bleeding.

## 3. How you'll see them and play with them

**The Look Lab** (a scene of its own; [§5](#5-the-island-a-scene-a-project-or-a-branch)):

- **Four slices side by side,** copied from the store's own pieces when the scene is built: a
  staff room (the clock, the lockers), a corner of the parking (a lamp post, a car, the doors),
  the backstreet (the boxes, the skip), and a run of maze aisle (shelves, a chiller, the cold
  room door).
- **One key switches the whole look** between the styles you picked (Q28); a panel shows the
  style's settings as sliders: exposure, fog distance and colour, grain, bloom, the grade, the
  render resolution, the outline width.
- **Walk it** as the player, with the flashlight, so you see a style the way you'd play it.
- **A contact sheet:** one command takes a picture of every style in every slice from the same
  camera spots, as a grid to put side by side.
- **Saving a tweak:** the panel saves your settings to a small file outside git; an agent turns
  your favourite into code (`StylePresets.cs`), since authored data lives in C#.
- **The cost is shown live:** milliseconds per frame in a corner, so a beautiful setting that
  costs 10 ms is obvious at once (C8.4 measures it properly on both computers).

## 4. What makes a game look good and still run fast

It must run on this Mac (an M2 with 8 GB), other Macs and Windows. The game already draws its
16,500 shelf slots with the GPU Resident Drawer, which needs the Forward+ renderer and works best
in big scenes [10].

| Trick | What it is | Why it's cheap or dear | Kehai |
|---|---|---|---|
| **Baked light** | light and soft shadow worked out once and stored | free at play time; static things only | the walls, floors and shelves |
| **Light probes** (Unity 6: Adaptive Probe Volumes) | stored light in the air, so moving things are lit to match | cheap; it streams for big scenes [11] | the player's hands, customers, Karen |
| **Few real-time shadow lights** | only a handful of lamps cast live shadows | every shadow light redraws the scene; fewer cascades and a shorter shadow distance cut the cost [12] | her flashlight, a flickering lamp |
| **Fog and darkness** | the world fades out at a distance | hides what isn't drawn, and it's horror's oldest friend | every style |
| **A colour grade** | one look-up table recolours the whole frame | nearly free | every style |
| **Bloom, grain, vignette** | glow, noise, dark corners | cheap-ish, but Apple's GPUs (and phones') draw in tiles, where every full-screen pass costs more; Unity's own advice is to keep them few [13] | a short stack: grade + grain + vignette, bloom at low quality |
| **Ambient occlusion** (darkening in corners) | baked into the textures, or real time (SSAO) | baked is free; SSAO needs extra passes and is poor on tile GPUs [13] | baked |
| **Trim sheets and atlases** | many props share one texture | fewer textures to switch between | new props |
| **Decals** | grime, stains and posters stuck on surfaces | adds detail without new models | the lived-in look |
| **Level of detail and culling** | far things drawn simpler, hidden things not at all | big savings in a maze, where most is hidden | already partly; checked per layout |
| **A lower render scale** | draw fewer pixels, scale up | the biggest single lever on a weak machine | a Low quality option, and part of S4's look |

**The rule of thumb:** light that doesn't move is baked; few lights move; the frame gets a short
list of filters, and the darkness does the rest.

## 5. The island: a scene, a project or a branch

Art never goes into the public repo. Today it reaches the Windows PC through the private
`kehai-assets` repo's releases.

| | A scene in this project ("Look Lab") | Its own Unity project | A git branch |
|---|---|---|---|
| The real props and shaders | yes, the same ones | copied by hand, and they drift | — |
| Disk on this Mac (13 GB free) | almost nothing | another Library, about 4–5 GB | nothing |
| Can it break the game? | no, if it's never in a build | no | — |
| Shared how? | the builder script is code (in git); the scene is made by a menu; new art goes to `kehai-assets` | the whole project zipped to `kehai-assets` | **can't**: art isn't in git, so a branch holds nothing of the look |
| **Verdict** | **my pick** | if the game's project ever gets too fragile | not workable |

**The name:** the scene already has a showcase root called `Models_Island`, which also carries the
store's NavMesh surface. Deleting or moving it breaks navigation, so the new scene is called
**Look Lab**, and `Models_Island` stays untouched.

## 6. Sources

1. tinyBuild, *Hello Neighbor* page: "Pixar-style visuals". <https://www.tinybuild.com/helloneighbor>
2. Game Developer, "Designing a domestic hunter-killer thriller the *Hello Neighbor* way" (the art moving from realistic to cartoony; "between Tim Burton and Pixar"). <https://gamedeveloper.com/design/designing-a-domestic-hunter-killer-thriller-the-i-hello-neighbor-i-way>
3. Engadget, "Tube TVs, VCRs and magnets give *Alien: Isolation* its signature look" (2014). <https://www.engadget.com/2014/10/07/alien-isolation-launch-interview/>
4. PC Gamer, "The making of *Alien: Isolation*" and "Why I love the low-fi tech of *Alien: Isolation*". <https://www.pcgamer.com/uk/the-making-of-alien-isolation> · <https://www.pcgamer.com/why-i-love-the-low-fi-tech-of-alien-isolation>
   The details about the 1979 film (the art team kept to it, the lighting matched to its set) come from interviews reported second-hand.
5. Wikipedia, "MiSide" (Unity; AIHASTO; released 11 December 2024; chibi 2.5D then first-person 3D; listed with cel-shaded games). <https://en.wikipedia.org/wiki/MiSide>
6. The A.V. Club, *Mouthwashing* review ("lo-fi horror"). <https://www.avclub.com/mouthwashing-review-lo-fi-horror>
7. Bloody Disgusting, *Mouthwashing* review. <https://bloody-disgusting.com/video-games/3832240/review-mouthwashing-is-a-surreal-trip-into-high-tension-horror>
   Its art designer's credit to Puppet Combo is reported in its Wikipedia entry.
8. GamesRadar, "Exploring the resurgence of the low-fi 3D visual style of the PS1 era"; 3D AI Studio, "PS1 graphics: what makes the look" (vertex snapping, warped textures, low resolution, point filtering, fog, dithering). <https://www.gamesradar.com/exploring-the-resurgence-of-the-low-fi-3d-visual-style-of-the-ps1-era> · <https://www.3daistudio.com/blog/ps1-graphics-how-to-make-ps1-style-3d-models>
9. No Film School, "*The Backrooms* is the viral horror short shaking the internet up" (Kane Parsons, 2022). <https://nofilmschool.com/backrooms-short-film-explained>
10. Unity Manual (6.3 LTS), GPU Resident Drawer. <https://docs.unity3d.com/6000.3/Documentation/Manual/urp/gpu-resident-drawer.html>
11. Unity Manual (6.2), Adaptive Probe Volumes. <https://docs.unity3d.com/6000.2/Documentation/Manual/urp/probevolumes.html>
12. Unity Manual, Shadow optimisation. <https://docs.unity3d.com/Manual/shadows-optimization.html>
13. Unity Manual, Optimisation for untethered devices (on tile-based GPUs: bloom's extra loads, SSAO's passes). <https://docs.unity3d.com/Manual/urp/xr-untethered-device-optimization.html>
14. Unity Toon Shader manual (cel shading for URP; Unity 6). <https://docs.unity3d.com/Packages/com.unity.toonshader@0.13/manual/index.html>
