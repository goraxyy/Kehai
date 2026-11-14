# External storage and future project size

Planning estimate, 14 November. No project, art or data is moved by this page.

## The proposed purchase

[Amazon ASIN B0H7WVWGFV](https://www.amazon.com/dp/B0H7WVWGFV) lists 128 GB and USB-A/USB-C
connectors, but explicitly says **USB 2.0**. No measured sustained or small-file performance
was found on the listing. A USB-C plug describes the connector, not the transfer speed.

USB 2.0 has a theoretical ceiling of 480 megabits/s = 60 megabytes/s, before overhead; the drive
itself can be much slower. See [Kingston's flash memory guide](https://media.kingston.com/pdfs/MKF-283.3-Flash-Memory-Guide_EN.pdf).
For illustration only, copying 20 GB at 10 MB/s takes about 33 minutes; at 30 MB/s about 11 minutes.
Those are arithmetic examples, not measurements of this product. Unity's many small cache files
can take considerably longer than one large archive of the same total size.

**Verdict:** enough capacity for today's project and portable archives; not recommended as the
active Unity project drive. Prefer a **500 GB or 1 TB external SSD with USB 10 Gb/s**, rather than
this USB 2.0 stick. An SSD is a storage drive designed for frequent fast access; Kehai's editor
uses it for its assets and cache, like the internal SSD in a laptop. For a concrete specification
reference, [Samsung's T7 support page](https://www.samsung.com/us/support/answer/ANS10002047/)
lists up to 1,050/1,000 MB/s sequential read/write. That is an advertised ceiling, not a promise
for Unity's small files. Match the cable and Mac port; no need to buy a premium 20/40 Gb/s drive
solely for this project.

128 GB is about 119 GiB before formatting. Reserve working space rather than planning to fill it.
The drive adds disk capacity; it does not increase the Mac's 8 GB RAM.

## Measured now versus estimates

Measured without running Unity:

| Item | Size now |
|---|---:|
| Main project's `Assets` | 161 MB |
| Main project's `Library` cache | 4.2 GB |
| Stored builds | 578 MB |
| RL tools/environment folder | 662 MB |
| Local evaluation data | 3.3 GB |
| Unity Editor installation (shared by projects) | 11 GB |
| Free Mac disk at this check | approximately 12 GiB (fluctuates; previously about 8 GiB) |

The three **editions** share one gameplay project and its source assets. They do not require
three copies of the Unity project. Retaining builds duplicates their included assets; keeping
both Mac and Windows outputs adds more. The separate visual project is a second editor project,
with its own cache and selected assets.

**Working budgets, not forecasts of final download size:** assumes a four-shift store, modest
textures, shared low-poly products, selective art imports, and only a few retained build versions.

| Stage / use | Disk budget | What can make it larger |
|---|---:|---|
| Today's gameplay project + small initial visual project, caches and a few builds | 10–20 GB | fresh imports / platform caches |
| Art-bearing gameplay project, cache, separate visual project and recent edition builds | **30–80 GB** | materials/textures, per-layout lighting data, duplicated art/caches |
| Later development with more polished art, source files and more retained builds | **60–150+ GB** | source textures/audio, imported asset packs, multiple platforms |
| Training recordings, checkpoints and marketing video | additional; budget on Windows separately | no fixed bound without retention; video can exceed the game project |

A 128 GB stick may hold the middle budget but leaves limited growth room and is still slow.
A 500 GB SSD is a reasonable working target; 1 TB gives more room for captures and archives.
Neither estimate promises the finished game's install size: source files and editor caches do
not ship. Measure each edition's first real build before publishing download/storage requirements.
The earlier 7–13 GB estimate was a small second-project estimate, not a lifetime budget.

## Do files have to be copied back every time?

**No, if the complete project lives on the external drive.** Unity can open that folder directly.

1. With Unity closed, copy the complete project to the drive once, including `Assets` and its
   `.meta` files, `Packages`, `ProjectSettings`, and needed source/code. A scene alone lacks its
   models, materials and settings. These private files never enter the public Git repository.
2. Use Unity Hub → **Add project from disk** → select the project root on the external drive.
   See [Unity Hub project management](https://docs.unity.com/en-us/hub/project-manage).
3. Open and save there. The drive must stay attached throughout editing and until Unity exits.
   There is no upload/download step each time.
4. If the cache isn't copied, Unity recreates it at the new project location on first import;
   that costs time and disk space. Don't discard caches every session to save transfer time.
5. Keep separate Mac and Windows working caches. Exchange source/asset snapshots using the
   established code repository and private `kehai-assets` releases, not one shared cache.
6. If the stick is only an archive, then yes: copy a project to the Mac to work, and copy changed
   source/private assets back afterwards. That uses internal disk again and is not a solution to
   insufficient working space. Keep versioned backups, not a single overwritten copy.

For a Mac working SSD, APFS is the default recommendation. For file exchange with Windows,
exFAT is compatible with both; use a separate exchange volume/drive or network transfer as needed.
[Apple's filesystem guide](https://support.apple.com/guide/disk-utility/file-system-formats-dsku19ed921c/mac)
explains the formats. Formatting erases data: buying a drive is not permission to format or move
anything. Retain a second copy before migrating; a flash drive is not the only backup.

## Chosen separate visual project

Item 8 option B is selected: a separate Look Lab project, stored externally once suitable storage
is available. Keep the same Unity/URP version, a small curated fixture set, one representative
room slice at a time, and a written/code preset of each lighting experiment. Transfer approved
source settings and private art back into the gameplay project; don't copy the visual project's
entire Library or blindly overwrite gameplay Packages/ProjectSettings.

If the owner buys only the linked stick, use it for archives/transfers and defer heavy visual
project work to the Windows SSD. No asset modelling, new Unity project, imports, file movement,
cleanup or benchmarking is authorised yet. Documentation work may continue.

## Second candidate: 512 GB Vackiit phone flash drive

The owner also asked about [Amazon B0HKR677TQ](https://www.amazon.com/dp/B0HKR677TQ).
Read directly from its Amazon listing on 14 November: Vackiit, advertised 512 GB, USB 3.0,
$29.99 at this check, and an advertised 800 MB/s read value. Its bullets describe roughly three
minutes per GB on a phone and a 1 GB per-transfer recommendation specifically for phone-to-stick
copies. Do not interpret that phone limitation as a proven PC limitation or benchmark.

**Capacity:** 512 GB (about 477 GiB before formatting) comfortably covers the planning budgets
above. **Speed/reliability:** no independent sustained-write or small-file benchmark was verified;
USB 3.0 and the claimed read value do not establish fast Unity cache access. The phone-focused
claims and unverified speed are insufficient to recommend this as the sole active project drive.
MFi marketing concerns Apple accessory compatibility, not a benchmark of editor workloads.
Use a reputable external SSD for active work; this remains a possible transfer/archive device,
subject to checking real capacity and write/read integrity after purchase. No product is bought,
formatted or tested here, and no evidence establishes that this particular item is counterfeit.

## Estimated player install size, separate from development storage

Assumptions: four shifts reuse the same store assets, six layouts mainly change placement data,
products stay simple, moderate-resolution compressed textures, and no large pre-rendered movies.
These are budgets for **one platform**, not measured future builds or promises of Steam download size.

| Edition / stage | Estimated installed files | Excludes |
|---|---:|---|
| Training player | 0.2–0.5 GB | Python/trainer environment, datasets, checkpoints |
| Quick-pass Testing build | 1–3 GB as art is added | accumulated tester replays and queued uploads |
| Art-complete Gameplay build | **2–6 GB working target** | saves/records, patch working space |
| Higher-resolution art/audio or cinematic-heavy case | 8–15+ GB possible | still not a committed release size |

Today retained old player folders are much smaller (Windows 143 MB, macOS 158 MB; not current
0.5.0 builds). Future textures, audio and per-layout lighting can dominate; ML inference weights
are expected to be minor beside art. Six layouts need not duplicate six complete sets of shelf
and product assets. Compressing a download may reduce size, but no ratio is promised in advance.
Players normally install only Gameplay, not all three editions or the Unity Editor/Library.

Comparable Steam pages list these **free-space requirements**, not measured downloads:

| Indie horror game | Published free space |
|---|---:|
| [The Convenience Store](https://store.steampowered.com/app/1228520/) | 2 GB |
| [MiSide](https://store.steampowered.com/app/2527500/MiSide/) | 2 GB |
| [Mouthwashing](https://store.steampowered.com/app/2475490/Mouthwashing/) | 3 GB |

Sources checked 14 November 2026. Kehai's 2–6 GB target is a planning inference from its scope,
not a claim that these games have identical assets or compression. Measure the first art-bearing
build using Unity's build report before setting a storefront storage requirement.
