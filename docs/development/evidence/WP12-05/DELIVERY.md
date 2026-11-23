# WP12-05 表单目录收藏、历史查询和最终下载披露

候选已在 Windows 本机完成验证，待总控独立验收。执行为 CP6-EXEC-2，主机 LAPTOP-3QQ44FJS，Main DEV-1 工作树 `D:/CP6-worktrees/wave17-dispatch-20261018`，任务分支 `codex/wp12-05`。领取基线 `4d31801d0456064ae38cf367759d38000be286e2`；受验源码 `e27faa22005fb657815ccd459db536da5f7d2b0f`，Git 树 `cf6c0a769d065c7bbb6d875bebc9ba5a4d1f7a7a`。按原定义复算 22 项严格前置完整 DONE。 [原卡](../../task-plans/WP12.md#wp12-05) · [正式 DECISION #416](https://github.com/GTX537/CP6.AI-Handoff/blob/451a66f2ca73d725195a380edce9fb90c6d8951e/handoffs/CP6-DEV-20261012/tasks/COORD/replies/20261123T114542Z-cp6-exec-2-wp12-05/MESSAGE.md) · [远端 START](https://github.com/GTX537/CP6.AI-Handoff/blob/03747b4d74086d545818ee790e09787b9118c65c/handoffs/CP6-DEV-20261012/tasks/WP12-05/messages/20261123T115557Z-cp6-exec-2-start/MESSAGE.md)

## 行为和边界

- 目录继续按已选 Published 版本、当前启用状态及资源准入筛选。收藏沿 Scope、实际用户和表单键作幂等赋值，原命令可恢复。当前 Scope 暂停时只显示本人的收藏键及不可用占位，恢复后仍以实时发布为准。进入填写时重新读取 `initiation-context`。
- 旧流程列表及详情读取各实例记录的版本快照。缺少快照时名称、版本号和版本 ID 返回 `null`，页面明示历史版本未证；旧系统回填有独立来源。旧列表按时间和 ID 排序，查询失败或请求过期不再保留旧结果。原生查询继续在当前授权集内筛选、计数和分页，精确显示旧发布版本和 `SubmittedUnbound`；OA 分列显示表单或业务提交、审批和 Owner 应用。
- 附件通过既有 FORM/FILE 最终下载入口。用真实 HTTP 及已打开的 Chrome 历史页验证，当前对象、租户、查询权、字段及 FILE 保留状态控制字节披露。另核当前 FORM 与旧查询路由及 PRIV 注册源。Main 的主体导出源是身份、安全及角色资料，本卡入口没有可采用的 FORM 导出路由。

## 本机证据

用户、租户、表单数据、WMS 和受控文档业务记录均为合成数据。PostgreSQL 18.6、Main Kestrel（net8.0）、Google Chrome 157.0.8103.78 和 Microsoft Defender 4.18.26100.4 实际运行。构建使用 SDK 10.0.302 的锁定还原；三份产品/测试程序集均带上述受验源码完整版本。各测试库使用独立 loopback 端口及数据目录，完成后已停库。仓内无本卡新增迁移。

本机证据根目录为 `C:/Users/tt/CP6-test-artifacts/WP12-05/20261123-wp1205`。`technical-summary.json` 保留各次运行的时间、合成环境、通过数与文件摘要。复跑使用该目录的 `run-pg.ps1 -Run <新运行名> -Filter <过滤条件>`，实际浏览器加 `-Browser`，FILE HTTP/浏览器按本机 `CP6_FILE_DEFENDER_EXE` 指向 Defender。构建为 `dotnet build CP6.Tests/CP6.Tests.csproj --artifacts-path <证据根目录>/dotnet -p:RestoreLockedMode=true -t:Rebuild -v quiet`。

| 证据文件 | 结果及范围 |
|---|---|
| `build-e27faa22.log`、`candidate-assembly.json` | 退出 0。`CP6.Core`、`CP6.WebApi`、`CP6.Tests` 精确标识受验源码。保留既有编译和依赖警告。 |
| `candidate-api.trx` | 29 通过、零失败或跳过。过滤 `PersonalCatalog_`、`PersonalFavorites_`、`NativeQuery_`、`FormFiles_`、`LegacyActor_HTTP_`、`InboxQueryPagingTests`。SHA-256 `c75e3a9dcd4fde76e4720672de40d766ba9d74d97b217710ea14f9b7e1f89748`。 |
| `candidate-form.trx` | 31 通过、零失败或跳过。过滤 `PersonalForm_`、`LegacyPersonalForm_HTTP_`、`PersonalSnapshotRecovery_HTTP_`、`UnifiedTask_HTTP_APP_keeps_accepted_command_identity_and_frozen_subject_without_patch`。SHA-256 `eba792d748f673a293c564ec7a49f5309ce1991473d54197573b585e1d26476f`。 |
| `candidate-chrome.trx` | 2 通过、零失败或跳过。`NativeBrowser_actual_favorite_lost_response_recovery_and_bound_unbound_pinned_history` 和 `NativeBrowser_actual_FORM_upload_unknown_recovery_draft_submit_and_history_download`。SHA-256 `150fa7771d02e9d39bf5dd0ec614e72416919c0d1d8e2554b85e224420099dfb`。 |
| `permission-consumption-wp1205-candidate-chrome` | `catalog-history-browser` 和 `form-files-browser` 的 `result.json`、截图与浏览器日志已复核；两条流程均通过真实 Main API。限定注入一次已提交收藏或上传的响应丢失；显示原结果，下载比对全部字节。隐藏附件字段后实际端点返回 403，旧详情清除，零新增浏览器下载。 |
| `vitest-c415d6c7.json`、`vue-tsc-e27faa22.exit` | 定向 Vitest 8 文件、45 项通过。复用前一源码提交的结果，因 `cp6.web` 树 `4a8c86f367a72bc5e228919b7a8b67a3a9570290` 完全相同。当前受验源码执行 `node node_modules/vue-tsc/bin/vue-tsc.js --build --force`，退出 0。 |

复核关键负例和后续正常路径。旧历史同一时刻的三条合成记录在分页中可完整读回，未授权账号无记录或计数。APP 审批已通过、业务已执行但原回执还未进入查询投影时，仍为 `UNPROVEN`；采用真正文档 Owner 产生的连续原回执后才返回 `APPLIED` 和准确结果引用。已列出的附件在保留锁下拒绝下载，解除锁后仍返回相同字节。实际下载当前权限拒绝保留普通错误响应，无附件头或摘要。正向响应验证 `no-store`、摘要和原件。

首次基线隔离数据库因 D 盘空间不足在初始化阶段失败，留存 `baseline-scoped.trx` 和 `baseline-initdb.log`。迁出本卡临时产物后，同机隔离基线复跑 13 项全部通过。候选初次编译缺测试命名空间导入；已在受验源码修复并重建。旧详情截图仍可见既有 `oa.detail.*` 本地化键，新增版本来源和状态为简体中文。

## 原 OA-FORM-01 的 35 项验收核对

以下为执行方对应证据，待总控逐项确认。领取台账原 AC 为 0/35，且没有单列的必需运行时修订。复用 WP12-03/04 依据已采用的 W319 清单及其原 W275、W278、W285、W287、W293、W302–W304、W316–W318 证据；已核 W319、W281、W285 的采用提交是领取主线祖先。历史证据保留原运行环境和范围。 [原 AC 目录](../../assessments/20261011/catalogs/engineering-oa.json) · [W319 逐项承接](../W319-01/DELIVERY.md) · [W281 原生历史](../W281-01/DELIVERY.md) · [W285 FILE](../W285-01/DELIVERY.md) · [FILE](../WP02-14b/DELIVERY.md) · [PRIV](../WP02-15/DELIVERY.md)

| 原 AC | 当前候选或已采用证据 |
|---|---|
| AC-F01-01 | 隔离 PostgreSQL 的目录用例确认发布已选版本、启用及角色准入都在计数和搜索之前。实际 Chrome 从目录进入时发起新请求并核对最新发布 GUID。 |
| AC-F01-02 | 收藏双连接赋值汇聚为一条记录，重放保持原结果。不同实际用户与租户使用同一表单键各自隔离。 |
| AC-F01-03 | 停用发布和暂停当前 Scope 后，实际 HTTP 收藏只返回本人保存的键及空发布信息；发起上下文拒绝。字段和附件继续按当前权限读取。 |
| AC-F01-04 | 实际 HTTP 对失效收藏连续取消成功；Chrome 验证断响应按原命令恢复再取消，组件验证身份变更清屏并丢弃迟到响应。 |
| AC-F02-01 | W275、W278 与本轮个人表单回归确认准确 pin、本人唯一 Active 草稿，创建不产生流程或任务。 |
| AC-F02-02 | W275、W285、W287 与 BUG756 的 partial、类型、子表、选项和文件证据；本轮复跑本人草稿与旧 HTTP 数据限额。 |
| AC-F02-03 | W275、W316、W318 的本人/租户/代理控制，及本轮旧 HTTP 会话与资源拒绝回归。 |
| AC-F02-04 | W275 双连接 CAS、W278 浏览器、W316 旧入口和本轮草稿回归对应旧 token 拒绝、保留输入与新值。 |
| AC-F02-05 | W319 已采用草稿摘要和 W318 旧内容当前读权。本轮 HTTP 检查历史投影、字段遮蔽、同租户非参与者、跨租户及下载拒绝；Chrome 验证撤权清除旧详情。 |
| AC-F02-06 | W275 与本轮停用上下文回归对应旧草稿按原 pin 编辑、保留 Active，以及提交拒绝。 |
| AC-F03-01 | W275、W278、W304、W316 与本轮 HTTP bound 草稿提交，固定原版本、唯一 FormData 与实例、同事务 Submitted。 |
| AC-F03-02 | W275、W316 及本轮提交原命令回归，核对同输入原结果、异输入拒绝、原键和各 ID。 |
| AC-F03-03 | W304 五前驱不匹配与 W316 旧 HTTP DTO；本轮上下文变化、封闭 DTO 与保留 Active 回归。 |
| AC-F03-04 | W304 真实人工任务落库失败的回滚，与本轮提交失败、最终权限变动及延迟约束失败回归。 |
| AC-F03-05 | W275、W316 及本轮 `SubmittedUnbound` HTTP 和 Chrome 验证显示表单已提交、未启动审批，准确版本只读。 |
| AC-F03-06 | W275、W316 与本轮不同实际用户旧同键、角色/字段投影和附件当前授权回归。 |
| AC-F03-07 | W275、W278、W304、W316 的原键/断响应/单实例证据；本轮真实 HTTP 撤销写权保留当前读权时查原提交结果，以及隔离提交回归。 |
| AC-F03-08 | W275、W282 的 FORM/APP 边界及本轮统一 APP 正常任务入口回归拒绝表单 patch。实际业务 Owner 产生的应用回执单独接收并呈现。 |
| AC-F04-01 | W293 的同名同类型、默认值、精确版本和逐字段诊断，W303 处置修订及本轮重基回归。 |
| AC-F04-02 | W293、W275 与 W303 对照带值损失、精确预览摘要和隐藏值受控处置；本轮重基及旧 HTTP 回归。 |
| AC-F04-03 | W293、W302、W303 的子表列损失、限制缩小保行、隐藏损失和真实处置证据，复用未改写的调用链。 |
| AC-F04-04 | W275、W293、W303 和本轮重基过期/上下文停用回归覆盖源、目标及策略变化后的冲突和原值保持。 |
| AC-F04-05 | W293、W304 的新必填留草稿与最终提交校验，复用未改写的规则/重基内核及准确路径证据。 |
| AC-F04-06 | W275、W303 相同原命令只生效一次；W317、W318 与本轮恢复和旧 HTTP 重基回归保留版本和回执。 |
| AC-F05-01 | W275、W316 和本轮本人与已提交草稿墓碑回归，原 FormData 与流程不受删除命令影响。 |
| AC-F05-02 | W275 与本轮双连接提交/删除竞争只获一个合法终态。 |
| AC-F05-03 | W275、W316 及本轮过时 token、原删除凭证、恢复与回滚回归。 |
| AC-F05-04 | W285、W317/318 与本轮 FILE 保留及草稿删除用例，确认移除表单引用时原件保留、保留锁拒绝交付原件。 |
| AC-F05-05 | 沿用 W316、W318 的实际 Chrome 删除取消/失败/刷新证据；本卡差异未触及该操作的输入。 |
| AC-F06-01 | 隔离 PostgreSQL 的授权集内组合发起人、处理人、状态及日期，计数对应可见记录；另核 TASK 代理的参与范围和当前查询权撤回。 |
| AC-F06-02 | 实际 HTTP 和 Chrome 验证合法无实例记录、无审批状态和只读详情，并交叉验证 `hasInstance` 筛选。 |
| AC-F06-03 | 原生历史保留改名前的固定发布版本；旧 HTTP 列表和详情一致展示固定快照名称，缺失时元数据为 null，回填标来源；实际 Chrome 验证缺失提示。 |
| AC-F06-04 | 关键词验证限流程代码和业务编号，合成字段值不影响匹配；当前遮蔽字段及附件、不同租户 ID、无对象权账号均不能扩大历史/下载披露。 |
| AC-F06-05 | 实际租户时区、UTC 半开日期边界、25 小时夏令时回拨日、带准确截点的翻页、延后写入，以及页大小 100 边界。旧列表同时间行按 ID 稳定排序。 |
| AC-F06-06 | 隔离库真实 APP 的批准保留未证；WMS 原回执有缺口时不提前采用，实际文档 Owner 的应用结果在原回执进入投影后才显示 `APPLIED` 和结果引用。Chrome 与组件核对状态分栏。 |

## 工时和交接

- 领取起点 `2026-11-23T11:53:35.6254830Z`，技术检查点 `2026-11-23T12:28:16.0368353Z`，本机有效技术工时 **2080.411 秒**。并行本地用例与证据核对按同一连续时段计入一次。额度 14400 秒，强制拆卡 21600 秒。草稿准备及交接另据 Handoff 实际快照计时。
- 已核本基线未出现 WP12-17 尾差源 `f90439b8cee0516d59ad2467908330e8f2db63cf`。其 **616.455 秒**在后续正常合资格 DONE/拆卡 PR 按同一完整 SHA 和原路径查重后单次结转。本草稿不写台账。 [前卡原 DELIVERY](https://github.com/GTX537/CP6.AI-Handoff/blob/f90439b8cee0516d59ad2467908330e8f2db63cf/handoffs/CP6-DEV-20261012/tasks/WP12-17/messages/20261123T112101Z-cp6-exec-2-delivery/MESSAGE.md)
- Main DEV-1 保留本卡候选供总控审阅。正式远端采用、独立签收及 Main 席位释放待总控决定。生产运行与跨系统 Owner 采用未在本机验证。交接回执记录远端候选和报告回读。
