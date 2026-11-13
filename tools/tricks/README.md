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

With the editor closed:

```bash
tools/tricks/record_tricks.sh
```

- It runs the **trick check** (one silent shift where each trick is forced in turn), then films
  fourteen seconds round each trick with the **replay renderer**. About ten minutes for the
  check and a minute or two a video.
- `--only fog,blackout` records just those. `--skip-check` reuses the last check's replay.
- To leave the real project alone, point `KEHAI_PROJECT` at an APFS clone
  ([HANDOVER §6](../../docs/agents/HANDOVER.md#6-how-to-work-on-it)) and copy the videos over.

Tricks the check doesn't force (the hunt, Cut, Throw, Ask the customers) have no video from this
script: they need her to see the player first. Record those from a played shift's replay with
`tools/marketing/render_shot.sh -krec <file> -from <s> -to <s> -shot chase -out
Assets/StreamingAssets/TrickVideos/<id>.mp4`.

The videos travel to the Windows PC with the rest of the art (the private `kehai-assets` repo).
