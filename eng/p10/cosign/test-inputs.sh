#!/usr/bin/env bash
# Real shell boundary tests. Run without network; no compiler or signing key is required.
set -euo pipefail
profile="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
scratch="$(mktemp -d)"
count=0

expect_failure() {
  local expected="$1" marker="$2" actual
  shift 2
  set +e
  "$@" > "$scratch/result.log" 2>&1
  actual=$?
  set -e
  [[ "$actual" == "$expected" ]] || { printf 'wrong exit: expected=%s actual=%s\n' "$expected" "$actual" >&2; exit 1; }
  grep -Fq -- "$marker" "$scratch/result.log"
  count=$((count + 1))
}

mkdir "$scratch/existing"
printf 'must remain unchanged\n' > "$scratch/existing/marker"
ln -s "$scratch/existing" "$scratch/link"
for entry in build.sh compile.sh; do
  expect_failure 64 output-path bash "$profile/$entry"
  expect_failure 64 output-path bash "$profile/$entry" relative-output
  expect_failure 64 output-path bash "$profile/$entry" "$scratch/existing"
  expect_failure 64 output-path bash "$profile/$entry" "$scratch/link"
done
[[ "$(< "$scratch/existing/marker")" == 'must remain unchanged' ]]

mkdir "$scratch/changed-lock"
cp "$profile/compile.sh" "$profile/go.mod" "$profile/go.sum" "$profile/lock-checksums.sha256" "$scratch/changed-lock/"
printf '\n// invalid unreviewed change\n' >> "$scratch/changed-lock/go.mod"
expect_failure 65 input-checksum bash "$scratch/changed-lock/compile.sh" "$scratch/never-created"
[[ ! -e "$scratch/never-created" ]]

mkdir "$scratch/link-lock"
cp "$profile/compile.sh" "$profile/go.sum" "$profile/lock-checksums.sha256" "$scratch/link-lock/"
ln -s "$profile/go.mod" "$scratch/link-lock/go.mod"
expect_failure 65 input-file bash "$scratch/link-lock/compile.sh" "$scratch/never-created"
[[ ! -e "$scratch/never-created" ]]
printf 'p10-cosign-input-tests passed=%s network=none\n' "$count"
