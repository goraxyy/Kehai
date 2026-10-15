# Blink sidecar

The webcam half of Karen's blink channel (see `IDEAS.md` → *Blink*, and `BlinkTracker.cs`).
A small helper process owns the camera, decides how shut your eyes are, and sends one JSON
packet per frame to the game on `127.0.0.1:5066`. The game does the rest: calibration,
hysteresis, predicting when your eyes will reopen, and what Karen does with the window.

**Privacy.** Opt-in only (F8 in game shows what is used and asks first). Frames are processed
in memory and thrown away; the helper never writes an image, never records, and only ever
sends to localhost. The one tool that saves anything is `record_dataset.py`, which asks you
first and keeps only 24×24 eye crops, with the time and each helper's reading for every frame,
in a folder you choose; `label_blinks.py` adds your own labels beside them.

## Quick start on a Mac (no installs, no downloads)

The Mac helper, `mac/BlinkVision.swift`, uses Apple's own Vision face landmarks, so it
needs nothing beyond the Xcode command line tools.

1. **Build the helper once** (from the project folder):

   ```bash
   tools/blink/mac/build.sh
   ```

   It prints `Built …/tools/blink/mac/build/BlinkVision`. If `swiftc` is missing, run
   `xcode-select --install` first.

2. **Optional: check it sees your eyes outside the game.** Run
   `tools/blink/mac/build/BlinkVision` in Terminal. macOS asks once whether Terminal may use
   the camera; click Allow. Every two seconds it prints a line like
   `eyes ####......  closed 0.40  (eye height 0.210, usually 0.330)  30 fps`. Close your eyes
   and the bar should fill up. Press Ctrl-C to stop it.

3. **In the game** (press Play in Unity):
   - Press **F10** to open the blink test panel. It is a checklist that ticks itself off.
   - Press **F8**, then **Y**, to allow the webcam. The game starts the helper for you.
   - The first time, macOS asks whether **Unity** may use the camera; click **Allow**. If you
     clicked *Don't Allow* earlier, turn Unity on in **System Settings → Privacy & Security →
     Camera**, restart Unity, and press **R** in the panel to restart the helper.
   - When step 3 of the checklist turns green, press **F9** to calibrate. It takes about 12
     seconds and tells you what to do, in big letters on the right of the panel:
     1. *Keep your eyes open and look at the screen* (3 seconds).
     2. *Close your eyes and keep them closed until you hear the beep* (about 3 seconds).
     3. *Open your eyes and blink 3 times, the way you usually do.* It then says how many of
        the 3 it caught.
   - Now blink. The big label flips to **EYES CLOSED**, the graph shows a red spike above the
     yellow line, and the blink counter goes up. The calibration is remembered.

4. Close the panel with **F10** (or **Esc**) and play. **F8** turns the webcam off again, and
   **Esc → Settings** has the same switches. Holding **B** closes your eyes with the keyboard at
   any time, with or without the webcam.

**Frame rate.** A MacBook's built-in camera tops out at 30 frames a second (1280×720); run
`BlinkVision --list-formats` to see what yours offers. A blink lasts 100–400 ms, so 30 fps
still sees 3 to 12 frames of it. An external 60 fps webcam catches blinks sooner: plug it in
and press **V** in the panel to switch cameras.

**If it doesn't work:** the panel shows the helper's last lines. *"can't see your face"*
means more light on your face, or sit facing the camera. For glasses with glare, tilt the
screen a little. If calibration says your eyes read almost the same open and closed, the
camera can't see your eyelids well enough: more light from the front usually fixes it.

**More accurate: MediaPipe.** Apple's face landmarks are built in and need no downloads, but
they are not made for blinks. MediaPipe's face model gives a blink score for each eye and is
noticeably more accurate. Set it up once (about 100 MB of downloads):

```bash
tools/blink/setup_mediapipe.sh
```

After that the game uses MediaPipe by itself; **M** in the F10 panel switches between the
two helpers, and each keeps its own calibration.

## The Python helper (any OS)

This is the MediaPipe helper. `setup_mediapipe.sh` sets it up on a Mac; by hand, anywhere:

```bash
cd tools/blink
python3 blink_server.py --synthetic            # no camera, no installs: a human-shaped blink trace
python3 -m venv .venv && .venv/bin/pip install -r requirements.txt
.venv/bin/python3 blink_server.py --method ear --preview   # MediaPipe face mesh + eye aspect ratio
```

In game: **F8** to consent (the webcam path stays off until you do), **F9** to calibrate
(10 s, eyes open then a few blinks), **F10** to see what the game reads, and **B** is the
keyboard fallback at any time.

| method | needs | notes |
|---|---|---|
| `mac/build/BlinkVision` | a Mac, the Xcode command line tools | Apple Vision landmarks; what the game starts first on a Mac |
| `--synthetic` | nothing | for testing Karen's reactions; same generator the eval bots use |
| `--method ear` | opencv, mediapipe | robust, adapts to your face automatically |
| `--method blendshapes --model face_landmarker.task` | + the MediaPipe FaceLandmarker model file | most accurate off the shelf; download the `.task` file from the MediaPipe Face Landmarker docs |
| `--method cnn --onnx eye_cnn.onnx` | + onnxruntime, your model | the "own training path" below |

## Measuring accuracy, and training your own model

How well does each helper actually see your blinks? The honest answer needs three things:
labels from a person, not from another model; sessions the model never trained on; and every
helper scored on the same frames. The tools below do that. Everything runs from `tools/blink`
in the MediaPipe environment (`setup_mediapipe.sh`, or `pip install -r requirements.txt`).

```bash
python3 record_dataset.py --out eye_dataset --person p1 --minutes 3 --vision   # one session; repeat
python3 label_blinks.py --data eye_dataset                                       # your labels, frame by frame
python3 eval_blink.py --data eye_dataset                                         # score ear, blendshapes, vision
python3 train_eye_cnn.py --data eye_dataset --out eye_cnn.onnx                   # train, then test on held-out sessions
python3 eval_blink.py --data eye_dataset --onnx eye_cnn.onnx                     # all four on the same frames
```

### 1. Record sessions

Each run of `record_dataset.py` is one **session**: one sitting, saved as
`eye_dataset/<session>.npz`. The id is the start time unless you give `--session`; `--person`
tags who is in it, with a pseudonym (`p1`), never a name. Record several, on different days,
in different light, with and without glasses: the trainer holds out whole sessions, so more
sessions means a fairer test. Five sessions of three minutes is a good start. `--notes` keeps
a word about the conditions.

A session goes like this:

1. **The game's calibration** (12.5 s, the same steps as F9): eyes open for 3 s; eyes shut
   until the beep (3.5 s); then three ordinary blinks. The scorer calibrates each helper on
   this part exactly as the game would, and never scores it.
2. **Free part**: look at the screen (read something, watch something) and blink as usual.
3. **A few closures on purpose**: press SPACE, shut your eyes for a second or so, open them,
   press SPACE again. A red line on screen says marking is on.

For every frame it keeps the two eye crops, the time, and what each helper would have told
the game: `ear` (MediaPipe face mesh + eye aspect ratio), `blendshapes` (if
`face_landmarker.task` is here) and, with `--vision` on a Mac, `vision` (the Apple Vision
helper, which runs alongside on its own camera stream and is lined up by capture time to
within half a frame; build it first with `mac/build.sh`). It runs every helper on every
frame, so it may record below the camera's frame rate; the times are real either way.

**Why SPACE isn't the test labels.** Pressing a key takes 150–250 ms, about as long as a
whole blink, so a key pressed live can't say when a blink started or ended. SPACE is kept as a
separate hint (it shows in the labeller) and nothing more. The labels everything is scored
against come from the next step.

### 2. Label your blinks

```bash
python3 label_blinks.py --data eye_dataset                     # the next session not yet labelled
python3 label_blinks.py --data eye_dataset --session 20261015-141200
```

It shows the saved crops, both eyes per frame, two seconds to a page. **Closed means the eye
is more than half shut: the pupil mostly covered.** Click a frame to mark it closed, drag
across a run, shift-click to fill from the last click (for a long closure). Right-click marks
a frame you can't judge (blurred, turned away); those are left out of scoring. SPACE goes to
the next page and counts the page as reviewed; only reviewed pages are scored, so you can
stop and come back. No helper's reading is shown, so none can sway you. A three-minute
session takes about ten minutes. Label at least two sessions fully.

The labels go to `<session>.labels.json` beside the recording, which is never changed: the
helpers' readings (the teachers) and your labels never share a file.

### 3. Score every helper

```bash
python3 eval_blink.py --data eye_dataset --per-session --json results.json
```

Each helper's reading runs through a copy of the game's `BlinkTracker`: the session's own
calibration, the smoothing (none as the eye closes, a 0.6 step per frame as it opens), the
0.5 / 0.3 hysteresis, and the blink length it learns for `PredictedReopenIn`. So the numbers are what
Karen would get. Sessions missing a helper are left out (or the helper is), so that every
helper is scored on the same frames; it says which and why.

| column | meaning |
|---|---|
| frame P / R / F1 | "eyes closed" per frame against your labels |
| AP | average precision of the raw reading before any threshold, so it doesn't depend on the tracker's settings |
| blink recall (95%) | the share of your blinks the game saw (a detection overlapping your blink), with a Wilson interval; *seen* is the count |
| false/min | blinks the game saw that you didn't make, per minute; seeing one blink twice counts once |
| onset | when the game learnt of a blink, after your first closed frame. 0 = the same frame; negative = it fires before the eye is half shut |
| reopen seen | when the game saw the eyes open, after your first open frame |
| reopen predicted | `PredictedReopenIn` at the moment of detection against your first open frame: median error / mean absolute error / how often it was **late** (Karen acting while you can already see). Blinks under 0.5 s only; longer closures are counted apart |
| window left | how long the eyes really stayed shut after the game learnt of the blink |
| compute | the helper's own time per frame. Onset is measured on recorded frames, so the live delay is onset + compute, plus the hop to the game (under a millisecond) and up to one game frame |

Times are on the camera's frame clock, so one frame (33 ms at 30 fps) is the resolution.

### 4. Train your own model (distillation → tiny CNN → ONNX)

```bash
python3 train_eye_cnn.py --data eye_dataset --out eye_cnn.onnx
python3 blink_server.py --method cnn --onnx eye_cnn.onnx
```

- **Whole sessions** go to train, validation or test, never single frames: neighbouring
  frames at 30 fps are near copies, and splitting them is how a model gets a perfect score on
  data it has as good as seen. With three or more people (`--person`) it holds out whole
  people instead (`--split-by person`), which is what "works on a new face" means.
- By default the **test** set is the latest quarter of your labelled sessions, **validation**
  the next latest, and training the rest. `--test` and `--val` choose them by hand.
- Training targets come from a **teacher**, the first of blendshapes, ear and vision that
  every training session has, scaled by that session's calibration. Where a training
  session has your labels, they win.
- The best epoch is picked on **validation**. The numbers it reports come only from the
  **test** sessions: `eval_blink.py`'s full table for the CNN next to the teachers, against
  your labels. Without a labelled test session it says so and gives only agreement with the
  teacher, which is not accuracy.
- It writes `eye_cnn.onnx.json` beside the model: which sessions went where, the teacher, the
  test numbers. `eval_blink.py --onnx` reads it and never scores the model on a session it
  trained or was selected on.

The model contract — input `eye` `[n,1,24,24]` grayscale 0..1 with rows top-down, output
`closed_logit` `[n,1]` — is the same one `SentisBlinkSource.cs` expects, so the same file can
run in-engine: add `com.unity.ai.inference`, import the `.onnx`, and define `KAREN_SENTIS`.
It is exported at ONNX opset 13 with PyTorch's TorchScript exporter (2.9+ defaults to a newer
one), and the trainer checks the file gives the same outputs as PyTorch.

### What the numbers can and can't say

- Your sessions measure your eyes, your camera, your rooms. Other people's numbers need their
  recordings (with their consent) and `--split-by person`.
- A handful of blinks gives a wide interval: read the 95% column before comparing helpers.
- Closed is defined as more than half shut. A helper that fires earlier in the closing shows
  a negative onset; that's a difference in definition, not a time machine.
- The Vision reading comes from its own camera stream, matched to within half a frame.

### Checking the tools without a camera

```bash
python3 make_fixture.py --out /tmp/blink_fixture        # 5 synthetic sessions, 3 labelled
python3 train_eye_cnn.py --data /tmp/blink_fixture --out /tmp/blink_fixture/eye_cnn.onnx
python3 eval_blink.py --data /tmp/blink_fixture --onnx /tmp/blink_fixture/eye_cnn.onnx
python3 -m pytest tests                                 # pip install pytest; numpy is enough for all but one
```

The fixture draws eyes, blinks them in the human shape, and simulates each helper with its
own kind of flaw. It proves the scripts work end to end and that the metrics are computed
right; the numbers it prints say nothing about real eyes, and the report says so.

### Files and privacy

| file | what |
|---|---|
| `eye_dataset/<session>.npz` | per frame: two 24×24 eye crops, the time, each helper's reading and timing, the SPACE hint |
| `eye_dataset/<session>.labels.json` | your labels (closed / can't tell / reviewed, as frame ranges) |
| `eye_cnn.onnx`, `eye_cnn.onnx.json` | your model and its record of which sessions it saw |

All of it is personal: it stays on this computer and out of git (`.gitignore` here). Same
rules as before: recording is opt-in and asks first, only crops are kept, never full frames,
and nothing is sent anywhere. Delete the folder to delete the data.

## Packet

```json
{"seq": 1234, "closed": 0.93, "conf": 0.88, "src": "ear",
 "capture": 1727000000.1234, "sent": 1727000000.1391, "fps": 30.0}
```

`capture` → `sent` is the pipeline's own latency; the game subtracts it to date the true start
of each blink, and Karen acts in whatever is left of the ~300 ms.
