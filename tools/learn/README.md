# Karen's learning tools

Plain Python (standard library; `tools/rl/.venv` where PyTorch is needed) for Karen 0.4.0's data,
fits and model packs. The design is in the [0.4.0 plan](../../docs/you/plans/0.4.0-build-plan.md);
the run order is in [TESTING.md](../../docs/you/TESTING.md). Nothing here starts a training run.

| File | What it does |
|---|---|
| `dataset.py` | everyone's shifts, one folder per player (§6.3–6.4, C3.3) |
| `records.py` | reads shift records the same way for bots and for people |
| `legs.py` | legs: one walk from a stop to the next, with where it ended (§8.5, C4.3) |
| `fit_personas.py` | trait fitting: which persona plays like this person (§7.5, C3.4) |
| `fit_start.py` | the starting Ledger, "everyone" (§6.1, C3.2) |
| `scorecard.py` | the players' scorecard: how realistic the bots are (§7.7, C1.3) |
| `tricks.py` | her tricks, read from the game's own C# (`Decision/Tactics/*.cs`) |
| `export_brain.py` | a trained ML-Agents checkpoint into generated C# with a model card (§5.5, C5.7) |
| `pack.py` | the model pack: the general model's numbers in one signed file (§6.5, C3.7) |
| `publish.py` | the publish gate: a candidate pack goes out only if it beats the current one |
| `rsa_sign.py` | RSA signatures over SHA-256, in plain Python |
| `unity_meta.py` | `.meta` files for what the exporters write into `Assets/` |
| `make_brain_fixture.py`, `make_pack_fixture.py` | the fixtures for the C# parity and pack tests |

**Tests:** `python3 -m pytest tools/learn/tests` (or `tools/rl/.venv/bin/python -m pytest tools/learn/tests`).
