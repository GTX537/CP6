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

## 执行记录（UPG-NET10-01 / NET10-06-CLIENT-TOOLS，2026-11-25）

| 验收项 | 状态 | 说明 |
| --- | --- | --- |
| 1. 目标框架 | 已完成 | `CP6.Client.Api`、`CP6.Client.Core`、`CP6.Client.Tests` 只保留 `net10.0`；`CP6.Desktop` 固定为 `net10.0-windows10.0.19041.0`，不再随 SDK 版本切换；`CP6.Mobile` 原为 `net10.0-android`。`scripts/check-client-net10-gate.ps1` 改为同时拦截多目标和带条件的 `net8.0` 目标。 |
| 2. 依赖 | 部分完成 | SignalR.Client、Microsoft.Extensions.* 与 ProtectedData 升到 10.0.15；MAUI 10.0.20、MVVM Toolkit 8.4.0 保持不变。签名流水线本卡未改动，也未运行。 |
| 3. MSIX/APK 安装与升级 | 未运行 | 本机没有签名证书和干净设备。Desktop 已在本地完成 `win-x64` 未签名 Release 发布；Android 构建受本机工作负载缺包限制，基线同样失败。 |
| 4. 端到端套件 | 部分完成 | `CP6.Client.Tests` 在 net10.0 下 114/114 通过，与 net8.0 基线逐条一致；设备上的端到端套件未运行。 |
| 5. OpenAPI 客户端面哈希 | 待批准 | 升级前后 swagger 完全相同。现有漂移来自 `cecb01120`（WP02-20）新增的 `channel`、`packageSha256` 和 `publisherSha256`，与 .NET 10 无关；须经批准后重新生成哈希。2026-11-25 用户决定不在本卡处理，作为 WP02-20 遗留另行处理。 |
