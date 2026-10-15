# WP02-02 source compatibility preparation

This runner tests fixed source snapshots. Formal package trust remains **BLOCKED**;
Main consumption remains **NOT_RUN**. It does not start Main Program or the database
compatibility executable, use a database, publish packages, or invoke workflows.

The immutable input contract is `input-manifest.json`; `verify-plan` compares every
field with the authorized constants, including the four original package references.
`input.schema.json` provides the equivalent JSON Schema contract.

```powershell
$python = 'C:/Users/tt/AppData/Roaming/uv/python/cpython-3.13-windows-x86_64-none/python.exe'
$runner = 'eng/wp02-02/consumption-compatibility/run.py'
$owner = [guid]::NewGuid().ToString('N')
$output = Join-Path (Get-Location) "artifacts/wp02-02/consumption-compatibility/$owner"
& $python -B -X utf8 $runner --phase verify-plan
& $python -B -X utf8 $runner --phase prepare --owner $owner --output $output --main-repo (Get-Location) --platform-repo D:/CP6.Platform-worktrees/wave6-integration-20261014
& $python -B -X utf8 $runner --phase build --owner $owner --output $output --dotnet C:/Users/tt/.dotnet/dotnet.exe
& $python -B -X utf8 eng/wp02-02/consumption-compatibility/capture_build.py --owner-root $output
& $python -B -X utf8 $runner --phase probe --owner $owner --output $output --dotnet C:/Users/tt/.dotnet/dotnet.exe
& $python -B -X utf8 eng/wp02-02/consumption-compatibility/inspect_graph.py --owner $owner --root $output --output "$output/graph-report.json"
& $python -B -X utf8 eng/wp02-02/consumption-compatibility/runtime_policy.py --expected "$output/pre-runtime-identities.json" --runtime "$output/runtime/runtime.json" --owner-root $output --output "$output/runtime-policy.json"
& $python -B -X utf8 -m unittest discover -s eng/wp02-02/consumption-compatibility/tests -v
```

Run from the authorized task worktree. `prepare` exclusively creates the exact
`artifacts/wp02-02/consumption-compatibility/<32 lowercase hex owner>/` directory.
Preexisting owners, traversal, alternate roots, symlinks and Windows junctions fail.
`build` and `probe` resume this owner once each; a failed attempt is retained and
requires a fresh owner. No environment dump or user NuGet config is read or emitted.
Caller credential-provider variables are inherited without inspection or logging.

## Source and overlay

`git ls-tree -rlz` inventories the entire fixed Main and Platform trees, including
every project, props and original lock. `git cat-file --batch` exports Git blob
bytes directly, bypassing checkout line endings, dirty files and LFS smudging.
The exported Main selection is the recursive ProjectReference closure of WebApi
and DatabaseCompatibility.Probe, the complete directories of those projects,
contracts and root build/config inputs. Platform exports all `src/`, `contracts/`,
`deploy/p09/` and root build/config inputs, retaining external project resources.
Omitted files retain their Git blob IDs and sizes in the
manifest; unselected docs, frontend and historical attachments are not needed to
compile these roots. Selected LFS pointers and nonregular entries fail closed.

Exactly four byte-local replacements in three copied Main projects change the
original `[0.10.2]` Platform PackageReferences to relative ProjectReferences into
the same fixed Platform snapshot. Each before/after hash is recorded. The first
original copy of each modified project and all exported original locks are saved
under `originals/`; production checkouts are never written. All other exported
source bytes are checked before execution and after builds. Added source files,
modified files and links fail; only generated `bin/`, `obj/` and the explicitly
named diagnostic locks are allowed additions.

The probe sources are copied from this task directory, hashed, and given an exact
copy of Platform's `Directory.Packages.props`. Owner `global.json` selects SDK
8.0.424 with roll-forward disabled; all dotnet commands use the explicit executable
and owner working directory. The actual selected SDK and installed runtimes are
recorded. Every restore passes the exact Main `NuGet.config` explicitly, retaining
signature validation, source mapping and signer pins. NuGet packages, HTTP/plugin
caches, CLI home and temporary files live under the owner. Every graph generates
`packages.wp02-02.lock.json`; existing `packages.lock.json` bytes remain unchanged.

Windows filesystem operations use extended `\\?\` paths, so successful restore
cannot be misreported as missing files by Python's ordinary MAX_PATH checks.
New attempts use `n/` for packages, `h/` for the HTTP cache and `p/` for plugin
cache. Both `--packages` and `NUGET_PACKAGES` use the same ordinary owner-local
`n/` path: NuGet-generated MSBuild imports may add duplicate separators, which
Windows verbatim paths reject. The actual selected net8.0 asset paths in the first
restored graph project to at most 254 characters under `n/`; Python still uses
extended paths for the full cache, including unselected framework/language files.
Canonical containment and evidence retain ordinary path identities. This
requires no junction, drive alias, shared cache or machine setting. The first
attempt's longer cache and failure logs remain intact; graph inspection obtains
the exact allowed cache from each executed restore command.

Run `capture_build.py` after builds and before the executable: it exclusively
writes `pre-runtime-identities.json` from actual artifacts and command evidence.
After runtime, graph and runtime-policy reports independently check dependencies
and observed identities. A partial private-probe run cannot pass the all-root policy.

Graph package hashes have two independently checked meanings. NuGet `contentHash`
must agree across `.nupkg.metadata`, assets, diagnostic locks and deps. The SHA-512
computed over the actual complete `.nupkg` archive must match its cache
`.nupkg.sha512` file; archive SHA-256 is recorded separately. The two SHA-512 groups
are not required to match each other. O849's actual CloudNative.CloudEvents 2.9.0
archive demonstrates different values for these groups. Missing or mismatching
hashes in either group still fail. These checks do not perform a new NuGet
signature validation, Current revocation check or feed round-trip verification.

## Evidence and limits

- `snapshot-manifest.json`: complete Git inventories, exact source/tree identities,
  selected-file hashes, original/overlay hashes, copied probe input hashes.
- `state.json`: separate source, build, runtime, candidate-plan and formal statuses;
  command argv/cwd/log/exit code/hash and invariant failures.
- `logs/*.command.json` and `*.log`: command metadata and original merged stdout/stderr.
- `build-identities.json`: actual assets, deps, DLLs, runtime configs and diagnostic
  locks with owner-relative paths, sizes and SHA-256, including partial failed builds.
- `runtime/runtime.json`: the executable's observed HTTP, telemetry, loaded assembly
  and cleanup results. Authentication/authority is explicitly synthetic.

All three real build commands include `-warnaserror:MSB3277`. Each independent root
is attempted once. A failed restore skips that root's build while retaining the
other roots' diagnostics; any failed root makes the overall build FAIL. Source or
SDK invariant failure stops execution. A successful private-probe restore/build
can still run after Main build failure, but runtime scope then explicitly reads
`PRIVATE_PROBE_ONLY_MAIN_BUILD_FAILED` and compatibility stays UNKNOWN.
Successful executable exit and reported PASS yield
only `OBSERVED_PASS_PENDING_POLICY`: the independent runtime policy must bind
actual assemblies to build artifacts/assets/deps and check required assertions.
Neither result approves formal packages, source production adoption, WP02-28 or
17PGT. Preserve logs and raw evidence; review secrets before publishing any copy.
The owner can be retained for auditing. Reuse of its output as a fresh run is forbidden.
