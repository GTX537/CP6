# E13-S01 Provider SPI、租户策略与功能开关完成报告

- 状态：**Integrated**
- 日期：2026-07-30
- 功能分支：`codex/space-e13-s01-provider-contract`
- 功能提交：`8f7fc25e`
- no-ff 集成提交：`ea161975`
- 集成分支：`integration/space-v1-20260730`

## 1. 交付结论

E13-S01 已按冻结边界交付并进入唯一 Space 集成基线。实现固定了 `IWarehouseGenerationProvider.GenerateAsync`、机器可读输入/输出类型、独立确定性生成端口、Provider 别名注册表、租户/Site/数据策略门禁和原子配额租约端口。

既有租户和未显式配置的租户始终返回 `Disabled`。默认依赖注入不注册任何 Mock、本地或外部 Provider，配额租约也默认失败关闭；因此仅授予权限或误配单个组件都不能触发外部调用。

## 2. 冻结边界

### 已实现

- Provider SPI 与 JSON Schema v1 对应的强类型输入/输出。
- `MetadataOnly` / `StructuredFeatures` Provider 输入；`Disabled` 无法构造为 Provider 输入。
- Provider 输入不包含 `TenantId`、`SiteId`、文件名、存储键、来源 URL 或 `LocationLogicalId`。
- Mock、本地、外部三类 Provider 共享同一端口和契约测试骨架。
- 租户策略包含允许 Site、批准的 Provider 别名、数据策略、最多三并发和外部 Provider 显式开关。
- Provider 策略只暴露批准别名，不暴露 URL、Endpoint、Secret 或 API Key。
- Provider 调用前完成租户、Site、别名、数据策略、外部 Provider 和配额租约检查。
- Provider 成功、失败或取消时均由 `await using` 释放配额租约。
- 独立确定性端口不依赖 AI 租户策略或 AI 权限，保留规则路径回滚面。
- Space 管理员权限种子新增 `space:model:generate-ai` 与 `space:model:review-ai`。

### 明确未实现

- E13-S02 的 Run、Proposal、Decision、Usage 持久化模型和 Migration。
- E13-S03 Worker 处理器。
- E13-S04 CAD IR 最小化生产管线。
- E13-S05 Mock、本地和外部 Provider 生产适配器及供应商证据。
- E13-S06 Provider 输出不可信输入校验。
- E13-S07 规则/AI 融合和确定性生成实现。
- E13-S12 并发、预算、用量与费用的生产租约实现。
- AI HTTP API、Provider 凭据、外部网络调用、Prompt 或响应正文持久化。

## 3. 权限、错误码与数据变更

新增权限种子：

- `space:model:generate-ai`
- `space:model:review-ai`

新增稳定错误码：

- `SPACE_AI_DISABLED`
- `SPACE_AI_QUOTA_EXCEEDED`
- `SPACE_AI_PROVIDER_UNAVAILABLE`
- `SPACE_AI_SOURCE_POLICY_DENIED`

本卡没有新增实体、表、索引或 Migration。权限授予与租户 AI 功能开关相互独立：管理员获得动作权限后，租户策略仍默认 `Disabled`。

## 4. 验证证据

| 检查 | 结果 |
|---|---|
| `dotnet build CP6.slnx -c Release --no-restore`（合并态） | 0 errors，10 existing warnings |
| Space UnitTests | 97 passed |
| Space IntegrationTests | 41 passed，30 SQL-gated skipped |
| E13 Provider 契约测试 | 18 passed |
| Space 权限聚焦测试 | 17 passed |
| CP6.Tests | 2680 passed，17 environment-gated skipped |
| CP6.Client.Tests | 71 passed |
| 新增/修改 C# 精确格式门禁 | 通过 |
| 暂存差异检查 | 9 个预期源码/测试文件，`git diff --cached --check` 通过 |

30 个 Space SQL 测试和 17 个主测试跳过项均为仓库既有环境门禁，不记作通过。本卡无数据库模型变化，因此未新增 SQL 专用测试。

## 5. 回滚与故障演练

- 默认租户策略源返回 `Disabled`，Provider 和配额端口调用次数均为 0。
- 外部 Provider 即使已注册，未显式打开租户外部开关时仍返回 `SPACE_AI_SOURCE_POLICY_DENIED`。
- 缺 Provider 注册返回 `SPACE_AI_PROVIDER_UNAVAILABLE`。
- 缺原子配额租约返回 `SPACE_AI_QUOTA_EXCEEDED`，Provider 调用次数为 0。
- Provider 抛出异常时配额租约仍被释放。
- 关闭或移除外部 Provider 不影响规则确定性端口和现有解析/编辑器路径。

## 6. 偏差、估算与后续

与 ADR-0002、AI JSON Schema 和 E13-S01 冻结验收无产品行为偏差。E13-S01 的 3 工程师日规划基线不调整；本卡没有产生足以重估 196 工程师日总基线的新证据。按冻结计划，E13-S05 完成首个外部适配器证据后再统一重估 Provider 工作量。

下一张可独立启动卡为 E13-S02。E13-S04 继续等待 E02-S03，E13-S05 继续等待 E13-S04 和正式供应商证据。
