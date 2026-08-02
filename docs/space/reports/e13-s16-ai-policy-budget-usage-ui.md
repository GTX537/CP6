# E13-S16 租户 AI 策略、预算与用量管理 UI 完成报告

- 状态：**Ready for controlled integration**
- 日期：2026-08-02
- 功能分支：`codex/space-e13-s16-ai-admin-ui`
- 实现提交：`0549a1f2`
- 集成目标：`integration/space-v1-20260730`

## 1. 交付结论

E13-S16 已完成租户管理员可见的 AI 管理闭环：版本化策略、获批 Provider
别名、站点 allowlist、最大并发、日/月预算、用量与费用查询，以及对应的
管理页面、菜单、权限、五语种 seed、OpenAPI 和 C#/TypeScript SDK。

管理合同只接受部署时已注册的 Provider 别名，不接受或返回密钥、URL、
Endpoint 等连接信息。外部主体被服务层显式拒绝；没有策略时运行时继续使用
`Disabled` 失败关闭默认值。

## 2. API 与策略边界

新增 Design V1 操作：

- `GET /api/space/design/v1/ai-policy`
- `PUT /api/space/design/v1/ai-policy`
- `GET /api/space/design/v1/ai-usage`

读取使用 `space-ai-admin:read`，变更使用
`space-ai-admin:manage`。菜单只分配给租户管理员。PUT 要求
`Idempotency-Key`，并使用 `ExpectedVersion` 做乐观并发检查；相同主体、
相同输入和相同幂等键可安全重放，不同或过期输入返回稳定冲突。

策略写入规则：

- `DataPolicy` 只允许 `Disabled`、`MetadataOnly`、
  `StructuredFeatures`；
- 启用策略至少需要一个站点和一个获批 Provider 别名；
- 最大并发固定在 1～3；
- `ExternalProviderEnabled` 必须与所选外部 Provider 别名一致；
- 日/月预算使用最小货币单位，非空预算必须带合法三字母币种；
- 旧活动版本先在同一事务中停用，再插入新版本，避免过滤唯一索引的执行
  顺序风险。

## 3. 数据与安全

Migration `20260802115537_SpaceE13S16AiPolicyManagement` 新增
`Space_AiTenantPolicy`。策略按租户和版本追加保存，并用过滤唯一索引保证每个
租户最多一个活动版本；模型保护器禁止删除历史版本，也禁止除活动→停用以外
的历史修改。

用量查询复用 E13-S02/S12 的审计账本，支持最长 366 天、Provider、结果、
分页和稳定排序。汇总明确区分：

- 实际费用；
- 只有估算费用；
- 没有 Provider 定价的用量。

日/月预算余额由当前周期的预留、已报告实际费用和释放状态计算，不伪造未
定价费用。响应 DTO 和生成 SDK 均不包含 secret、API key、URL 或 endpoint
字段。

## 4. 管理界面

新增 `/space/ai-admin`：

- 策略版本、数据策略、站点、获批 Provider、并发和预算编辑；
- 外部 Provider 开关由 Provider 类型派生，不允许手工绕过；
- 用量摘要、日/月余额、实际/估算/未定价费用、筛选和分页；
- `space-ai-admin:manage` 前端按钮权限与后端强制权限一致；
- 54 个 `space.aiAdmin.*` 文案以五个非空语言版本进入 seed。

## 5. 验证证据

| 检查 | 结果 |
|---|---|
| E13-S16 服务聚焦测试 | 5/5 passed |
| 权限守卫聚焦测试 | 18/18 passed |
| Space UnitTests | 231/231 passed |
| Space IntegrationTests | 168 passed / 57 SQL-environment skipped / 0 failed |
| CP6.Tests 全量 | 2733 passed / 17 environment-gated skipped / 0 failed |
| 前端 E13-S16 聚焦测试 | 3/3 passed |
| 前端全量 | 112 files / 622 tests passed |
| 前端 type-check 与 production build | passed；仅既有大 chunk 提示 |
| 完整 `CP6.slnx` Release build | 0 warnings / 0 errors |
| EF model drift | 无待生成模型变更 |
| OpenAPI / C# / TypeScript SDK drift | passed；Design V1 为 59 operations |
| `git diff --check` | passed |

`npm run i18n:check` 仍报告基线中已知的 843 项存量缺口；本卡新增 key 全部
进入五语 seed，缺口数量没有增加。默认测试环境没有 SQL Server，因此 57 个
真实 SQL 用例按既有规则跳过；本卡的 EF 模型、Migration 和迁移漂移门禁均
已通过。

## 6. 明确未提前实现

- Provider 密钥、URL、Endpoint 的租户级配置或回显；
- 外部 Provider 实际调用、CAD IR、输出验证和 Apply；
- 汇率换算、财务发票对账或供应商采购流程；
- E13-S17 迁移前向修复/保留清理和 E13-S18 指标告警/槽回收演练。

E13-S16 只消费 E13-S01 与 S12 已冻结的安全端口和账本，不改变 E13-S04
以后仍受 CAD/Provider 证据约束的依赖关系。
