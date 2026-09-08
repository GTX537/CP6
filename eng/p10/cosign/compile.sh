#!/usr/bin/env bash
# Deterministic compiler only. Outputs are UNVERIFIED until build.sh checks their pinned hashes.
set -euo pipefail
umask 077

fail() { printf 'p10-cosign-compile %s\n' "$2" >&2; exit "$1"; }
[[ $# == 1 && "$1" == /* && ! -e "$1" && ! -L "$1" ]] || fail 64 output-path
output="$1"
profile="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
for name in go.mod go.sum lock-checksums.sha256; do
  [[ -f "$profile/$name" && ! -L "$profile/$name" ]] || fail 65 input-file
done
(cd -- "$profile" && sha256sum --check --strict lock-checksums.sha256) || fail 65 input-checksum
work="$(mktemp -d)"
mkdir -- "$work/tmp" "$work/module-cache" "$work/build-cache" "$work/gopath"

printf 'p10-cosign-compile stage=toolchain\n'
curl --silent --show-error --fail --location --proto '=https' --tlsv1.2 --retry 3 --connect-timeout 30 --max-time 300 \
  https://go.dev/dl/go1.26.8.linux-amd64.tar.gz --output "$work/go.tar.gz"
printf '%s  %s\n' d0f743b33e8d8945e6b1f432edd15785c70507121d6e2a723b21285eddf8b57b \
  "$work/go.tar.gz" | sha256sum --check --status
tar -xzf "$work/go.tar.gz" -C "$work"
curl --silent --show-error --fail --location --proto '=https' --tlsv1.2 --retry 3 --connect-timeout 30 --max-time 120 \
  https://github.com/jqlang/jq/releases/download/jq-1.8.2/jq-linux-amd64 --output "$work/jq"
printf '%s  %s\n' b1c22172dd303f3be49e935aa56aa48a8b7a46e0bc838b4997d3bb451495870f \
  "$work/jq" | sha256sum --check --status
chmod 0500 "$work/jq"

# No inherited Go flags, ambient credentials, VCS configuration or auto-selected toolchain.
build_env=(env -i PATH="$work/go/bin:/usr/bin:/bin" HOME="$work" TMPDIR="$work/tmp"
  GOPATH="$work/gopath" GOMODCACHE="$work/module-cache" GOCACHE="$work/build-cache"
  GOENV=off GOWORK=off GOTOOLCHAIN=local GOPROXY=https://proxy.golang.org GOSUMDB=sum.golang.org
  CGO_ENABLED=0 GOOS=linux GOARCH=amd64 GOAMD64=v1 GOMAXPROCS=2 GOMEMLIMIT=1200MiB)
[[ "$("${build_env[@]}" go version)" == 'go version go1.26.8 linux/amd64' ]] || fail 65 toolchain-version
cd -- "$work"
printf 'p10-cosign-compile stage=source\n'
"${build_env[@]}" go mod download -json github.com/sigstore/cosign/v3@v3.1.3 > source.json
"$work/jq" -e '.Path == "github.com/sigstore/cosign/v3" and .Version == "v3.1.3" and
  .Sum == "h1:001JQRI/PJ/5T+g/kJ1KTvKFbb322+fomc+pHDZ/6sg=" and
  .Origin.VCS == "git" and .Origin.URL == "https://github.com/sigstore/cosign" and
  .Origin.Hash == "11926fa5bbbbde47e88fc006b625a17769b743b2"' source.json > /dev/null
archive="$("$work/jq" -er .Zip source.json)"
source="$("$work/jq" -er .Dir source.json)"
[[ "$archive" == "$work/module-cache/"* && "$source" == "$work/module-cache/"* ]] || fail 65 source-path
printf '%s  %s\n' fbf05afe62db35ca00129ff65fab6f7ad8b7851ae1cf7dbb4b1789b6ccd65db2 \
  "$archive" | sha256sum --check --status
cp -R -- "$source" "$work/source"
chmod u+w "$work/source" "$work/source/go.mod" "$work/source/go.sum"
cp -- "$profile/go.mod" "$profile/go.sum" "$work/source/"
cd -- "$work/source"
printf 'p10-cosign-compile stage=modules\n'
"${build_env[@]}" go mod download
"${build_env[@]}" go mod verify
# Dependency download must not rewrite the reviewed locks.
cmp -- go.mod "$profile/go.mod"
cmp -- go.sum "$profile/go.sum"

ldflags='-buildid= -X sigs.k8s.io/release-utils/version.gitVersion=v3.1.3-cp6.1 -X sigs.k8s.io/release-utils/version.gitCommit=11926fa5bbbbde47e88fc006b625a17769b743b2 -X sigs.k8s.io/release-utils/version.gitTreeState=modified -X sigs.k8s.io/release-utils/version.buildDate=2026-08-05T23:43:27Z'
mkdir -- "$output"
printf 'p10-cosign-compile stage=linux\n'
"${build_env[@]}" go build -p=1 -mod=readonly -trimpath -buildvcs=false -buildmode=exe \
  -ldflags "$ldflags" -o "$output/cosign" ./cmd/cosign
printf 'p10-cosign-compile stage=windows\n'
"${build_env[@]}" GOOS=windows go build -p=1 -mod=readonly -trimpath -buildvcs=false -buildmode=exe \
  -ldflags "$ldflags" -o "$output/cosign-windows-amd64.exe" ./cmd/cosign
cd -- "$output"
"${build_env[@]}" go version -m cosign > cosign.buildinfo.txt
"${build_env[@]}" go version -m cosign-windows-amd64.exe > cosign-windows-amd64.exe.buildinfo.txt
sha256sum cosign cosign-windows-amd64.exe
printf 'p10-cosign-compile unverified-outputs; use build.sh for the pinned handoff\n'
