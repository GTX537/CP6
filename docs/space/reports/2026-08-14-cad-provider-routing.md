# Space Studio WP3 Site CAD Provider 认证与路由基础

日期：2026-08-14

任务分支：`codex/space-cad-provider-routing`

基线：`main@1c64e57710df80bf06f2525897c18b71f2e32907`

## 结论

本任务完成 Site 级 CAD Provider 认证、运行注册、主备选择和合规故障切换的仓库基础，并把能力状态接入 CAD 起始向导。它关闭的是 WP3 的内部合同与失败关闭边界，不代表真实 Provider、某个 Site 或 CAD GA 已验收。

生产默认 `ISpaceCadProviderRegistry` 为空；未安装并注册受控 Worker 时，任何 Site 都不会因为仅写入认证记录而获得可用 CAD 能力。`CadGaReady` 只在 Primary 和 Backup 均处于有效期、运行注册与认证边界一致，并且两者同时覆盖 DWG/DXF 时返回 true。

## 已交付

- Tenant/Site 级追加式 Provider 配置，包含 Configuration Revision、Primary/Backup、部署模式、数据边界、审批证据引用、有效期、DWG/DXF 能力和 Secret 引用。
- 配置替换使用专用 Site applock、Serializable 事务、Expected Revision 和 Idempotency-Key；历史配置保留，认证明细不可修改或删除。
- `GET /api/space/design/v1/sites/{siteId}/cad-capability` 与 `PUT /api/space/design/v1/sites/{siteId}/cad-provider-configuration`，分别要求 `space:model:read` 和 `space:model:provider:manage`。
- `SpaceCadProviderRouter` 同时实现 Preparation 和 Parse Provider 合同，只选择当前 Site 认证与当前部署注册的交集。
- Primary 发生明确可重试资源故障时，只允许转到同一 Site、同一当前配置中的 Backup；未认证 Provider、格式不匹配、过期认证、部署/数据边界不一致、不可 seek 的输入和从 Backup 反向切回 Primary均失败关闭。
- sealed Preparation 保存实际 Provider Key/Version；Parse payload schema v3 绑定 Preferred Provider Key 和 Semantic Preview Hash，审核阶段继续复核完整工件链。schema v2 仍可在当前 Site 合规路由下读取，schema v1 保持拒绝。
- Space Studio 起始向导先读取 Site 能力，展示配置 Revision、主备链和阻断码；没有 `CanPrepareCad` 时不轮询来源扫描，也不开放 Preview。
- 可回滚 EF Migration、幂等 SQL 部署脚本、OpenAPI、C#/TypeScript SDK、权限种子和自动化测试。

## 安全与数据边界

- 客户端不能指定任意 Provider；路由由服务端当前认证配置和运行注册共同决定。
- 外部主体不能读取或维护 CAD Provider 配置。
- `ApprovedCloudService` 必须提供受管 Secret 引用；API 只返回是否已配置，不返回引用内容或 Secret。
- Provider 注册的部署模式、数据边界和格式能力必须覆盖认证声明；否则配置写入或执行失败关闭。
- 原始 CAD 不会自动从本地链跨到未获该 Site 批准的云链。

## 验证

| 门禁 | 结果 |
|---|---|
| Release solution build | 通过，0 warning / 0 error |
| CP6.Tests | 2,876 passed / 19 environment-gated skipped |
| Space UnitTests | 501 passed / 0 skipped |
| Space ClientTests | 71 passed / 0 skipped |
| Space IntegrationTests | 305 passed / 104 environment-gated skipped |
| Web Vitest | 754 passed |
| Space Studio Playwright | 8 passed |
| Vue type-check / production build | 通过 |
| OpenAPI + C#/TypeScript SDK drift | 通过 |
| EF pending-model changes | 无漂移 |
| `git diff --check` | 通过 |

新增路由自动化覆盖两 Provider 配置/重放、Revision 冲突零漂移、Primary 到认证 Backup、未认证运行实例拒绝、Parse 主备切换、Backup 不反向切换以及认证不可变。新增真实 SQL 用例覆盖并发配置替换、唯一 Current Revision、历史追加、认证不可变和迁移脚本重复执行，但当前机器未配置 `CP6_TEST_SQLSERVER`，因此该用例被测试框架跳过，不能计作真库通过。

## 未完成与 No-Go

- 未实现或认证真实 ODA、APS 或其他候选适配器；运行注册默认为空。
- 未取得任何 Site 的客户、租户、安全、法务、采购或数据区域审批材料。
- 未证明同一 Site 存在两条真实、有效、同黄金集通过的 Provider 链。
- 未在真实 SQL Server 执行本任务新增并发/迁移测试。
- 未运行 20 份授权真实黄金 CAD、50MB P95、Iris Xe Viewer、CP6 WMS 恢复、双仓 14 天 Pilot 或五方签字。

下一步应继续使用独立任务分支交付真实 Provider 适配器/隔离 Worker 注册及其安全测试；只有认证、黄金集和 Site 双链证据齐全后，才能把该 Site 标记为 CAD GA。
