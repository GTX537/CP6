# C03 local Release publication

Four outputs were published locally from committed, clean source inputs on 2026-09-12:

- Core API and ERP fixture: `a51938f8ab055e54ebbedfec3b810e44c82e1020`.
- CRM API and C03 transport runner: `b3adb17dd0087c99c2d82a759d560d4bbf79a7ed`.

Each used `dotnet publish --no-restore --configuration Release --output <fresh-output-directory>` with the exact
`SourceRevisionId`. [published-files.json](published-files.json) contains SHA-256 and byte counts for all **864 files**.
Every recorded file hash was checked again after actual execution and matched.

The published Core API/fixture and CRM runner passed the [fresh seven-case real transport execution](../transport-attempt-4/README.md).
The published CRM API separately started on an isolated loopback port and passed [three HTTP checks](crm-api-http.json):
`/health/live` and `/` returned 200; explicitly unconfigured `/health/ready` correctly returned 503. That API check proves
packaging/startup and the default-off boundary. The enabled ERP read client was exercised by the published transport runner.

The [verification record](verification.json) binds execution, publication hashes and the reused local test evidence.
The 95 SQL, 153 Core unit/HTTP and 114 CRM unit results came from their recorded pre-publication executions;
they are not claimed as tests rerun against Release outputs. Source, dependencies and configuration relevant to those
results did not change. Existing C02 Web/browser evidence is reused only for unchanged Web inputs: this task changes no
Web source and performs no new Web build or browser acceptance.

The four publications, raw build logs, test TRX and private runtime inputs remain local. Only allowlisted metadata is
archived here. No Actions were dispatched, no remote success checks were fabricated, and no R2 image, signature,
registry artifact or production deployment was created. These outputs are local verification evidence only.
