# Kehai's documents

The repository's markdown files, by what they're for. The project [`README`](../README.md) and the
[`CHANGELOG`](../CHANGELOG.md) stay at the top level; each tool's README lives with its tool.

## Design: how the game works

| document | what it covers |
|---|---|
| [`Aiko.md`](design/Aiko.md) | Aiko, the adaptive antagonist: her three minds, what she perceives, her tactics, how she learns |
| [`STORE_CATALOG.md`](design/STORE_CATALOG.md) | the 80 products, their packaging, the aisles they're sold in, what customers say |
| [`MERCHANDISING.md`](design/MERCHANDISING.md) | how the shelves are stocked: the route, eye/waist/stoop, brand blocks, end caps, the shelf grid, every aisle's layouts |
| [`STORE_MAP.md`](design/STORE_MAP.md) | the map of the building the game builds from its NavMesh (written by Kehai/Map/Export) |
| [`CONTROLS.md`](design/CONTROLS.md) | every key and what it does |
| [`IDEAS.md`](design/IDEAS.md) | the research notes and ideas behind the evaluation and learning work |

## Production: getting it made and out

| document | what it covers |
|---|---|
| [`RELEASE_PLAN.md`](production/RELEASE_PLAN.md) | the milestones to a Steam release |
| [`MARKETING.md`](production/MARKETING.md) | the devlog and early-player plan |
| [`PLAYTEST.md`](production/PLAYTEST.md) | how playtests run: the build, consent, the upload, the reports |

## Engineering: how it's built

| document | what it covers |
|---|---|
| [`DECISIONS.md`](engineering/DECISIONS.md) | the heavy decisions and why: the repository, measuring, rendering, shelves as data, the endless maze, Aiko's fairness, replays, the pipelines, and what's still open |

## Results

| document | what it covers |
|---|---|
| [`AIKO_RESULTS.md`](results/AIKO_RESULTS.md) | what the evaluation runs found about Aiko |

## With the tools

| document | what it covers |
|---|---|
| [`tools/eval/README.md`](../tools/eval/README.md) | running Aiko's evaluation and the training environment |
| [`tools/blink/README.md`](../tools/blink/README.md) | the webcam blink helper |
| [`tools/marketing/README.md`](../tools/marketing/README.md) | the marketing pipeline, step by step; its plan is [`BUILD_PLAN.md`](../tools/marketing/BUILD_PLAN.md) |
| [`tools/playtest/README.md`](../tools/playtest/README.md) | the playtest upload service; the itch page and tester message are beside it |
