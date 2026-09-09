#!/usr/bin/env bash
# Policy entry point: only hand off the reviewed, byte-identical security derivative.
set -euo pipefail
umask 077

fail() { printf 'p10-cosign-build %s\n' "$2" >&2; exit "$1"; }
[[ $# == 1 && "$1" == /* && ! -e "$1" && ! -L "$1" ]] || fail 64 output-path
output="$1"
profile="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
for name in compile.sh go.mod go.sum lock-checksums.sha256 checksums.sha256 UPSTREAM-LICENSE NOTICE; do
  [[ -f "$profile/$name" && ! -L "$profile/$name" ]] || fail 65 input-file
done
(cd -- "$profile" && sha256sum --check --strict lock-checksums.sha256) || fail 65 input-checksum

work="$(mktemp -d)"
env -i PATH=/usr/bin:/bin HOME="$work" TMPDIR="$work" \
  bash "$profile/compile.sh" "$work/unverified"
(cd -- "$work/unverified" && sha256sum --check --strict "$profile/checksums.sha256") || fail 65 output-checksum

# Never overwrite a previous handoff, even if the path was created while compiling.
mkdir -- "$output"
for name in cosign cosign-windows-amd64.exe cosign.buildinfo.txt cosign-windows-amd64.exe.buildinfo.txt; do
  cp -- "$work/unverified/$name" "$output/$name"
done
cp -- "$profile/UPSTREAM-LICENSE" "$output/cosign.LICENSE"
cp -- "$profile/NOTICE" "$output/cosign.NOTICE"
chmod 0555 "$output/cosign" "$output/cosign-windows-amd64.exe"
printf 'p10-cosign-build verified 3.1.3-cp6.1\n'
