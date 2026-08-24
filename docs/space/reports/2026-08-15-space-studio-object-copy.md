# Space Studio 对象复制

日期：2026-08-15

需求：`LM-FR-023`（复制）

证据类别：RepositoryImplementation

接受结论：不构成 GA 接受证据

## 结论

Space Studio 的批量检查器已补齐“复制”：用户可选择 1–100 个 Active 通用元素和货架，显式确认后在同一 Design V1 Element Command Batch 内创建副本。通用元素通过 `CreateElement` 分配新 LogicalId；货架复用 `GenerateRackArray` 创建一个新货架并复制 Active RackLevel 与未绑定、空编码的 Location。整批继续受 Lease、Floor Revision、Content Revision/Hash、幂等和 Serializable 事务保护，任一命令失败零写入。

该纵切关闭 LM-FR-023 的“复制”仓库实现。对齐、等距分布、旋转和阵列此前已有实现，因此 LM-FR-023 五项仓库能力现已闭环；WP4 仍为 `Partial/Pending`，核心 GA 仍为 72% / `NoGo`。

## 冻结复制语义

- 一次只处理通用 Element 与 Rack，可混合选择；空选择、超过 100 个对象、非 Active 对象、资产实例及没有 Active 设计层的货架在写入前失败关闭。
- 通用元素保留类型、规范几何、父级、位置/旋转/尺寸和设计属性；副本清除 BusinessCode、业务链接及 CAD SourceId/SourceRef，避免复制唯一业务身份或把人工副本伪装为解析产物。
- 通用元素沿自身局部 X 轴按“对象宽度 + 500 mm”偏移；坐标和尺寸必须保持在 Int32 毫米范围。
- 货架沿既有阵列合同偏移 500 mm，复制 Active 逐层规格；生成库位使用新 LogicalId、空 LocationCode、`Generated/Unbound`，不复制 WMS 绑定语义。
- 货架编码使用 `<原编码>-COPY-<源 LogicalId 短码>-NNN`，在当前 Zone 内从首个可用序号开始；服务端仍执行最终唯一性和 Revision 并发校验。
- 确认前不发送写请求；成功后选中新副本。撤销对全部新 LogicalId 发送 `DeleteObject`，重做发送 `RestoreLogicalObject`，不会再次 Create 或分配新身份。

## 数据与接口影响

本任务没有数据库迁移、OpenAPI 或 SDK 变化。它只组合既有 `CreateElement`、`GenerateRackArray`、`DeleteObject` 和 `RestoreLogicalObject`，没有建立第二套复制 API 或绕过设计态 Revision。

## 可复现门禁

| 门禁 | 结果 |
|---|---:|
| 复制规划、身份清理、偏移、编码和失败关闭 Vitest | 4 passed |
| 批量检查器可达交互 Vitest | 3 passed（含本任务新增 1） |
| 前端全量 Vitest | 805 passed |
| Vue type-check / production build | passed |
| Space Studio Playwright（确认前零写入、稳定撤销/重做身份） | 19 passed |
| SQL Server LocalDB 混合 Element + Rack 复制、补偿和重做 | 1 passed / 0 skipped |
| Space Unit / OpenAPI 聚焦合同 | 531 / 44 passed |
| Release solution | passed / 0 warnings / 0 errors |
| OpenAPI/C#/TypeScript SDK drift | passed |
| GA 证据自测 | 36 passed / 0 failed |
| GA 普通/严格门禁 | exit 0 / expected exit 2 (`NoGo`) |

Mock 浏览器与 LocalDB 只证明仓库行为，不能替代授权真实 CAD、Site 主备 Provider、生产等价 WMS、双仓 Pilot 或五方签字。

## 后续任务

1. 下一独立任务实现 LM-FR-018：重新解析前明确展示被替换对象，并以持久化锁定/Apply Fence 保护人工校正不被覆盖。
2. 继续逐项审计 WP4 的其余 LM-FR 和三条路径，不因 LM-FR-023 闭环而把 WP4 标记 Complete。
3. 使用授权真实 DWG/DXF、Excel、PDF/图片、两条已认证 Provider 与 CP6 WMS 形成正式端到端接受证据。
