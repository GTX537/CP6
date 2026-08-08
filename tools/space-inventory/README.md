# CP6 Space E00-S01 inventory

This tool generates the repeatable current-facts inventory required by
`E00-S01`. It compares the frozen baseline commit with the uncommitted
`space-volume1` candidate without merging or modifying either source.

## Run

From the clean E00 worktree:

```powershell
python tools/space-inventory/space_inventory.py `
  --repo-root . `
  --candidate-root ../space-volume1
```

Verify that committed generated files match current inputs:

```powershell
python tools/space-inventory/space_inventory.py `
  --repo-root . `
  --candidate-root ../space-volume1 `
  --check
```

Run the scanner unit tests:

```powershell
python -m unittest discover -s tools/space-inventory -p "test_*.py"
```

## Outputs

- `docs/space/reports/e00-s01-current-facts.json`
- `docs/space/reports/e00-s01-current-facts.md`

The JSON file is the complete machine-readable inventory. The Markdown file is
the review report. Both are deterministic: the scanner emits no timestamps or
absolute paths, sorts every collection, and records a SHA-256 digest of its
inputs.

## Safety

- The scanner is read-only except for its two generated output files.
- Baseline paths come from `git ls-files` and their content is read from the
  recorded `HEAD` commit, so later uncommitted implementation work cannot
  silently rewrite the E00-S01 baseline. Candidate inputs come from
  `git status --porcelain`.
- Build outputs and ignored files are excluded by Git.
- Reports contain repository-relative paths, route names, symbols, and status
  only. They never contain source bodies, environment values, credentials, or
  customer data.
- A Git failure, unreadable UTF-8 source, missing candidate file, stale report,
  or duplicate backend endpoint makes the command fail.

Rollback is deletion of the two generated report files and this tool directory.
