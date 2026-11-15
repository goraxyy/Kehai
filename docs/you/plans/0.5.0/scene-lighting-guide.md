# Practical guide: realistic store, simple products

Part of [0.5.0](../0.5.0-plan.md). The Look Lab is now a **separate external project** (option B),
with matching Unity/URP and selectively copied private fixtures. Setup and modelling remain on hold. Owner's direction, 14 November: realistic surroundings with
simple low-poly items, especially products. Resources below are official Unity 6 documentation;
use the project's installed Unity/URP version when following the controls.

## 1. Design the scene before decorating it

**Low-poly** means fewer faces in a mesh. Here a cereal box can remain a six-sided box while its
label and material match the realistic shop; like a distant building in a racing game that reads
correctly without modelling every brick.

1. Make one small representative aisle first: a shelf, pillar join, floor, ceiling light, fridge,
   signage and 5–10 products. Include a doorway and a dark corner. Reuse it as the comparison scene.
2. Keep metre scale consistent. Judge it from the player's actual eye height, walking and
   crouching, rather than only a floating editor camera.
3. Choose a small palette: warm off-white paint, worn grey steel, muted signage, slightly glossy
   flooring. Make useful objects recognisable by shape, contrast and placement.
4. Products keep simple geometry and readable labels. Use shared materials/texture sheets where
   appropriate; don't give thousands of stock objects unique high-resolution materials.
5. Add believable wear where hands, wheels and water touch surfaces. Avoid covering everything
   with equal amounts of dirt; it removes the visual hierarchy.
6. Compare neutral lighting first. If a model only looks good under heavy grain, blur or black
   shadows, fix the material or lighting before adding effects.

**Physically based materials** describe surface colour, metalness and smoothness. Kehai uses
these to distinguish painted shelving, plastic bottles and polished flooring; the familiar
example is how a metal kettle reflects a room differently from a cardboard cereal box.

## 2. Set up lighting in this order

[Unity: lighting configuration workflow](https://docs.unity.com/en-us/engine/6000.0/manual/lighting-overview/lighting-configuration-workflow)

1. Start with camera effects off and a stable exposure. Keep environment light modest enough
   that ceiling lights matter, but don't make the whole store unreadably black.
2. Put visible fixtures first; light should plausibly come from the strips, signs and lamps.
   Emissive material makes a fixture look bright, but doesn't automatically provide the real-time
   light required to illuminate the room.
3. Give one aisle its main light; check faces, price labels and floor reflections. Then extend
   the scheme. Use cooler store lighting against warmer staff-room light as a starting comparison.
4. Use few shadow-casting lights. Many overlapping shadowed lights are expensive even if the
   meshes are simple. Check the Mac before repeating fixtures across the entire floor.
5. Bake only what remains fixed. **Baked light** is precomputed illumination: Kehai can use it
   for fixed room structure, like a photograph storing the light that was there when taken.
   A different shelf layout changes shadows and bounced light, so reuse of one bake is unsafe.
6. Compare per-layout bakes against a modest real-time setup. **Mixed light** combines baked
   and live contributions; turning its live component off does not necessarily remove its baked
   glow. Blackout and Room lights must actually look off. Don't bake permanent bright spill
   from a light Karen is expected to switch off.
7. Add reflection probes for interiors. A **reflection probe** stores an image of surrounding
   surfaces for shiny objects: Kehai's fridge doors and floor use it like a shop window reflecting
   the room. Prefer captured/baked probes over updating every camera frame.
8. Test all six layouts, power on/off, fog and the flashlight, in a built player on Mac and Windows.
   Keep art lighting separate from Karen's perception rules unless an explicit mechanic says otherwise.

Read next:
- [Light modes: Baked, Mixed, Realtime](https://docs.unity.com/en-us/engine/6000.0/manual/lighting-overview/lighting-light-sources/components/configuring/light-modes/introduction)
- [Reflection probes in URP](https://docs.unity.com/en-us/engine/6000.0/manual/lighting-overview/lighting/reflection-probes/introduction)
- [URP lighting and limits](https://docs.unity.com/en-us/engine/6000.0/manual/lighting-overview/lighting)

## 3. Apply effects to the player's camera

**Post-processing** modifies the rendered picture. Kehai uses it for subtle camera treatment
or a temporary trick effect; a phone photo filter is the everyday example.

[Unity: add post-processing in URP](https://docs.unity.com/en-us/engine/6000.0/manual/post-processing-and-full-screen-effects/urp/add-post-processing)

1. Select the player camera; enable its **Post Processing** setting.
2. Add a **Global Volume** to the scene and assign a Volume Profile (a private asset).
   A volume groups screen effects; Kehai can blend a room-specific look, like a photo app keeping
   several filter settings together in one preset.
3. Ensure the camera's Volume Layer Mask includes the volume's layer, and its volume trigger
   follows the player. A local volume needs a collider and blend distance; global needs neither.
4. Add one override at a time; enable the checkbox beside each parameter you change. Keep a
   neutral profile to compare instantly.
5. Use a separate temporary effects layer/profile for fatigue, hits and Karen's tricks. On effect
   end restore the baseline; don't let overlapping effects permanently overwrite each other.
6. Draw task text and prompts after the scene effect where possible, so blur/distortion doesn't
   make essential instructions unreadable. Give camera blur, grain and distortion off controls.

| Effect | What it does | Proposed use in Kehai | Familiar example |
|---|---|---|---|
| Colour adjustment / tonemapping | controls colour and how bright values map to the screen | restrained cool store / warm staff room | a phone's colour filter and highlight recovery |
| Bloom | spreads bright pixels into a glow | subtle on strip lights and signs | bright lamps glowing in a photograph |
| Film grain | adds fine noise | optional, faint baseline; stronger on CCTV | visible grain in a film scan |
| Lens distortion | bends the picture | subtle lens character or brief trick effect | a wide-angle action camera |
| Chromatic aberration | separates colour near edges | brief effect only | coloured fringes from a cheap lens |
| Vignette | darkens picture edges | very mild; don't conceal useful peripheral detail | a camera lens shading the corners |
| Depth of field | blurs by distance from focus | inspect/menu shots; avoid a permanent blur while navigating | portrait mode blurring the background |
| Motion blur | blurs movement | optional/off by default | a moving object smeared in a long exposure |
| Custom full-screen blur/warp | purpose-built image effect | temporary detergent hit or panic distortion if existing effects cannot do it | heat shimmer or frosted glass |

Depth of field, motion blur and general blur are different effects. A general screen blur may
need a custom URP Renderer Feature/shader; don't add an old Built-in pipeline tutorial or force
Unity 6 into Compatibility Mode just to copy older code. Use the current Render Graph workflow
when a custom effect is necessary, and measure its full-screen cost on the M2.

Resources:
- [URP effects and custom post-processing](https://docs.unity.com/en-us/engine/6000.0/manual/post-processing-and-full-screen-effects/urp/in-urp)
- [Film Grain reference](https://docs.unity.com/en-us/engine/6000.0/manual/post-processing-and-full-screen-effects/urp/effect-list/film-grain)
- [Lens Distortion reference](https://docs.unity.cn/6000.0/Documentation/Manual/urp/Post-Processing-Lens-Distortion.html)

## 4. How the owner tries it

Fold this into C8.1–C8.4: a neutral preset, realistic clean lighting, and realistic worn/night
lighting, all with the same low-poly products. Switch each effect individually; save the chosen
numbers as authored C# settings, with any required Unity assets in private `kehai-assets`.
Show identical camera views and a short walk, with CPU/GPU frame time and memory on the Mac.
A contact sheet judges lighting/materials; walking judges shimmer, readability and comfort.
No claim of a supported minimum until a player build has been measured.

## Trying the code presets

Implementation lives in `Look/Scripts/StylePresets.cs`. The external lab's Inspector switches
between Neutral, Fluorescent Realism and Soft Stylised, with an effects toggle. Bloom and grain
start subtle; motion blur, lens distortion and colour fringing start at zero. Depth of field
is off during play so tasks and threats stay readable.

When suitable storage is available, create the separate private Unity project with the **same
Unity and URP versions**. Put an empty `KEHAI_PRIVATE_LOOK_LAB` file at its root. Select one
location slice in the game project, then **Kehai → Look Lab → Export selected slice privately**.
Save outside the public repository and import into the external project. Repeat for rooms,
parking, backstreet and maze; use one scene per slice. The exporter preserves the store scene,
exports selected parents only once, and cleans up its temporary scene.

Use **Add style controls to external lab**, enable post-processing on the player camera's URP
settings, and select the style on `LookLabStyle`. Keep the same camera position and light setup
when comparing styles. **Capture camera comparison privately** writes three 1280×720 images
and an HTML comparison outside the public project. This is a visual comparison, not a benchmark.
Measure frame time and memory separately on the Mac and Windows before adopting a style.
No second project or imported art has been created by the implementation.
