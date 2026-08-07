# E06-S02 版本差异与影响预览开发报告

日期：2026-08-07
状态：已 no-ff 集成
集成基线：`05c1df86b2125e8872c38927b0547670555517e4`
功能分支：`codex/space-e06-s02-publish-preview`
功能提交：`a174f7cc2ea93b30377b1e424ba8294f5d751931`
no-ff 集成提交：`5bd2c6169f108d5b591377560664f31deb6c8520`

## 1. 本卡边界

本切片只交付 E06-S02：根据当前权威 Published 指针与目标 Draft/Ready 版本，生成只读、确定性的版本差异和
WMS 影响预览。预览绑定当前 ContentHash、已完成 ValidationRun、规则集、WMS AdapterId 与
CapabilityHash，并形成稳定 PlanHash。

本卡不持久化 PublishPlan，不排队发布，不调用 WMS，不切换 Published 指针，也不写运行态。
不可变发布计划、仓库级发布 Saga、WMS 回读验证、激活、失败对账、重试和回退分别属于 E06-S03～S05；
发布管理 UI 属于 E06-S06。

## 2. 确定性差异与计划哈希

- `space-publish-plan-v1` 按 ObjectType + LogicalId 稳定排序比较 Floor、Zone、Aisle、Rack、RackLevel、
  Location 与 Element。
- 每个对象将生命周期、主数据、几何、WMS 投影、来源血缘和外部绑定规范化为 canonical JSON 与 SHA-256；
  JSON 属性顺序和等价数字格式不会制造伪差异。
- 动作分类为 `Create`、`UpdateMaster`、`UpdateGeometryOnly`、`Disable`、`Restore`、`NoOp`。
- WMS 影响分类为创建、更新、停用、恢复、无操作、仅运行态与重命名阻断；Location 改码明确失败关闭，
  已采纳并绑定的存量 WMS 库位不会被重复创建，纯几何变化不会产生 WMS 写入。
- PlanHash 绑定目标/基线版本、ValidationRun、ContentHash、Adapter/CapabilityHash 与所有有序计划项；
  纯 WMS 投影变化也进入对象 PayloadHash，并归类为 `UpdateMaster`，避免漏掉下游更新。

## 3. 权威读取与可发布判断

- 服务端重新读取目标版本并计算权威 ContentHash，不接受客户端提交差异、哈希或校验状态。
- 只选择与当前内容、`space-validation-rules-v1`、AdapterId 和 CapabilityHash 完全匹配的最新终态
  Passed/Blocked ValidationRun。
- Passed 必须与 Ready 版本上冻结的 ContentHash、ValidatedHash、RuleSetVersion 和 WmsCapabilityHash 一致；
  Blocked 只允许对应 Draft。证据漂移、旧 Published 指针或跨模型基线均返回 409。
- 基线严格来自同一模型的 `CurrentPublishedVersionId`，且必须仍为 Published；不存在当前 Published 时按首次发布预览。
- `Publishable=true` 仅当 ValidationRun Passed、BlockingCount=0、目标为 Ready 且计划没有阻断影响。
- E06-S01 来源规则同步修正：Editor/Template 无文件内建来源的合法终态为 Ready；DWG/DXF 等文件来源仍必须完成
  PreviewReady/Imported，并继续校验 SHA、CAD 单位和正比例尺。

## 4. API、权限、审计与分页

- `GET /api/space/design/v1/versions/{versionId}/publish-preview`
  - 权限：`space:model:read`
  - 读审计：`space.publish-preview.read`
  - 资源：`ModelVersion`
- 支持 `floorLogicalId`、`objectType`、`action`、`impactCode`、`includeNoOp`、`limit` 与受保护游标。
- limit 默认 100、最大 500；游标绑定 PlanHash、全部筛选项和页大小，计划或查询条件变化后旧游标失败关闭。
- 返回整体变更/WMS 影响汇总、匹配数量、分页计划项、阻断标记和 NextCursor。
- 标准 400/401/403/404/409/422/500 Problem Details、OpenAPI、C# SDK 与 TypeScript SDK 已同步。

## 5. 持久化边界

本卡没有 EF 模型、Migration 或增量 SQL 变化。预览只查询现有 Model/Version/Revision/Source/Attribute、
ValidationRun 和 WMS Adoption 数据，并执行读审计；计划不会保存到数据库。`has-pending-model-changes` 返回
无模型漂移。

## 6. 验证证据

| 门禁 | 结果 |
|---|---|
| 校验引擎 + 发布计划引擎聚焦 | 17/17 passed |
| Controller、权限、OpenAPI 聚焦 | 55/55 passed |
| E06-S02 真实 SQL：确定性/筛选/租户隔离、Blocked rename、已采纳库位 | 3/3 passed |
| Space Unit 全量 | 448/448 passed |
| CP6.Tests 全量 | 2794 passed / 17 environment-gated skipped / 0 failed |
| 默认 Space Integration 全量 | 259 passed / 89 SQL-gated skipped / 0 failed |
| 完整 `CP6.slnx` Release 双架构 AOT 构建 | 0 warning / 0 error |
| EF pending model changes | none |
| OpenAPI/C#/TypeScript SDK drift | passed |
| `git diff --check` | passed |

本机沙箱首次阻止读取 ASP.NET Data Protection 测试密钥和用户级 NuGet 配置；在获得只读权限后，原样重跑
CP6.Tests 与 SDK/构建门禁均通过。Android Release 双架构 AOT 在仅约 3.8 GB 可用内存时并行原生汇编器崩溃；
清理 Mobile Release 中间产物并使用 Android SDK 自带的顺序 AOT 开关后，保持双架构与 AOT 强度不变，完整
solution 在 0 warning / 0 error 下通过。

真实 SQL 使用同机健康的 `cp6-db` SQL Server 容器；凭据只从容器运行时读取，未写入日志、源码或文档，
测试创建的临时数据库均已删除。

验证和 no-ff 合并后，删除受控工作树内 36 个可重建 `bin/obj` 目录，共 6,578 个文件、
1,621,278,419 bytes（约 1.51 GiB）；源码、生成 SDK、报告和 Git 历史均保留。

## 7. 尚未完成与下一步

1. E06-S03：持久化不可变 PublishPlan，基于已冻结的 PlanHash 启动仓库级发布 Saga；WMS 成功并回读验证后才能激活运行态，部分或不确定结果必须进入可恢复对账。
2. E06-S04：发布队列、超时、人工重试与审计；失败时继续保留当前生产版本。
3. E06-S05：以历史版本创建新的发布动作实现可审计回退，不删除历史。
4. E06-S06：预检、差异、审批、进度、失败原因和回退入口 UI。
5. 生产 Hosted Space Worker、正式 CAD Provider/授权黄金集、E03-S04 权威 Match Artifact 与 E03-S05 写入链，
   以及 Beta/GA 跨职能证据仍是独立缺口。

本卡不是完整 E06、Beta 或 GA 发布签收，也不得把只读预览的 PlanHash 当作已经持久化或已经执行的发布计划。
