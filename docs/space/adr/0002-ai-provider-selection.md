# ADR-0002：AI Provider 选择、安全与降级

- 状态：**Accepted, first external provider gated by evidence**
- 日期：2026-07-25
- 关联：E13-S01、E13-S04～S06、D13～D17、T1～T7

## 1. 问题

AI 可以降低 CAD 语义映射成本，但 Provider 可能造成原图外发、租户串数据、不可预测输出、费用失控和供应商锁定。Provider 选择不能改变“AI 只产提案、确定性引擎生成几何、人工确认后才写 Draft”的边界。

## 2. 决策

采用三层实现且共享同一 Provider 端口：

1. Mock Provider：契约、故障注入和离线 E2E 的必备实现。
2. 本地/规则 Provider：外部 AI 不可用时的确定性降级。
3. 外部 Provider：只有通过本 ADR 门槛的租户才能显式启用。

既有租户策略固定为 `Disabled`。试点租户只能由管理员在指定 Site、预算和数据策略下启用 `MetadataOnly` 或 `StructuredFeatures`。

## 3. 硬门槛

| 类别 | 门槛 |
|---|---|
| 数据 | 不接收原始 DWG/DXF/PDF/图片，只接收机器构造的最小 CAD IR 特征 |
| 训练 | 租户数据、Prompt、输出和人工修正不得用于 Provider 训练 |
| 保留 | 保留期可配置并可提供删除/到期证据 |
| 区域 | 数据处理区域可选择且符合租户合同 |
| 输出 | 支持结构化 JSON；输出必须经过本地 JSON Schema、类型、范围和引用校验 |
| 隔离 | 每次调用固定 Tenant/Run 追踪；缓存、日志和用量不得跨租户 |
| 运维 | 超时、限流、熔断、幂等记账、取消、重试和健康状态可观测 |
| 商务 | 价格、速率限制、SLA、责任边界和退出/删除条款明确 |
| 安全 | 通过 Prompt 注入、敏感内容外发和日志脱敏测试 |

任一硬门槛失败，外部 Provider 不得启用。

## 4. Provider 接口冻结

```csharp
Task<WarehouseGenerationResult> GenerateAsync(
    WarehouseGenerationInput input,
    CancellationToken cancellationToken);
```

机器契约：

- [`warehouse-generation-input.schema.json`](../contracts/ai/v1/warehouse-generation-input.schema.json)
- [`warehouse-generation-output.schema.json`](../contracts/ai/v1/warehouse-generation-output.schema.json)
- [`proposal-patch-policy.md`](../contracts/ai/v1/proposal-patch-policy.md)

Provider 不得：

- 直接访问数据库或对象存储。
- 接收可下载原文件的 URL。
- 分配最终 `LocationLogicalId`。
- 修改 Draft 或 Published。
- 决定单位、坐标、拓扑、碰撞和编码。

外部 Provider 适配器调用前还必须经过运行时出站门禁：请求对象及其所有嵌套对象只能包含冻结 JSON 字段白名单；Run、Source、Layer、Block、Attribute、Mapping Hint 等关联值必须符合本地最小化器生成的 HMAC Token 格式；任一额外字段、原始名称、任意文本或非白名单锁定字段均以 `SPACE_AI_OUTBOUND_PAYLOAD_DENIED` 失败关闭，且不得先占用配额或调用 Provider。

## 5. 评分规则

通过硬门槛后按 100 分评分：

| 维度 | 权重 |
|---|---:|
| 黄金数据语义质量 | 35 |
| 数据安全、区域和合同 | 25 |
| 延迟、可用性和限流恢复 | 15 |
| 单 Run 和月度预算成本 | 15 |
| 可观测性、版本固定和退出能力 | 10 |

总分低于 85 不得进入试点。分差小于 3 分时，优先选择数据保留更短、区域更可控、结构化输出更稳定的方案。

## 6. 质量和发布门槛

- 覆盖率 ≥80%。
- 整体准确率 ≥90%。
- 高置信度组实测精确率 ≥95%，且不能用阈值替代实测。
- 50MB CAD 到可审查提案 P95 ≤15 分钟。
- 人工操作量下降 ≥70%。
- Provider 失败、非法输出、取消和版本冲突均为零部分写入。

发布步骤固定为：

1. Mock/本地离线。
2. 影子运行 ≥7 天且 ≥50 Run，不开放 Apply。
3. 至少 2 个显式同意租户，各运行 ≥14 天且 ≥20 个成功 Run。
4. 每批最多增加已启用租户的 25%，观察 ≥7 天。

## 7. 成本与配额

- 单租户同时最多 3 个 Generation Run。
- 每次调用记录 Provider、模型版本、输入/输出计量、费用、RunId 和 TenantId。
- 预算在发送前预留，完成后结算；超预算返回 `SPACE_AI_QUOTA_EXCEEDED`。
- Provider 不确定结果仍须记账并由对账任务闭合。

## 8. 降级与回退

- 熔断或预算关闭时不接受新的外部推理。
- 未发送任务取消；已发送请求等待记账和审计。
- 已生成但未 Apply 的提案保留审计，关闭后不允许 Apply。
- 已 Apply 的 Draft 可以继续人工编辑、校验或删除。
- Published、规则 CAD 导入和地图编辑器不受影响。

## 9. 试验输出

E13-S05 必须提交：

- Mock、本地和外部 Provider 契约测试。
- Provider/模型/区域/保留/SLA/价格版本。
- 20 份黄金数据报告和留出集证据。
- Prompt 注入与原文件外发检查。
- 限流、超时、非法输出、取消和熔断证据。
- 月度预算模型和供应商退出方案。
