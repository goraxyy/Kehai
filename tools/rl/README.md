# Rung G: training Karen's tactic choice with PPO

Rung G is rung F with one change: when Karen's planner needs a tactic, a small neural network
trained with reinforcement learning picks it instead of the bandit. Everything else stays hand-built:

- the belief grid
- the goal chosen by utility
- the candidates the Director allows
- the HTN plan
- the tells
- the fairness rules

It sits on the ablation ladder ([`KAREN_RESULTS.md`](../../docs/KAREN_RESULTS.md)), so "does a
learned policy beat the bandit?" is answered the same way as every other rung: paired against F
on the same seeds.

**Where it stands:** the pipeline works end to end, but there is no trained result yet. Against
today's simulated players a policy has as little to learn as the bandit did: the bots ignore
Karen's tricks, so the Panic Index barely moves. Bots that react to her tells come before any
results.

| piece | where |
|---|---|
| the seam in the planner | `Assets/!_Project/_Game/AI/Scripts/Decision/Goals.cs` (`HtnPlanner.Plan`, `Shielded`) |
| what the policy sees, its mask and reward | `Decision/Policy/PolicyObservation.cs`, `Decision/Policy/TacticPolicy.cs` |
| the ML-Agents agent | `Decision/Policy/MlAgentsTacticPolicy.cs` (compiled only with `KAREN_MLAGENTS`) |
| setup and training builds | `Assets/!_Project/_Core/Editor/KehaiRl.cs` (Kehai → RL) |
| trainer config and scripts | this folder |

## Versions

These four must match: the trainer refuses a Unity package it doesn't speak to.

| Unity package | trainer (PyPI) | Python | PyTorch |
|---|---|---|---|
| `com.unity.ml-agents` 4.1.0 (brings `com.unity.ai.inference` 2.6.1) | `mlagents` 1.1.0 | 3.10.1–3.10.12 (3.10.12 recommended) | 2.1.1–2.8.0 |

## One-time setup

1. **Unity side.** With the project open: **Kehai → RL → Set Up ML-Agents**. It adds the package
   to `Packages/manifest.json` and, once the package is installed, the `KAREN_MLAGENTS` define.
   With the editor closed, run this instead:
   ```bash
   Unity -batchmode -nographics -projectPath . -executeMethod KehaiRl.SetUp
   ```
   Neither the package nor the define is in git, so each copy of the project needs this once.
   The same package also unlocks the blink CNN's in-engine path (`KAREN_SENTIS`).
2. **Python side.** Run `tools/rl/setup.sh` on macOS, or `tools\rl\setup.ps1` on Windows. Either
   one makes `tools/rl/.venv`. With [uv](https://docs.astral.sh/uv/) installed it fetches Python
   3.10.12 itself; otherwise it needs `python3.10` (macOS) or `py -3.10` (Windows).

## Build the training player

Training runs copies of the built game, not the editor. Use **Kehai → RL → Build Training
Player → macOS** (or **Windows**), or from the command line:

```bash
Unity -batchmode -nographics -projectPath . -executeMethod KehaiRl.BuildMacOS
Unity -batchmode -nographics -projectPath . -buildTarget Win64 -executeMethod KehaiRl.BuildWindows
```

The players go to `Builds/KehaiTrain/macOS/` and `Builds/KehaiTrain/Windows/`. The Windows one
is also zipped as `Builds/KehaiTrain/Windows.zip`.

## Record demonstrations

Before PPO starts, the policy learns to copy rung F's bandit (`behavioral_cloning` in
`karen_ppo.yaml`), so it starts at F's level instead of picking at random. Record about five
minutes of the bandit's choices:

```bash
tools/rl/record_demos.sh 2000        # 2000 decisions → tools/rl/demos/Karen.demo
```

## Train

```bash
tools/rl/train.sh karen_g1           # 2 copies of the game, karen_ppo.yaml
tools/rl/train.sh karen_g1 2 tools/rl/karen_ppo.yaml --resume
```

- Each copy plays the ablation's shifts 4–7 against the three simulated players in turn, with
  rung G and no shift records written. Every shift is one episode.
- Each copy gets its own seeds and its own `kehai_eval/worker_<port>/` folder.
- The copies run in batch mode, so they are silent and open no window.
- **On the 8 GB Mac, close the Unity editor first.** Two copies are the most it has room for.
- Watch progress with `.venv/bin/tensorboard --logdir tools/rl/results`.
- When training ends, the model is at `results/<run-id>/Karen.onnx`.

**On a Windows machine** (for example a university one):

1. Copy `tools/rl` and `Windows.zip` to the machine and unzip the zip.
2. Run `powershell -ExecutionPolicy Bypass -File setup.ps1`.
3. If there's no Python and no admin rights, install uv per user first:
   `powershell -ExecutionPolicy Bypass -c "irm https://astral.sh/uv/install.ps1 | iex"`.
4. Start training:
   ```powershell
   powershell -ExecutionPolicy Bypass -File train.ps1 -Run karen_g1 -Envs 8 -Player C:\path\to\Windows\Kehai
   ```

`-Player` is the training player without `.exe`. One copy per two CPU cores is a good start, and
a GPU doesn't help a network this small. Bring `results/` back to the Mac.

**Check it works** before a long run:

```bash
tools/rl/train.sh smoke 1 tools/rl/smoke.yaml --force
```

This runs a few hundred steps on a tiny network and should end with `results/smoke/Karen.onnx`.

## Use a trained model

1. Copy it to `Assets/!_Project/_Game/AI/Resources/KarenPolicy/karen.onnx`. That folder is
   gitignored, because models are personal.
2. Run rung G against F on the same shifts:
   ```bash
   Unity -batchmode -nographics -projectPath . -executeMethod EvalBatch.Play -kehai-ablation -ablation-rungs FG \
         -ablation-careers 3 -ablation-shifts 4 -ablation-start 4
   ```
   In a build or in play mode, use `-karen-rung G` instead. To run a model under another name in
   that folder, add `-karen-policy <name>`.

Where rung G's choice comes from:

| situation | what picks the tactic |
|---|---|
| a trainer is connected | the policy being trained |
| a model is in `KarenPolicy/` | that model, taking its best guess |
| neither | the bandit, as on rung F |
| `KAREN_MLAGENTS` isn't defined | the bandit (the log says so once) |

The thought log keeps working. A PLAN line reads, for example, `policy: the bandit would have
picked follow (bandit 0.41)`. ML-Agents doesn't expose the policy's probabilities, so the log
shows what it chose, what the bandit would have chosen, and the legal alternatives, but not
how sure it was.

## The shield, and looking for loopholes

By default (`-karen-shield on`) the action mask lets the policy pick only what the Director
allows: unlocked tiers, tactics off cooldown, within the tension budget. `-karen-shield off`
drops those three gates and keeps only what can physically run. That is the experiment: what
does a trained policy do when the pacing rules aren't enforced?

`FairnessGuard.CheckPacing` counts every tactic above tier 1 started while the employee is
settling in or recovering from a chase (Karen.md §9, rule 4). It runs on every rung. With the
shield on it must stay at zero; with it off, it measures what slipped through. Each violation
also costs the policy 0.1 reward.

## The reward, and how it could be gamed

At every Director tick the policy earns `−|Panic − Setpoint| · dt / shift length`. That is the
Director's own job (Karen.md §7.4): hold the employee's panic near the target curve. A whole
shift sums to between about −1 and 0. RL optimises the number, not the intent, so these are the
loopholes to look for in the unshielded runs and the thought logs:

- **Cornering.** Freezing counts towards the Panic Index, so trapping the player can score
  without frightening anyone.
- **Route blocking.** A long way round counts as path inefficiency.
- **Coasting.** Doing as little as possible while the setpoint is low (settling in, recovery).
- **Bot exploits.** A move that reliably steers a scripted bot does nothing to a person. This is
  why the held-out profile and real players matter before any claim.

## What it costs

- **Decisions:** a decision happens only when a plan needs a tactic for a goal that has
  learnable tactics. That's a few dozen per 150-second shift.
- **Speed:** a headless shift takes 5–15 s.
- **Per night:** two copies on the Mac manage a few hundred thousand decisions overnight. Eight
  copies on a bigger Windows machine manage about four times that.
