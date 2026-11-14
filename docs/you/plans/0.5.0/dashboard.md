# One page for everything

Part of the [Kehai 0.5.0 plan](../0.5.0-plan.md), item 9. **Discuss first:** the tasks wait on
Q32–Q34.

1. [The options](#1-the-options)
2. [Where every number comes from](#2-where-every-number-comes-from)
3. [How it stays current](#3-how-it-stays-current)
4. [What stays private](#4-what-stays-private)

---

## 1. The options

| | A. A private claude.ai page | B. GitHub Projects | C. A page made on this Mac | **A + B (my pick)** |
|---|---|---|---|---|
| **What** | a web page only you can open, with a small database of its own; agents write the day's numbers to it | GitHub's board for issues and PRs, with charts of work over time [1] | an HTML file a nightly job writes to the Mac | the page for the summary and growth; the board for PRs and bugs |
| **Private?** | yes, until you share it | a project can be private, but this repo is public | yes | yes |
| **On your phone?** | yes | yes | no | yes |
| **Views, likes, training curves?** | yes | no: it holds issues and PRs only | yes | yes |
| **Who updates it** | a scheduled job (§3) | GitHub, as PRs and issues change | the Mac, when it's on | both |
| **Cost** | your plan | free | free | — |
| **Cons** | needs the daily job to keep it fresh | can't hold the numbers | only on the Mac, and only when it's awake | two places, linked both ways |

## 2. Where every number comes from

"Today" says whether the number exists anywhere yet.

### Release

| Number | Source | Today |
|---|---|---|
| Milestones and their exit tests | the tick boxes in [`RELEASE_PLAN.md`](../../RELEASE_PLAN.md) | yes |
| Plot | a plot page in `docs/you/`, once one exists | no |
| Assets: models made, still placeholder | Milestone 7's tick boxes; the releases of the private `kehai-assets` repo | partly |
| Scripts | lines and files of C# per area, counted from git | yes |
| Bugs | GitHub issues labelled `bug`, open and closed | the label doesn't exist yet |

### Builds

| Number | Source | Today |
|---|---|---|
| PRs open, merged this week | GitHub | yes |
| Fixes | merged commits and PRs labelled `fix` | partly |
| Tests passing | the EditMode suite's `results.xml` from the last clone run; `pytest tools/marketing` | yes, by hand |
| The last build of each version, and its commit | each build's `build.txt` | yes, for Mac and Windows |
| Plans' progress | the tick boxes of every plan in `docs/you/plans/` | yes |

### Training

| Number | Source | Today |
|---|---|---|
| Runs in progress, done, and waiting for your go | the run register (`runs.jsonl`, Phase 5), the heavy-work lock's holder, the "your go" tasks in each plan | no (the register is Phase 5) |
| Which computer ran what | the run register's computer field; the Windows PC sends its lines (§3) | no |
| The model's changes and progress | the model cards beside each generated file; the model pack's versions; [`KAREN_RESULTS.md`](../../../agents/results/KAREN_RESULTS.md) | partly |
| Real players' reports and feedback | playtest v2's cloud database and reports, once PR #81's build exists | no |
| Training bugs | issues labelled `training` | no |

### Marketing

| Number | Source | Today |
|---|---|---|
| Recent videos, and what's planned | the marketing pipeline's own records (drafts, approvals, the Showrunner's plan in the [marketing agents plan](../marketing-agents.md)) | partly |
| Views, likes, subscribers | the YouTube Data API's statistics for the channel and each video [2]; Buffer for the other channels | no: the weekly report says these join "when the YouTube key and Buffer are set up" |
| Wishlists | Steamworks' wishlist reports, once the Steam page is live [3]. **Not confirmed from Valve's own docs:** an API for wishlist data, reported by third parties | no Steam page yet |

### Growth

Each day's numbers are kept in the page's database, so every chart is a line over time: views,
subscribers, wishlists, players, tests passing, plans' progress.

## 3. How it stays current

```
 every night                                                    whenever you look
 collectors (plain scripts, tools/status/) ──► today's numbers ──► the page's database ──► the page
   git, GitHub, plans, test results,              (JSON, never         │
   run register, marketing records,               in git)              └─► a short "what changed"
   YouTube, Buffer, Steamworks                                              written by Haiku 5.5
 the Windows PC ──► its run-register lines ──┘
```

- **Counting needs no model:** the collectors are plain scripts. A small model (Haiku 5.5) only
  writes the few sentences of "what changed this week".
- **Where the job runs:** Railway, where the marketing posting already runs (always on), or the
  Mac's scheduler (only when it's awake). The Windows PC sends its run lines with a write-only
  key of its own.
- **GitHub Projects** updates itself as PRs and issues move.

## 4. What stays private

- **The repo is public:** no views, likes, subscriber counts, player data or keys go into git.
  The collectors live in git; what they collect doesn't.
- **Keys** stay where they are today (the marketing `.env` on the Mac, Railway's settings) plus
  one write-only key on the Windows PC.
- **The page is private by default.** A cleaned-up public view for interviews can come later
  (Q33).

**Sources.**
[1] GitHub Docs, "About insights for Projects". <https://docs.github.com/en/issues/planning-and-tracking-with-projects/viewing-insights-from-your-project/about-insights-for-projects>
[2] Google, YouTube Data API: channels (statistics: views, subscribers). <https://developers.google.com/youtube/v3/docs/channels>
[3] Steamworks Documentation, Wishlist reporting. <https://partner.steamgames.com/doc/marketing/wishlist/reporting>
