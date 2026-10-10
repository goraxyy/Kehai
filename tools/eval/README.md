# Kehai as an agent eval

The store is a long-horizon, interruption-heavy environment with an adversary (`IDEAS.md` §1).
These tools drive it over a local socket; the environment itself lives in
`Assets/!_Project/_Game/Eval/`.

## Start the environment

- **Editor:** Kehai → Eval → Start Env Server (port 5555). Enters play mode if needed.
- **Headless build:** Kehai → Eval → Build Eval Player (macOS), then
  ```bash
  Builds/KehaiEval/Kehai.app/Contents/MacOS/Kehai -batchmode -nographics -kehai-env 5555
  ```
- **Headless editor:** `Unity -batchmode -nographics -projectPath . -executeMethod EvalBatch.Play -kehai-env 5555`

Resets reload the store with a fixed seed and a fixed time step (`1/fps` per frame), and headless
runs go faster than real time (a 150 s shift takes 5–15 s). The first shift after launch replays
identically for the same seed and actions; later shifts in one session drift a little (engine-side
threading in NavMesh carving and crowd updates), so compare configurations on paired seeds and
averages, or restart the game per episode when you need an exact replay.
Eval runs keep Karen's Ledger in `kehai_eval/eval_ledger.json` under the game's persistent data
folder — never the player's own.

## Clients

```bash
python3 run_baseline.py --episodes 3 --rung F        # scripted floor to beat
python3 llm_agent.py --episodes 1 --rung F           # Claude plays (pip install anthropic)
```

`kehai_env.py` is the client (standard library only): `reset`, `step(verb, target, …)`,
`observe`, `map`, `metrics`. Observations come as data (`obs`) and as prose (`text`).

| verb | target |
|---|---|
| `move_to` | a place name from the map, a landmark (`checkout_2`, `time_clock`, `coffee_machine`), or an id |
| `pick_up` | `mop`, `crate`, `flashlight`, `bag_n` |
| `restock` / `mop` / `serve` / `help` / `bag_trash` | `bay_n` / `spill_n` / `cust_n` / `cust_n` / `bin_n` |
| `dispose`, `drink_coffee`, `clock_in`, `clock_out`, `drop`, `toggle_flashlight` | — |
| `flip_breaker` | `breaker_n` (the observation gives each one's hum pitch rank) |
| `wait` | `seconds` |

## The agent's body

Actions run through the real player: `AgentDriver` steers `PlayerMotor` along NavMesh paths and
presses things through `PlayerInteract`, so walls, doors, stamina, noise and the time a mop takes
are the player's. Three small helps stand in for what a person does without thinking: a closed
door is opened on reaching it (pressing E), a shopper standing in the way is stepped round, and the
customer being escorted doesn't physically block the guide. An action that stays wedged fails with
a message rather than burning the shift.

## What comes back

Per episode: completion, work done by kind, queue waits, customers lost, energy, Karen's
detections/catches/tactics, the Panic Index, and a **failure taxonomy** — starvation,
thrashing, interference blindness, clock blindness, resource mismanagement, spurious
completion — computed from the action trace.

## The ablation

`Kehai → Eval → Run Quick/Full Ablation`, or headless:

```bash
Unity -batchmode -nographics -projectPath . -executeMethod EvalBatch.Play -kehai-ablation \
      -ablation-careers 2 -ablation-shifts 4 -ablation-seconds 150 -eval-timeout-min 120
```

runs the three simulated players (efficient, skittish, reckless) against Karen rungs A–F and
writes `ablation_<time>.jsonl` and a markdown table. `KAREN_RESULTS.md` holds the latest run.
