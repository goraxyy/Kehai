# System requirements

What a computer needs to play Kehai, and to work on it. Written on 26 November 2026, for 0.5.2.
The Steam page's requirements (Milestone 2 of the [release plan](RELEASE_PLAN.md#milestone-2-steam-page-live))
come from this page, once real builds have been tried on more machines.

**How sure each line is:**

| Mark | Meaning |
|---|---|
| **Unity's floor** | Unity 6.5's own requirements for a built game. Below these, the game can't run at all ([Unity 6.5's system requirements](https://docs.unity3d.com/6000.5/Documentation/Manual/system-requirements.html)). |
| **Measured** | Tried on a real machine, with the result given. |
| **Estimate** | Not tried yet. Checked when the draft Testing builds (C13.3) go to other machines. |

## Playing the game: the minimum

| | Mac | Windows |
|---|---|---|
| **System** | macOS 12 Monterey or newer (Unity's floor) | Windows 10 version 21H1 (build 19043) or newer, 64-bit (Unity's floor) |
| **Processor** | Apple silicon (M1 or newer), or an Intel Mac (Unity's floor). Played on an M2 (measured) | x64 with SSE2, or Arm64 (Unity's floor). A 4-core processor from about 2017 on (estimate) |
| **Memory** | 8 GB (measured: an M2 with 8 GB runs the game, and the editor beside other apps) | 8 GB (estimate) |
| **Graphics** | A Metal-capable GPU: every Apple silicon Mac; Intel and AMD GPUs with Apple's own drivers (Unity's floor) | A DirectX 11 or 12, or Vulkan, capable GPU with the maker's drivers (Unity's floor). About a GTX 1050 / RX 560 or better (estimate) |
| **Disk** | About 1 GB free (estimate: the stripped Training player is 196 MB; the Testing build, with art, sound and the trick videos, hasn't been measured) | The same |
| **Screen** | 1280 × 720 or larger (estimate: the UI is a quick pass, redesigned before launch) | The same |
| **Controls** | Keyboard and mouse | Keyboard and mouse |
| **Webcam** | Optional, for blinking with your real eyes (B holds your eyes shut instead). It needs a camera and your consent | The same |
| **Internet** | Not needed to play. A playtest build uploads its session only with your consent | The same |

**Not supported:**
- **Linux and Steam Deck:** no build yet, so it's a release-plan question.
- **Running the game in a virtual machine or emulator:** Unity doesn't support that for its players.

## Playing the game: recommended

Estimates until the Testing builds have run on more machines:
- **Mac:** Apple silicon with 16 GB.
- **Windows:** a 6-core processor, 16 GB and a GTX 1660 or RX 5600 class GPU.

The store has 277 lights and about 10,000 objects to draw, so the graphics card is the likeliest
limit on older machines. That isn't measured yet.

## Working on the game

For you, the sound designer, an artist and the agents:

| | Mac | Windows |
|---|---|---|
| **Unity editor** (6000.5.10f1) | macOS 13 Ventura or newer; Apple silicon (M1 or newer), or an Intel Mac; a Metal-capable GPU; Rosetta 2 installed (Unity's floor) | Windows 10 21H1 or newer, x64; or Windows 11 21H2 or newer on Arm64; a DirectX 11/12- or Vulkan-capable GPU (Unity's floor) |
| **Memory** | 8 GB is Unity's minimum. On this M2 with 8 GB, only one Unity can run at a time (measured), hence the rule of one heavy job at a time | 16 GB or more; the training PC has 32 GB |
| **Disk** | About 15 GB for the project, its Library cache and one clone (measured on this Mac, roughly); more for the art's history once Phase 7 is set up ([storage](plans/0.5.0/storage.md)) | The same, plus room for training runs (the PC has 2 TB free) |
| **Tools** | Python 3 for the tools; `tools/rl/.venv` for training (ML-Agents) | Python 3 and the same training environment ([setup](../../tools/rl/README.md)) |

**Training Karen:**
- **Where:** on the Windows PC (an i9-13900KF, 32 GB, an RTX 4090), several copies of the Training player at once.
- **The Mac:** runs one copy at a time.
- **Speed:** the Training player plays about 1.7 times faster than the editor (measured, 26 November).
