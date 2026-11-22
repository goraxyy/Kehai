# Build notes: the shape

Every build session keeps a short note in the shared notes folder, updated at the end of each
step ([RULES.md](RULES.md), "Working within the usage limits"), so a new session can pick the
work up in minutes. Copy this, fill it in, keep it short.

```
## <date> <time>: <plan> <task ids>
- Branch: <branch> at <commit>; pushed as <remote branch> (PR <link>, draft or ready)
- Done this step: <tasks>, with how each was checked (compile, tests, which ones)
- Running now: <run id or "nothing">; the heavy-work lock: <free / held by pid N for "...">
- Left behind on disk: <clones, data folders, or "nothing">
- Next step: <task id and the first thing to do>
- Waiting on the owner: <question numbers or "nothing">
```
