# E04-S05 CAD 问题列表与画布定位开发切片

日期：2026-08-05

## 交付结论

CP6 已在 E03-S04 集成基线 `3300d01b` 上完成功能提交 `2ac9472f`、证据提交
`5114307e`，并以 no-ff 提交 `bd4ab90a` 集成到
`integration/space-v1-20260730`：把 E02-S07 的 CAD 诊断位置、低置信度/拒绝
提案和可选的 E03-S04 Excel/CAD 异常匹配行，组合成确定性、只读的 CAD Review
Workspace；Design V1 编辑器可本地加载该开发工件，按状态、严重度、类型、关键字
和是否可定位筛选，并在点击问题后同步聚焦底图、已应用设计对象与 CAD 问题覆盖层。

该实现不建立生产问题 API，不修改诊断、匹配预览或 Draft，不执行自动修复，也不
把本地 JSON 文件视为授权图纸或生产审计证据。因此这是解除后续 UI/交互风险的开发
切片，不是正式 E04-S05 验收。

## 本次实现

1. 新增 `SpaceCadReviewWorkspaceV1` 合同与 JSON Schema，显式绑定 Tenant、
   ModelVersion、Floor、Diagnostic Index SHA、可选 Match Preview SHA、编辑器内容
   修订/哈希/快照 SHA、前一工作区 SHA 和自身 Workspace SHA。
2. 工作区收录全部 Mapping/Semantic diagnostics、Low/Rejected proposals，以及可选
   Excel Unmatched/Conflict/Error 行；每项保留稳定 TrackingKey/ReviewItemId、严重度、
   恢复建议、上游证据 SHA、SourceRef/Preview ID/LogicalId 和精确空间位置。
3. 构建器重新验证每条输入链并失败关闭：跨租户、跨模型、跨楼层、旧编辑器修订、
   同修订不同内容哈希、输入哈希篡改、重复身份、非法位置、超量工作区均被拒绝。
4. 后继工作区会把已经消失的 TrackingKey 标为 Resolved，并记录首次解决来源工作区；
   同一 TrackingKey 再次出现时恢复为 Open。状态迁移不回写或改写上游事实。
5. 查询默认 50、单页最多 200，支持 status、severity、kind、精确 SourceRef、全文关键字
   和 only-locatable；排序、摘要、空字段省略及 SHA-256 序列化保持确定性。
6. 新增开发 CLI：`build-dev-cad-review-workspace` 与
   `query-dev-cad-review-workspace`，支持可选 Match Preview 和 Previous Workspace。
7. Design V1 新增只读问题面板。默认显示 Open，可切换 Resolved，并显示 Blocking、
   Warning、Info、可定位/不可定位计数；过期工件会明确告警并禁用定位。
8. 点击可定位问题时，优先按 LogicalId、其次按精确 SourceRef 选中已应用 Rack/Element；
   同时使用 Bounds/Anchor 计算安全缩放与居中视口，并让底图、设计对象层和问题覆盖层
   共用同一 pan/zoom。零面积 CAD 实体仍显示最小 18px 锚点，用户可手动重置视图。
9. 浏览器只做有界结构、摘要和当前场景新鲜度校验；规范 SHA 重算与真实性验证仍由
   C# 生产器/验证器负责。本地文件导入不能替代生产服务端的权限、审计和工件签发。

## 样例 13 连续证据

输入为仓库合成开发语料 `13-automated-warehouse.dxf`，使用 E02-S07 已封存诊断索引和
E03-S04 编辑器快照构建两次独立工作区。本样例故意不附加 Match Preview，用来证明
E04-S05 对 E02-S07 的最低依赖；Excel 异常行路径由应用单测和 CAD CLI 连续测试覆盖。

- Source SHA-256：`aa573f04e39345b4e03bfce9304f0916a973da47f1ab19b8e17645bc731fb106`；
- CAD IR SHA-256：`b6aa6501ea67e9e3b9622c8838eb28b4e3569ca79f777ff77fe44987e0614310`；
- Coordinate Transform SHA-256：`b1223a8f2406ac28023d35d300adb6b729403cd1bdb85b12eaba58ae2353cfba`；
- Inventory SHA-256：`634329583747825b5c40c37402e03cdfa046c6f3e54f3d0ae2a4eb8faa9697a9`；
- Profile Definition SHA-256：`732eef8a1014e35428d427c639dce4936936087a08c484b23d67275c08de59d1`；
- Mapping Preview SHA-256：`98a0a3153af112563a3075dd9ee9fff1f113d122d22f03b89b399ba04d8009ca`；
- Semantic Preview SHA-256：`e398d192aa4d7f8cb5e92c18ac60dd6ae2ea667a338ee1e99eece0f39befc866`；
- Diagnostic Index SHA-256：`f0d18f95b144a4b4b8b503f9d6665528a25816b5d399eb8c9d0f18c17209448b`；
- Editor Snapshot SHA-256：`e64aed71eb0d7b8253ea440e4b477f15d54ca4103911f60fa8ff7ba79e8a9948`；
- Review Workspace SHA-256：`3a2962288b658d60f7810a60a95a54af1d0744da7943e688686e8d57b17288eb`；
- Workspace JSON 文件 SHA-256：`29ff00143380c6bd1aabf86da60a2b55d688b3b34d35610730b14ca8d3f6eeb3`，
  34,843 bytes；两次独立运行文件哈希完全相同，且没有序列化 `null` 字段。

结果为 29 项 Open / 0 项 Resolved，其中 Info 12 / Warning 17 / Blocking 0；
25 项可定位、4 项不可定位；21 项 CAD diagnostics、8 项 proposal reviews、0 项
Excel reviews。LowConfidenceProposal + locatable 查询返回 8 项；Open + Warning +
locatable 查询返回 17 项。当前 CLI 已重新反序列化、完整验证并查询该工件。

## 门禁

- E04-S05 应用聚焦测试：5 passed / 0 failed / 0 skipped，覆盖诊断/提案组合、可选 Excel
  异常行、Resolved/重开迁移、跨租户/旧修订/同修订不同哈希、确定性、篡改和分页上限；
- CAD Review 前端与共享画布聚焦测试：15 passed / 0 failed / 0 skipped；
- Space Unit 全量：341 passed / 0 failed / 0 skipped；
- CAD 实验工具全量：23 passed / 0 failed / 0 skipped；
- 前端全量：126 files / 685 tests passed；`vue-tsc --build` 与 Vite production build 通过；
- 功能树与 no-ff 合并树的完整 solution Release 非增量单线程构建均为 0 error /
  10 条既有 warning，Desktop 与 Android 原生 AOT 强度保持不变；合并态再次通过
  Space Unit 341/341、CAD 工具 23/23、前端聚焦 15/15 和类型检查；
- 受影响 C# 文件 `dotnet format --verify-no-changes`、Schema JSON 语法、生成工件的类型
  反序列化/应用验证、空字段省略和 `git diff --check` 通过；
- `i18n:check` 仍报告仓库既有 908 个缺失快照 key，与本切片前基线一致；本切片没有
  扩大该计数，但该仓库级债务尚未清零。

## 正式边界与下一步

正式 E04-S05 仍等待生产 CAD Artifact/Issue API、权限与审计策略、服务端权威
Workspace 签发、真实授权图纸和真实编辑器验收。当前 UI 的本地 JSON 导入只用于开发，
不会证明来源可信，也不会触发修正命令。

本切片为后续 E03-S05 用户确认/幂等 Draft 写入提供了问题查看与定位基础，但 E03-S05
不能在生产 CAD 链和并发内容修订门禁解除前伪装为正式完成。下一步应先更新总体状态，
重新盘点仍可独立推进的 E02/E03/E13 主链开发切片，再选择不依赖外部 CAD 许可的最高
价值工作继续。
