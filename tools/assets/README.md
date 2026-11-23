# Public and private project files

The public Kehai repository holds the code and docs. The private project repository holds the
scenes, art and Unity settings. The guard checks which files Git is about to commit and flags
paths listed in both repositories' indexes when the private `.git-assets` directory exists.

Run the guard directly from the project:

```sh
python3 tools/assets/check_public_index.py
```

To run it before every public commit, check `git config --get core.hooksPath`. If the repository
already uses a hook directory, add the script call to its existing pre-commit hook. Otherwise:

```sh
git config --local core.hooksPath .githooks
```

For example, a staged `Assets/Store/Fridge.png` stops the commit. Unstage it in the public
repository and add it to the private one. A staged `Assets/Store/Fridge.cs` and its `.meta` can
go in the public repository.

## Transfer game data before the PC link is ready

Use Python 3.9+ and GitHub CLI signed in to the same private repository on both machines.
On the Mac, name the data files or folders to include. Use a fresh release tag each time.

```sh
python3 -m tools.assets.data_transfer upload --repo YOUR-OWNER/YOUR-PRIVATE-REPO \
  --tag game-data-20261122 --include "ledger=/path/to/karen_ledger.json" \
  --include "records=/path/to/shift_records"
```

On Windows, use the same release tag and a new output folder.

```powershell
python -m tools.assets.data_transfer download --repo YOUR-OWNER/YOUR-PRIVATE-REPO --tag game-data-20261122 --destination C:\KehaiData\game-data-20261122
```

The upload packs each source under its label. The download verifies each file against the
archive manifest, then places the complete set in the new folder for use on the PC.
