# The itch.io playtest page

A private page testers download from. You make it; this is what to put in it.

## Settings

- **Dashboard → Create new project.**
- **Title:** Kehai (playtest) · **Project URL:** `kehai-playtest`
- **Kind of project:** Downloadable · **Classification:** Games
- **Release status:** Prototype · **Pricing:** No payments
- **Uploads:** the zips from `Builds/playtest-<round>/`:
  - `Kehai-playtest-<round>-macOS.zip`, ticked **macOS**;
  - `Kehai-playtest-<round>-Windows.zip`, ticked **Windows**.
  A new round replaces both files (delete the old ones), so testers always get the current build.
- **Visibility & access:** **Restricted**, with a password. Put the URL and the password in
  `tools/playtest/.env` (`KEHAI_PLAYTEST_ITCH_URL`, `KEHAI_PLAYTEST_ITCH_PASSWORD`) so the
  tester messages carry them.
- **Comments:** off (feedback goes through the game and the form).
- **Genre:** Simulation · **Tags:** horror, stealth, ai, first-person, simulation.

## Short description

> A night shift in a convenience store. The management AI learns your habits.

## Page text

> **Thank you for playtesting Kehai.**
>
> You work the night shift in a 24-hour convenience store: clock in, stock the shelves, mop the
> spills, take out the bins, serve the customers. Karen, the store's management AI, is watching.
> She only knows what she can see and hear, and she remembers how you work.
>
> **How to play this test**
> - Download the build for your computer, unzip it, and open it. *Mac:* if macOS won't open it,
>   go to System Settings → Privacy & Security → Open Anyway. *Windows:* if SmartScreen
>   appears, click More info → Run anyway.
> - Type the tester code you were sent.
> - Play up to three shifts, about 20–30 minutes. Headphones help.
> - Don't look anything up first: not knowing what to do is part of what's being tested.
> - Shift+F7 marks a bug and lets you write one line about it.
> - At the end: four quick questions in the game, and a link to a few more.
>
> **What gets sent:** with your OK, the game sends what happened in it (where you went, what
> you pressed, what Karen did, how smoothly it ran) so the developer can watch your session as a
> 3D replay. Never your camera, microphone or screen.
>
> Keyboard and mouse. macOS (Apple silicon and Intel) and Windows 10/11 (64-bit).
