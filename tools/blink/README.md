# Blink sidecar

The webcam half of Aiko's blink channel (see `IDEAS.md` → *Blink*, and `BlinkTracker.cs`).
A small helper process owns the camera, decides how shut your eyes are, and sends one JSON
packet per frame to the game on `127.0.0.1:5066`. The game does the rest: calibration,
hysteresis, predicting when your eyes will reopen, and what Aiko does with the window.

**Privacy.** Opt-in only (F8 in game shows what is used and asks first). Frames are processed
in memory and thrown away; the helper never writes an image, never records, and only ever
sends to localhost. The one tool that saves anything is `record_dataset.py`, which asks you
first and keeps only 24×24 eye crops in a folder you choose.

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
| `--synthetic` | nothing | for testing Aiko's reactions; same generator the eval bots use |
| `--method ear` | opencv, mediapipe | robust, adapts to your face automatically |
| `--method blendshapes --model face_landmarker.task` | + the MediaPipe FaceLandmarker model file | most accurate off the shelf; download the `.task` file from the MediaPipe Face Landmarker docs |
| `--method cnn --onnx eye_cnn.onnx` | + onnxruntime, your model | the "own training path" below |

## Own training path (distillation → tiny CNN → ONNX)

```bash
python3 record_dataset.py --out eye_dataset --minutes 3   # asks for consent; MediaPipe labels each crop
python3 train_eye_cnn.py --data eye_dataset --out eye_cnn.onnx
python3 blink_server.py --method cnn --onnx eye_cnn.onnx
```

The model contract — input `eye` `[n,1,24,24]` grayscale 0..1 with rows top-down, output
`closed_logit` `[n,1]` — is the same one `SentisBlinkSource.cs` expects, so the same file can
run in-engine: add `com.unity.ai.inference`, import the `.onnx`, and define `AIKO_SENTIS`.

Datasets and models are personal and stay out of git (`.gitignore` here).

## Packet

```json
{"seq": 1234, "closed": 0.93, "conf": 0.88, "src": "ear",
 "capture": 1727000000.1234, "sent": 1727000000.1391, "fps": 30.0}
```

`capture` → `sent` is the pipeline's own latency; the game subtracts it to date the true start
of each blink, and Aiko acts in whatever is left of the ~300 ms.
