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
