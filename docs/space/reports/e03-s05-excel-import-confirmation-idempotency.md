# E03-S05 Excel 导入确认与幂等写入开发报告

日期：2026-08-08
基线：`cdc629ea`（`integration/space-v1-20260730`）
功能提交：`4e92e435`
验证报告提交：`d048e01a`
no-ff 集成提交：`f735747a`

## 交付结论

E03-S05 已完成应用内正式开发切片：用户必须在权威 Excel–CAD 匹配结果中显式点击确认，服务端才会创建 `ExcelCadApply` Job。后台执行器重新打开并校验 E03-S04 的私有 Match Artifact、Excel 来源、映射方案、Draft ContentRevision 与 Floor 修订；只有全部权威输入一致，才在一个数据库事务中写入当前 Draft。

重复确认、HTTP 重试、Job 重放以及“数据库事务已经提交、Job checkpoint 尚未保存”这类崩溃窗口，都会复用同一个确定性 CommandBatch，不会生成重复货架或重复提升修订。任何 Artifact、哈希、来源、版本、Floor 或 ContentRevision 漂移均失败关闭，并保持零部分写入。

## 本次实现

1. 新增确认与查询 API：
   - `POST /api/space/design/v1/versions/{versionId}/excel-cad-matches/{matchJobId}/confirmations`
   - `GET /api/space/design/v1/versions/{versionId}/excel-cad-matches/{matchJobId}/confirmations/{applyJobId}`
2. 确认请求必须包含显式 `Confirmed=true`、Match Artifact ID/SHA、ExpectedContentRevision 和幂等键。浏览器不能提交匹配行、目标对象、LogicalId 或待写命令。
3. 创建服务只接受 E03-S04 产生的唯一权威 Match Artifact，核验 Tenant、Site、ModelVersion、Job、Schema、SHA、映射方案和来源血缘；外部 Customer、Supplier、3PL 主体在数据访问前拒绝。
4. 新增 `ExcelCadApply` Job、30 分钟处理器、服务与步骤执行器，复用现有 Job Ledger、Attempt、Step、租约、重试、CommandBatch、Source、Version 和 Floor Revision 表，不新增数据库表或 Migration。
5. Worker 重新打开 Artifact 和 Excel，使用与 E03-S04 相同的规范化工作簿投影和匹配算法重新计算结果；Artifact 内容或 Excel 字节发生变化时拒绝写入。
6. 写入事务采用 Serializable 隔离：重新校验 Draft/ContentRevision/Floor Revision 后，创建或更新货架、关联 Excel Source、记录命令批次与命令、提升一次 Floor Revision 和一次 ContentRevision，并把来源置为 Imported；任一步失败则整批回滚。
7. 新对象 LogicalId、逐行命令 ID 和 CommandBatch ID 均由冻结输入确定性生成。相同幂等键重放、不同幂等键重新确认同一已应用 Artifact，以及 checkpoint 恢复都会返回同一 Apply Job/CommandBatch。
8. 编辑器权威匹配面板新增“确认写入当前 Draft”按钮、Apply 状态与成功提示。页面只在 `CanConfirm=true` 时开放确认，不会在预览、加载或定位时自动写入。
9. OpenAPI 操作数由 113 增至 115，C# 与 TypeScript SDK 已同步并通过漂移检查。

## 当前正式写入范围

本切片只对已匹配且可确定性复核的 `Racks` 工作表行执行写入。若规范工作簿中的 Zone、Aisle、RackLevel、Location 等其他目标工作表存在数据，执行器返回稳定的 `SPACE_EXCEL_CAD_APPLY_SCOPE_UNSUPPORTED`，不会静默忽略；非空 `RackTemplateCode` 同样失败关闭，直到模板权威解析链另行实现。

这一边界保证本卡满足“预览确认后才写草稿、重复确认不生成重复对象”，但不宣称完整 Excel 模板的所有层级对象已支持导入。

## 信任边界与故障语义

- HTTP 只负责确认并排队，真正写入只发生在后台执行器重新校验之后。
- Apply 只消费服务器私有存储中的 E03-S04 Artifact；客户端不能替换结果、哈希、目标或命令。
- Conflict、Error、Unmatched、阻断项、Artifact/来源/方案/修订漂移、非 Draft 版本、未知或歧义批次均失败关闭。
- CommandBatch 是数据库提交事实。若 Job 状态落后于已提交批次，重放会读取该批次并补齐 Job checkpoint，不重复执行写命令。
- 成功写入后允许查询和幂等重放，即使 Draft 后续继续变化；但不会使用旧 Artifact 再创建第二批对象。

## 验证证据

- E03-S05 API、权限与 OpenAPI 聚焦：62/62 passed；
- Apply 服务、原子写入、漂移和处理器注册聚焦：8/8 passed；
- Match/Artifact/Job Processor 单元聚焦：27/27 passed；
- Space Unit：464 passed / 0 failed / 0 skipped；
- 默认 Space Integration：270 passed / 0 failed / 94 SQL-environment-gated skipped；
- CP6.Tests：2808 passed / 0 failed / 17 environment-gated skipped；
- 前端聚焦：7/7 passed；前端全量 132 files / 705 tests passed；
- TypeScript 类型检查、前端 production build、OpenAPI/C#/TypeScript SDK drift、受影响 C# whitespace 和 `git diff --check` 全部通过；
- 完整 `CP6.slnx` Release 单线程构建：0 error / 10 条既有 warning，含 Desktop 与 Android 双架构 AOT。首次完整构建在第三方 Kotlin Serialization 的 Android x64 AOT 工具处瞬时崩溃；未修改代码或降低 AOT 强度，清理可重建缓存后独立 Mobile Release 0 warning / 0 error，随后同一完整解决方案命令通过。
- 集成并推送后清理 38 个可重建 `bin/obj/node_modules/dist` 目录、28,788 个文件、1,544,566,969 bytes（约 1.438 GiB）；源码、锁文件、报告和远端 Git 历史不受影响。

## 尚未解除的边界

E03 的五张应用内开发卡现已形成“模板/映射/预检 → 权威匹配 → 显式确认 → 原子幂等写入”闭环，但这仍不是生产 CAD/Excel 正式签收：

1. 生产 WebApi Hosted Worker 当前只认领 Publish、Reconcile 和 HistoricalRepublish；`ExcelCadApply` 仍需接入并部署生产后台 Worker Host，才能在生产环境自动消费 Job。
2. 正式 CAD Provider、组织授权的真实 DWG/DXF 黄金集、真实大文件/异常/性能证据仍需外部环境提供。
3. Zone、Aisle、RackLevel、Location 等工作表以及 `RackTemplateCode` 的权威解析与写入尚未纳入本卡。
4. 本卡未调用 WMS、未发布版本，也未改变 `main`；版本校验、发布与 WMS 同步继续由 E06 权威链负责。

