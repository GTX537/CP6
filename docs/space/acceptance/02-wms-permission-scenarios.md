# Space WMS、故障恢复与权限验收矩阵

## 1. WMS 场景

| ID | 场景 | 前置 | 动作 | 必须结果 | 发布关卡 |
|---|---|---|---|---|---|
| WMS-GREEN-001 | 绿地首次发布 | WMS 无库位 | 发布 10,000 库位 | 全部按 LogicalId 幂等建立；验证成功后激活 Published | Alpha |
| WMS-GREEN-002 | 绿地重复请求 | 首次请求结果未知 | 使用同一幂等键重试 | 不重复建库位，返回同一业务结果 | Alpha |
| WMS-BROWN-001 | 存量采纳 | WMS 已有编码 | 导入并绑定现有库位 | 保留外部 ID/编码，建立 Adapter Binding | Beta |
| WMS-BROWN-002 | 存量缺失几何 | 已采纳但未放置 | 在地图中绑定货架格口 | 只补几何与关系，不修改 WMS 身份 | Beta |
| WMS-PARTIAL-001 | 部分应用 | 第 N 批 WMS 失败 | 重试发布 | Published 不切换，进入可恢复对账 | Beta |
| WMS-UNKNOWN-001 | 提交结果不确定 | WMS 超时 | 查询适配器状态 | 按回读结果继续或补偿，不盲目重复 | Beta |
| WMS-LOCAL-001 | WMS 成功、本地激活失败 | WMS 已验证 | 重试本地激活 | 不重复写 WMS，最终 Published 与 WMS 一致 | GA |
| WMS-ROLLBACK-001 | 历史版恢复 | 当前版已发布 | 选择历史版重新发布 | 形成新 PublishAttempt，历史记录不修改 | Beta |
| WMS-RECON-001 | 人工对账 | `ReconciliationRequired` | 管理员执行恢复动作 | 每个不一致项闭合并保留审计 | GA |
| WMS-DOWN-001 | WMS 不可用 | 适配器健康失败 | 发起发布 | 拒绝或排队，不改变当前 Published | Alpha |

## 2. AI 和 Draft 故障

| ID | 场景 | 必须结果 |
|---|---|---|
| AI-DISABLED-001 | 既有租户创建 AI Run | 返回 `SPACE_AI_DISABLED`；规则 CAD/编辑器继续 |
| AI-INVALID-001 | Provider 输出非法引用/枚举/数量 | 返回 `SPACE_AI_OUTPUT_INVALID`；Draft 无变化 |
| AI-STALE-001 | 审查期间 Draft Revision 改变 | Apply 返回 `SPACE_AI_RUN_STALE`；零部分写 |
| AI-INCOMPLETE-001 | 必审项未决 | Apply 返回 `SPACE_AI_REVIEW_INCOMPLETE` |
| AI-CANCEL-001 | Preparing/Inferring 中取消 | 状态终止、槽位释放、已发生费用记账 |
| AI-PROVIDER-001 | 超时/限流/熔断 | 可重试或规则降级；Published 无变化 |
| AI-TX-001 | Apply 事务中故障 | 全部回滚，ContentRevision 不增加 |

## 3. 主体与资源

内部角色：

- `SpaceModeler`：上传、建模、审查 AI、编辑 Draft。
- `SpacePublisher`：校验、审批和发布。
- `TenantSpaceAdmin`：策略、成员、Grant、预算和 Provider 配置。
- `SpaceAuditor`：只读审计与发布证据。

外部主体：

- `CustomerViewer`
- `SupplierViewer`
- `ThirdPartyLogisticsViewer`

外部主体始终受 Organization Membership、Site/Zone/Owner/BusinessObject Grant 和 FieldPolicy 共同约束。

## 4. 权限矩阵

| 行为/资源 | Modeler | Publisher | Tenant Admin | Auditor | Customer | Supplier | 3PL |
|---|---:|---:|---:|---:|---:|---:|---:|
| Published 3D/库存/任务 | Grant | Grant | 允许 | 允许 | Grant | Grant | Grant |
| Draft/Ready 版本 | 允许 | 允许 | 允许 | 只读 | 拒绝 | 拒绝 | 拒绝 |
| 上传源文件 | 允许 | 拒绝 | 允许 | 拒绝 | 拒绝 | 拒绝 | 拒绝 |
| 下载源文件 | 允许 | 拒绝 | 允许 | 按审计策略 | 拒绝 | 拒绝 | 拒绝 |
| CAD/Excel 映射 | 允许 | 拒绝 | 允许 | 只读 | 拒绝 | 拒绝 | 拒绝 |
| 创建 AI Run | 允许 | 拒绝 | 允许 | 拒绝 | 拒绝 | 拒绝 | 拒绝 |
| AI 提案/Prompt/日志 | 允许 | 按职责只读 | 允许 | 脱敏只读 | 拒绝 | 拒绝 | 拒绝 |
| AI 费用/预算 | 拒绝 | 拒绝 | 允许 | 脱敏只读 | 拒绝 | 拒绝 | 拒绝 |
| Proposal Apply | 允许 | 拒绝 | 允许 | 拒绝 | 拒绝 | 拒绝 | 拒绝 |
| 校验 | 允许 | 允许 | 允许 | 只读 | 拒绝 | 拒绝 | 拒绝 |
| Publish Saga | 拒绝 | 允许 | 允许 | 只读 | 拒绝 | 拒绝 | 拒绝 |
| 外部组织/Grant 管理 | 拒绝 | 拒绝 | 允许 | 只读 | 拒绝 | 拒绝 | 拒绝 |
| 审计导出 | 拒绝 | 按职责 | 允许 | 允许 | 拒绝 | 拒绝 | 拒绝 |

`Grant` 表示仍须通过 Space 多维数据评估，不是拥有全租户读取权限。

## 5. 外部组织场景

| ID | 场景 | 必须结果 | GA 阻断 |
|---|---|---|---:|
| EXT-SITE-001 | 客户仅获 Site A | Site B 返回 404/403 且不泄露存在性 | 是 |
| EXT-ZONE-001 | 供应商仅获 Zone Z1 | Z2 库位、库存和任务不可枚举 | 是 |
| EXT-OWNER-001 | 3PL 仅获 Owner O1 | O2 的库存、批次和容器被过滤 | 是 |
| EXT-FIELD-001 | FieldPolicy 隐藏批次/数量 | DTO 不序列化受限字段 | 是 |
| EXT-DRAFT-001 | 外部用户猜测 Draft URL | 拒绝且写安全审计 | 是 |
| EXT-SOURCE-001 | 外部用户猜测 File/Artifact URL | 拒绝；对象存储 URL 不生成 | 是 |
| EXT-AI-001 | 外部用户猜测 Run/Proposal URL | 拒绝；Prompt/费用/日志不泄露 | 是 |
| EXT-EXPIRED-001 | Membership 到期 | 所有 Portal 请求立即失效 | 是 |
| EXT-CACHE-001 | Grant 撤销后访问缓存 | 缓存键隔离并在 SLA 内失效 | 是 |
| EXT-SIGNALR-001 | 订阅其他 Site/租户频道 | 握手或组加入被拒绝 | 是 |

## 6. 验收证据

每个场景保存：

- Tenant、主体类型、Organization、Grant 和 FieldPolicy 的非敏感标识。
- 请求、响应状态、稳定错误码和 CorrelationId。
- 数据库/适配器前后快照。
- 审计事件。
- 应用 SHA、Migration、数据包版本和执行时间。

任何跨租户、外部 Draft/Source/AI 访问或字段泄露都阻断 GA，不能以“低概率”豁免。

