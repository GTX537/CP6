# W43-01：Main 原命令日志与 PA090 恢复首批交付

七天一期D4先行 / WP02-10限定窗口；COORD本聊天单人。Main基线`c91a50796a4bdb2acf4360b74900aee445e3ffc6`，固定DEV-1/codex/w43-01、DEV-2/codex/w43-handoff。产品及Handoff已采用，原分支最后文档/实际finish见下文。

## 行为与审查

有版本PA090命令采用非空固定UUID；同一实际CP6Context/Npgsql事务保存价格/兄弟传播、必要workflow intent、不可变原输入/结果、效果对象和价格前后审计。重放先查原操作，准确同体通过当前读取授权后回原结果，不重新应用旧ETag或要求新写权限；异体冲突、跨actor拒绝。GET原结果不修改业务/receipt/audit/权限revision；UNKNOWN不推定未执行，显式close与新写共用原权限根，防止迟到生效。

新PG迁移`20261022061500_MainOwnerOperationsV1`只创建新schema/table/guard，不回填旧数据。读恢复使用现有根行FOR SHARE，写/close保持原根UPDATE；没有新建平行锁命名空间、全库tenant schema迁移或强制改动旧Writer。页面实际改用新命令，服务端当前tenant/actor绑定浏览器锁与公共UUID记录；刷新/多次点击/账号切换/迟到响应安全恢复，不保存价格草稿、不自动重发。

一次作者完整diff审查包含：原输入精度/顺序与codec版本、原操作归属/对象/字段当前授权、回放不要求写权限、close竞争、原生句柄/事务、审计/业务/意图回滚、HTTP真实登录/CSRF/no-store、原兼容入口范围、浏览器故障及历史证据。新并发夹具曾错误共享HttpContextAccessor的可变holder，改为独立请求ExecutionContext；页面确认框跨身份清空新提示的问题已修复并定向复验。没有本窗剩余合并阻断，无审查代理。

合同与writer边界见 [Main journal v1](../../../../design-handbook/remediation/main-owner-operation-journal-v1.md)。原接口`batch`/IOrderService保持历史非恢复合同；新命令只在PA090页面与新API实际采用。写命令准确重放仍会消费技术权限根revision，不能说全库零写。纯GET应用只读使用行锁，不能冒称PostgreSQL READ ONLY或Platform Inbox的采用。

## 实际验证

43项去重相关.NET通过（11项新增PG/真实HTTP，32项既有PA090权限/范围/精度/竞争回归），16项Vitest通过，完整vue-tsc -b通过。精确批次及用例来源`D:/CP6-local-test/wave43/foundation/final-validation.json`。

|批次|结果及范围|
|---|---|
|original-20261022T061333Z|8/8：同键/异体、完整原结果/审计/意图、commit后确认丢失、撤销写权限仍读、最终授权故障全回滚、UNKNOWN与close、当前字段/对象/租户、同键并发、不可变DB guard及实际HTTP|
|original-races-20261022T061714Z|1通过/2失败；other-actor拒绝通过；close竞争夹具上下文互扰，原失败保留，不称全批通过|
|race-diagnosis-20261022T061901Z|2失败，补保留首请求拒绝与后续断言失败，定位共享HttpContextAccessor holder|
|race-final-20261022T062002Z|2/2：隔离真实请求上下文后，close先提交拒迟到命令；命令先提交close只能回原结果|
|regression-20261022T062105Z|32/32：29个明确PA090方法（含参数化），原生精度/字符串/兄弟传播、字段/对象、部门维护消费、最终撤权/取消/丢确认及实际HTTP；相同产物no-build|
|frontend-green / frontend-recovery|16/16；确认跨身份、未知与刷新、查询/封闭、字段隐藏、当前权限、迟到恢复、损坏记录/浏览器锁；最终消息调整后相关组件重验|
|vue-tsc-green|完整类型检查exit0；此后只调整原结果件数/冲突提示并由上述组件复验|

第一次frontend-final为15通过/1失败，旧确认框清空新身份提示，生产代码修复后通过；日志保留。新增去重11项、既有32项，不把重复运行累计为完成。真实HTTP使用本地实际Program和登录，测试DB是隔离PG18，未验证全仓、浏览器E2E、真实DEV/UAT/生产；已有NuGet漏洞/nullable警告不在本窗修订，不冒充正式安全发布。

## 剩余与生命周期

原Workflow仍AwaitingAdapter，Main journal不是Platform Inbox/OutboxV2切换、broker投递、POWER EGG 46字段或任何外部业务成功。完整WP02-10/PLT-DEL、其他Owner writer、RMA MainUow/收货适配、REP Transfer及SCHED资源重绑、正式数据库角色/政策/保留与真实验收都保持未完成。七天目标D4首批完成后继续；D2/D3不因依赖顺序调整而勾选。

自有PG/API停止；5个数据目录按ownership/绝对路径/无进程及重解析点核验后回收571773920字节；原日志/TRX/失败/ownership和缓存保留。分支正常采用后准确删除、固定席位finish；当前仍占DEV-1/2、集成席位空闲。无新树/代理/Actions/既有环境迁移/生产部署，无触发修改/取消项，125目标/247运行与整卡完成数不提升。预计2–4h，实际按最终finish校正。

## 远端采用与原分支收尾

[产品PR #397](https://github.com/GTX537/CP6/pull/397)正常合并，source`1ed6753ee3e946e6c6eb44ae58c8c0ccb598f7e0`、远端main`8d7001fc6c6b537bcba35b50ad0fc9941bb07e8d`，23项任务文件blob一致。[Handoff PR #19](https://github.com/GTX537/CP6.AI-Handoff/pull/19)采用source`fe53b4b7cfa45a27ea3897135924c4755ff7cdc7`、目标`d9e43509b5bddcd8c9b5f74e39d17a775b736dad`，3项资料blob一致；准确Handoff本地/远端分支已删除，DEV-2干净detached并释放。

43项去重相关.NET（11新增PG/实际HTTP、32既有PA090）、16项Vitest及完整类型检查通过；原失败保留，所需范围通过。合并未改变产品输入，复用真实成功结果；最后文档只查diff/链接/台账。PG/API停止、5处生成data回收571773920字节，原日志和失败保留。本分支Actions0运行，无取消项或触发修改。

此快照 2026-10-22T06:31:27.045360+00:00 仍在原DEV-1/codex/w43-01做最后文档；正常最终PR后manage finish，不提前声明自身采用/释放。准确分支处置、工作区和席位结果及实际耗时见最终PR、实时管理登记与`C:/Users/tt/CP6-planning-reports/20261017-foundation/wave43-final-verification.json`。原估2–4h按真实finish校正。

七天目标ACTIVE，D1完成，D2与D4各首批已采用；继续D4可靠投递/实际Owner前置。旧batch非恢复、workflow AwaitingAdapter与Platform正式采用边界保留，不将局部原日志充当REP/RMA/SCHED的完整共同事务证明。125目标/247运行及整卡不提升，根用户资料保留，无新树/代理/Actions/既有环境迁移/生产部署，W37–W42不重复。
