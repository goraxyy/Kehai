#!/bin/sh
# Make git run tools/git/guard.py before every commit to the public code repo (0.5.2, C7.2).
# It sets core.hooksPath for this repo, which every worktree of it shares. Run it once:
#   tools/git/install-hooks.sh
# To undo: git config --unset core.hooksPath
set -eu
root="$(git rev-parse --show-toplevel)"
chmod +x "$root/tools/git/hooks/pre-commit"
git config core.hooksPath tools/git/hooks
echo "install-hooks: commits in $(git rev-parse --git-common-dir | sed 's|/.git$||') now refuse art (tools/git/guard.py)"
