# .NET 10 LTS upgrade gate

Owner: CP6 client/platform team

Target completion: 2026-09-30

Hard gate: 2026-10-01

.NET 8 support ends in November 2026. No external/native production rollout
may pass the hard gate while `CP6.Client.Core`, `CP6.Client.Api`,
`CP6.Desktop`, or `CP6.Mobile` still targets .NET 8.

Acceptance:

1. Retarget all four projects and client tests to .NET 10.
2. Upgrade MAUI, WPF, SignalR, MVVM Toolkit, and signing pipelines.
3. Re-run clean-device MSIX and APK install/upgrade tests.
4. Re-run auth, refresh concurrency, MOVE idempotency, five-language, and
   forced-upgrade end-to-end suites.
5. Regenerate and approve the OpenAPI client surface hash.
