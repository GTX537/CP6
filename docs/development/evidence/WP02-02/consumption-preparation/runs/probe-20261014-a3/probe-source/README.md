# WP02-02 task-private archive probe

This executable proves only archive API shape and synthetic contract plumbing.
It does not adopt Platform in Main, authenticate a real principal, query a real
authority, access a database, establish formal package trust, or run the 17 PGT
acceptance cases. All runtime adapters are explicitly synthetic fixtures.

Run `../run_probe.py --help` for the wrapper arguments. The wrapper requires a
separately approved seven-package document and its independently obtained SHA-256.
It checks every archive hash and nuspec repository/source/version before extracting
the four context DLLs. The approval document uses the producer consumer-lock shape:
`repository`, `sourceGitSha`, and seven `packages` with `packageId`, `version`,
`sha256`, `sourceGitSha`. Hash agreement proves local input identity, not feed trust.

Each invocation requires a new output directory. Runtime mode copies this task's
probe sources into that directory, uses SDK 8.0.424 and the installed ASP.NET Core
framework reference, and performs a no-feed restore/build. It adds no package
source and changes no production NuGet configuration or project reference.
The local `Directory.Build.props/targets` isolate this project from Main imports.

`archive-metadata.json` uses PE metadata to enumerate context types and visibility,
including the internal `ContextBindingValidatorV1`, and list missing required
types. `signatures-and-synthetic-runtime.json` records public declared constructors,
methods, properties, fields and events, plus base types, implemented interfaces,
parameter/return types, default values, modifiers, nullability and generic
variance/constraints. Inherited members are represented by their declaring base
type; they are not repeated. Compiler-generated public record members are retained.
The loaded DLL hashes must equal the extracted approved DLL hashes.

The runtime checks use the actual packaged implementations with fixture
`IContextSourceVerifierV1<HttpContext>` and `IContextBindingAuthorityV1` adapters.
They cover DI/middleware success, no public trusted-context constructor/setter,
masked verifier/authority access, cleanup after completion and captured execution
contexts, legacy scopes without trusted evidence, tenant mismatch, missing
authentication, unavailable authority, cancellation before/after authority,
handler failure, and the generic message executor. These are task probe checks,
not a Main host or compatibility qualification.

Metadata-only mode takes `--mode inspect --inspector <previously-built-Probe.dll>`.
It inspects the supplied DLLs through PEReader without loading or executing those
DLLs. A successful inspection can report `MISSING_REQUIRED_TYPES`; its PASS means
the inspection completed, not that the package was admitted. This mode provides
the actual 0.10.2 negative evidence without trying to compile missing APIs.

Parser regression tests: `python eng/wp02-02/consumption/probe/test_runner.py -v`.
Their deliberately synthetic ZIP fixtures cannot be used as package evidence.
The wrapper retains command lines, exit codes, errors, SDK/runtime information,
input and output hashes, source hashes, and logs in `report.json` and siblings.

The observed WP02-27 archive probe builds with MSB3277: the framework selects
DiagnosticSource 8 while the AspNetCore/Messaging archive references include
DiagnosticSource 10. The context fixture paths pass on .NET 8.0.30, but the warning
is retained and full package dependency compatibility remains Unknown. No warning
is suppressed and no production dependency is altered to make the probe pass.
