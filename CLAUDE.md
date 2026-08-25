# CP6 Agent Context

Repository-wide rules remain authoritative in `AGENTS.md`. This file records the deploy configuration detected and approved for the current CP6 workflow.

## Deploy Configuration

- Platform: custom Azure Pipelines YAML on the dedicated Windows `CP6-Deploy` agent, deploying to local Docker Desktop.
- CI workflow: `azure-pipelines.yml` (`GTX537.CP6`), triggered by `main`; CI does not deploy.
- DEV CD workflow: `azure-pipelines-dev.yml` (`CP6 DEV CD`), with manual and pipeline-completion modes.
- DEV target: Docker Compose project `cp6-dev`; database `CP6_DEV`; local API `http://127.0.0.1:19991`; local Web `http://127.0.0.1:18080`.
- Public daytime-test URL: `https://cp6.uk`; API health: `https://api.cp6.uk/health/ready`. This is a home-hosted DEV endpoint, not production.
- Public connector: dedicated Compose project `cp6-public-tunnel` attached only to `cp6-dev_default`. The one-time cutover is manual and must never run implicitly from DEV CD.
- Automatic deployment control: Azure Pipeline variable `CP6_DEV_AUTO_DEPLOY_ENABLED`, initially `false`. Manual runs remain available. A manual older-version deployment requires this variable to be `false`.
- Public verification control: Azure Pipeline variable `CP6_DEV_PUBLIC_VERIFICATION_ENABLED`, initially `false`; set to `true` only after the dedicated Tunnel cutover is verified.
- Environment serialization: Azure Environment `cp6-dev` must have an external Exclusive lock check; YAML uses `lockBehavior: sequential`.
- Merge method: normal PR merge into protected `main`; no direct development on `main`.
- Local status command: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-Cp6LabEnvironment.ps1 -Environment dev -Action Status`.
- Tunnel status command: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-Cp6PublicTunnel.ps1 -Action Status`.
- Manual DEV snapshot export: `scripts/Export-Cp6DevSnapshot.ps1`.
- Manual local side-by-side import: `scripts/Import-Cp6DevSnapshot.ps1`; target must be `CP6DEV_IMPORT_yyyyMMdd_HHmmss` and must never overwrite or merge into `CP6DB`.
- Production authority: unchanged GitHub R2/GHCR workflows under `.github/workflows/` and `docs/client/r2/`. Azure home DEV is not a production release authority and must not deploy UAT/PROD from locally rebuilt images.

Do not run a real deploy, Tunnel cutover, database restore, remote branch deletion, or production action unless the user explicitly authorizes that external mutation.
