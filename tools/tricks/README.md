# Trick videos

The Esc menu's **TRICKS** tab shows what Karen can do: one entry a trick, with three plain lines
(what she does, how you notice, what you can do) and a short video. The words are in code
([`TrickGuide.cs`](../../Assets/!_Project/_Game/AI/Scripts/Diagnostics/TrickGuide.cs)). The videos
are recorded from the game by this folder's script.

| What | Where |
|---|---|
| The words | `TrickGuide.All` (C#, in git) |
| The videos | `Assets/StreamingAssets/TrickVideos/<trick id>.mp4` (never in git; `.gitignore`) |
| How the game finds one | `TrickGuide.VideoPath(id)`: that folder first, then `<game data>/TrickVideos/` |
| Without a video | the entry shows its words and "No video of this one yet." |

## Recording them

After 0.5.2 merges, with the editor closed:

```bash
tools/tricks/record_tricks.sh
```

- The **trick check** runs in a `kehai-run` clone pinned to the script checkout's commit.
  Its log, reports and replays stay together under `~/TokenLimit/runs/out/<run id>/`. The
  replay renderer uses those retained files to film fourteen seconds per trick.
- `--only fog,blackout` selects tricks. `--seed 4242` sets the check seed. `--skip-check` uses
  the newest available check from the retained runs or the project's legacy data folder.
  Set `KEHAI_DATA` to select one specific data folder with `--skip-check`.
- `--dry-run` prints the check command. With `--skip-check` it looks up the replays and prints
  the render commands. Both forms run without Unity or video output.
- `KEHAI_PROJECT` selects the source project, the renderer's project, and the video's
  destination. Point it at an APFS clone to keep the videos separate, then copy them over.
  [HANDOVER §6](../../docs/agents/HANDOVER.md#6-how-to-work-on-it)

Tricks the check doesn't force (the hunt, Cut, Throw, Ask the customers) have no video from this
script: they need her to see the player first. Record those from a played shift's replay with
`tools/marketing/render_shot.sh -krec <file> -from <s> -to <s> -shot chase -out
Assets/StreamingAssets/TrickVideos/<id>.mp4`.

The videos travel to the Windows PC with the rest of the art (the private `kehai-assets` repo).
