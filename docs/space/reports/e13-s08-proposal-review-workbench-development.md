# E13-S08 AI 提案审核工作台开发切片

日期：2026-08-05

## 交付结论

CP6 在 E13-S07 集成基线 `d100a956` 上完成功能提交 `b1ab93f6`：新增版本化、只读的
Draft 基线快照和 AI 提案审核工作区合同，确定性的差异投影、筛选、受保护游标分页及批量选择
资格预检；开发 CLI 可封存基线、生成/查询工作区并预检 Accept/Reject；Design V1 编辑器可本地加载
开发工件，查看提案、证据、差异、问题和画布位置。

本切片不创建 Proposal/Decision 数据库记录，不写 Draft、Published、WMS 或设备控制数据，也不提供
Accept/Reject 写入按钮。工作区固定 `IsReadOnlyWorkspace=true`、`DecisionWritten=false`、
`DraftWritten=false`；批量操作只返回资格预检并固定 `RequiresServerRevalidation=true`。

## 基线、身份与防篡改边界

- Draft 基线必须是一个楼层的完整只读投影，绑定 Tenant、ModelVersion、Floor、ContentRevision、
  可选 ContentHash、规范对象顺序和 Snapshot SHA-256；对象身份必须唯一，几何和字段令牌受限。
- 提案集继续由 E13-S07 自身验证器深验；工作区要求提案与基线的 Tenant、ModelVersion、Floor 完全
  一致，绑定 ProposalSet SHA、Baseline SHA、Revision/ContentHash，并生成独立 ReviewEtag 和
  Workspace SHA-256。
- 反序列化工作区会重新验证只读/未写入标记、身份、规范排序、唯一 ReviewItemId/LogicalId、摘要、
  问题、差异、ReviewEtag 和 Workspace SHA；哈希、顺序、摘要或内容篡改均失败关闭。
- 单个工作区最多 100,000 条提案；默认页长 50、最大 200；单次显式或筛选批量选择最多 1,000 条，
  未知/重复 ID、空筛选结果、过量结果和陈旧 ETag 均拒绝。

## 审核投影、差异和定位

- 审核项固定按 High、Medium、Low，再按对象类型和 LogicalId 排序；保留完整字段胜者与 evidence、
  关系、货架派生、问题、SourceRef 和整数毫米几何。
- 相对当前 Draft 基线区分 Added、Modified、Unchanged；几何、字段 Added/Removed/Changed、
  RackLevel 数量和 Location 容量分别展示 before/after，不把变化压成一个布尔值。
- Readiness 由 E13-S07 结果确定：Blocking 问题为 Blocked，`RequiresHumanReview` 为 NeedsReview，
  其余才是 Ready。只有 Ready、High 且提案本身允许批量接受的项目才可进入 Accept 资格集。
- 每条提案携带 Floor、SourceRef、bounds、中心锚点和建议留白；前端复用既有 CAD 画布选择/定位适配器。
  工件与当前 Model/Floor/ContentRevision/ContentHash 任一不一致时标为 stale，并禁用定位与选择。

## 查询、游标与批量预检

- 可按置信度、对象类型、Readiness、差异类型、问题严重度/代码、字段胜者、evidence code、
  SourceRef、关键词和可定位性组合筛选。
- 游标 resource 绑定 Workspace SHA，filter hash 绑定完整筛选条件；游标不能跨工作区或跨筛选重放。
  开发 CLI 使用 32～128 byte 二进制 HMAC key 并在使用后清零；生产必须复用既有
  Data Protection 游标实现及其 Tenant/Actor/Grant/15 分钟保护，不能把开发 HMAC 工具当生产授权。
- 显式 ID 和筛选条件严格二选一。Accept 对 Blocking、NeedsReview、非 High 或明确禁用批量接受的
  项目逐条返回稳定不可用原因；Reject 仍只做选择资格预检，不创建决策。
- 前端本地 JSON 上限 50 MiB，页面固定每页 50 条、选择上限 1,000；面板明确展示只读提示，
  不包含 API mutation 或 Decision/Draft 写入口。

## Sample13 连续证据

输入为仓库内合成开发语料 `13-automated-warehouse.dxf`，不计正式黄金集或生产签收。E13-S07
上游稳定工件为：

- Source SHA-256：`aa573f04e39345b4e03bfce9304f0916a973da47f1ab19b8e17645bc731fb106`；
- CAD IR SHA-256：`b6aa6501ea67e9e3b9622c8838eb28b4e3569ca79f777ff77fe44987e0614310`；
- Coordinate Transform SHA-256：
  `b1223a8f2406ac28023d35d300adb6b729403cd1bdb85b12eaba58ae2353cfba`；
- Inventory SHA-256：`634329583747825b5c40c37402e03cdfa046c6f3e54f3d0ae2a4eb8faa9697a9`；
- Mapping Profile Definition SHA-256：
  `732eef8a1014e35428d427c639dce4936936087a08c484b23d67275c08de59d1`；
- Mapping Preview SHA-256：
  `09882a25f61690b1d42a996e0fd0782b49f3736ee8b3ff8c5804c4c7d553b486`；
- Semantic Preview SHA-256：
  `a777c8d2fd48e428102ac16ab17afa0ad18dd1bc01663573bed5dc125f103c20`。

本轮使用新的临时 HMAC key，因此 Provider Input 之后的 HMAC 相关哈希与 E13-S07 报告不同，这是
按 Run 隔离的预期结果：

- Provider Input SHA-256：
  `22bbddb9f67d59daa68b7fb74adf33654ecf527a951fecec447eb26ba081d59f`；
  local Source Map Canonical SHA-256：
  `d8f54b4719970e74a89dda426fcb80f707f14b7b70d3d9e5bf2606ca51699e74`；
- Provider Output Canonical SHA-256：
  `db8c9c142366c23d0d125e6f3cb695990f723ff1243d71ae72d4402ccff6960e`；
- 21 个唯一只读提案：High 13 / Medium 0 / Low 8；8 个 Rack、24 个 RackLevel、192 个
  Location；Info 9 / Warning 8 / Blocking 0；ProposalSet SHA-256：
  `a63276c4b131aa7cfacfb64cbac2e5903c2569309f96417b64a9b4f25381eaed`；JSON 40,400 bytes，
  文件 SHA-256 `50f15aa60883333250d5625a5415355d7d577f9b5e2dffd4ab6ed4a481574cb4`。

本轮显式封存一个 ContentRevision=0 的完整空楼层开发基线，用于证明 Added 路径和只读边界：

- Baseline Snapshot SHA-256：
  `38d2ca0b840d55c438613adf9db449b44daf672964d5cfc344e163e1aa2b4f12`；JSON 357 bytes，
  文件 SHA-256 `b5d29e6d230fd1064a4894c3fa550db8a0b070dc9e842c9d35e94562b7a33b08`；
- ReviewEtag：`c2d9162373f3ba8d757c9bf5d2c5ecbb361ccfa6c3e117a636156c3e3d6319ec`；
- Workspace SHA-256：
  `2fc473e40d1cc3dfa1205e4f9363c0ca758d31d218b0b09a89aa76c4c5530efd`；JSON 58,645 bytes，
  文件 SHA-256 `f21d06baf668cf35dc89cf418a12cf417816298119750b164f03ab969d14b8d9`；
- 摘要：21 项，High 13 / Low 8，Ready 0 / NeedsReview 21 / Blocked 0，Added 21，
  locatable 21，batchAcceptEligible 0，Decision/Draft 均 false；另有 1 条 run-level 非 Blocking 问题；
- 分页 page1/page2 各 5 项、交集 0，ReviewEtag 和 filter hash 保持一致，page1 正确返回 next cursor；
- `High + DeterministicRule + locatable` 返回 13 项；Accept 预检选中这 13 项但合格 0，全部以
  `SPACE_AI_INDIVIDUAL_REVIEW_REQUIRED` 拒绝；Reject 预检选中 21 项且 21 项均进入资格集；两者
  都要求服务端复验且没有写 Decision/Draft；`High + Ready` 空筛选被 CLI 按失败关闭拒绝；
- ProposalSet 与 Workspace 各独立重复生成一次，Canonical SHA 和文件字节均完全一致。

空基线只是诚实的开发证据，不代表真实 Draft 对比已经验收；Modified、Unchanged、字段删除、
几何和货架容量变化由单元测试覆盖，仍需生产 Draft 快照和授权真实项目验证。

## 测试矩阵与门禁

- E13-S08 后端聚焦：4 passed / 0 failed / 0 skipped，覆盖 Added/Modified/Unchanged、分页游标、
  filter/ETag 绑定、批量资格、上限、篡改与身份错配；
- Space Unit 全量：401 passed / 0 failed / 0 skipped；
- CAD 实验工具全量：25 passed / 0 failed / 0 skipped；开发 CLI 串联聚焦 1/1；
- 前端 E13-S08 聚焦：4 passed；前端全量：127 files / 689 tests；TypeScript type-check 和
  production Vite build 通过，仅保留既有大 chunk 提示；
- 完整 solution Release 非增量、单线程、禁用节点复用/共享编译构建：0 error / 10 条既有
  warning；Desktop 和 Android AOT 强度未降低；
- 受影响 C# whitespace format、两份 JSON Schema 解析和 `git diff --check` 通过。

## 正式边界与下一步

这是 E13-S08 的可执行开发切片，不是正式生产端到端签收。仍需：

- 生产 Proposal/Review 持久化、数据库模型/Migration、租户授权、审计、公共 WebApi、OpenAPI 和
  SDK；当前本地 JSON 导入及开发基线封存命令不是权威服务端入口；
- 从同一权威 Draft 修订生成完整楼层基线，并使用生产 Revision/ContentHash/rowversion 构造
  ReviewEtag；真实场景验证 Added/Modified/Unchanged、分页缓存和大工作区性能；
- 接入 E13-S03 Worker/Run/Artifact、E13-S07 方案/编码/碰撞前置门禁，以及 E13-S05/S06 外部
  Provider 正式证据；本切片不解除任何既有缺口；
- 以授权真实 DWG/DXF、生产 CAD Artifact 和真实 Draft 完成签收，而不是把合成 Sample13
  当作正式黄金集。

下一独立切片是 E13-S09：实现单条/批量 Accept/Reject 的追加式 Decision、rowversion/ReviewEtag
并发控制、服务端资格复验、补丁白名单和审核完成状态；任何接受结果仍不得直接写 Draft，E13-S10
才可在事务内再次验证并原子 Apply。High 自动批量接受继续关闭，直到正式黄金集质量门槛和统计下界
门禁得到验证。
