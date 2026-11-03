# W290-01 统一任务动作上下文与 APP 原命令适配

W290：统一任务动作上下文与服务端SFS/APP分型、APP accepted原命令五字段与同事务WFC alias、真实APP办理页及未知结果恢复已闭环。

Main 基线 `c0fa67eb371e0fcb49f861f7a42ebe71b222213a`，COORD 单人复用 DEV1/Main、DEV2/Handoff。[24小时持续计划](../../planning/20261102-codex-24h-oa.md)继续。采用原 OA-TASK R2 §3、S3 Common WFC 与 accepted OA-APP §4/§5；原 WP12-08 完整卡另作逐项核证。

## 实际采用

`GET /api/oa/v2/tasks/{task}/action-context` 从服务器实际原生来源选择 SFS/APP，返回真实任务/实例版本、SFS 表单数据版本、固定流程/表单版本、token/node/stage/round、实际办理人、当前职责人、最初职责人、当前委派及允许决定。SFS 同一授权快照包含可见表单与固定节点/IAM/委派字段交集；APP 无可编辑表单或 dataPatch，返回冻结业务对象引用和意见规则。GET 不构成最终写授权；决定仍在原 Owner 的最终事务内核对当前业务权限、SOD、职责、grant 和版本。

`POST /api/oa/v2/tasks/{task}/decisions` 同样由服务器分型。SFS 直接采用 W289 的封闭 patch/三版本原子入口，合法通用字符串 commandId 不变。APP 映射准确 accepted 五字段 commandId/decision/comment/taskRowVersion/instanceRowVersion，只接受 APP/同 taskId/APPROVE 或 REJECT/意见/两版本；客户端不能传 actor、dataPatch 或表单数据版本。缺少版本为 428，envelope 与 body 任务版本不同为 400，旧实例/任务版本沿原 APP 412；queryRef 保留原 accepted 命令地址，不替换成另一份结果。

原 OA-APP §4 明确小写 RFC4122 Guid 业务域，新 APP WFC 使用原 commandId 值，绝不映射/派生另一个身份。`APP_ACCEPTED_GUID_2` 与 SFS `WFC_STRING_1` 分型明确；accepted 五字段 canonical/request digest/result wire 不变。WFC 原 bytes/digest 仅作为原 AppCommand 行的附加不可变字段，随同一 TxD 的任务票决、Recorder、终态 outbox 和原结果提交。已有 completed 行可继续通过原 accepted 入口回放；原 legacy 命令也可通过相同五字段的适配查询，不回填别的成功槽。clientCorrelationId 不改变 WFC 正文摘要；真正不同的 WFC 正文拒绝冲突。

新增按 task 限定的 APP 原命令查询/关闭，返回 COMPLETED/CLOSED/NOT_OBSERVED，继续使用原实际操作者和当前业务读取权；撤销旧 approve 或委派不抹掉已完成原事实。未知命令查询不是失败证明，关闭与决定共享既有原命令互斥协议。

收件箱按实际详情类型打开 APP 或 SFS 页面。APP 页面展示固定业务引用和主体，准确发送本次读取的两版本；意见按 Unicode code point 计数，最多500，拒绝及特定业务 Owner 必填。冲突保留意见，明确重新读取后才允许新意图。未知结果保留同一个命令与原始请求；刷新后仅用会话中的恢复身份查询，不持久保存意见/业务内容。身份/代理人切换立即清私有内容并忽略迟到响应。APP 支持 `?appTask=` 恢复入口；结果沿原 task/instance 状态展示，不把审批终态称为业务应用成功。

## 验证与集中审查

实际隔离PostgreSQL18及HTTP通过统一SFS/APP服务端分型、真实版本/固定引用/主体、封闭输入、APP五字段与原命令身份、WFC alias不可变、同事务回滚和未知提交/撤权后恢复；7项既有APP行为复用本窗通过结果。3条真实Chrome链通过（APP办理恢复、SFS三版本编辑、目录发布与代理运行），新APP链500个补充Unicode字符无损到数据库；24项相关Vue行为通过，显式应用vue-tsc通过。未跑全仓。

证据索引 `D:/CP6-local-test/wave290/foundation/final-validation.json` 保存本窗原日志/TRX、通过来源及失败记录。第一次 HTTP 红灯确认旧主线无统一入口；新 HTTP 负例发现 ScopeAuthorizationException 继承 InvalidOperationException，形状转换误将428变400，已排除领域异常并定向复测所有封闭输入/版本/意见/CSRF情况。初始测试字典构造编译失败也保留。未把失败批次整体标通过，其已成功行为按相同输入直接复用。

APP 浏览器以实际登录人从列表进入任务详情，再打开服务器选择的 APP 页面；500个补充字符完整提交，501个不能发送。实际 HTTP 已提交后只丢响应，刷新再查询得到完全相同 accepted vote/decision，数据库确认单一 AppCommand 与单一终态 outbox。SFS 新入口的精确金额/并行三版本/文件链和原代理流程页面重新通过；真实 PostgreSQL18、内部 Owner、认证/CSRF 与 Chrome，业务数据/用户/政策为明确模拟。迁移仅执行于隔离测试库，既有环境未迁移。

一次集中审查覆盖生产代码、迁移、前端与行为证据。原 AppCommand 的整行不可变触发器继续保护 alias；错误状态、原身份/摘要、当前读取权、冲突/未知恢复、跨租户和迟到响应均检查。没有增加资格包装或独立审计门禁，没有工作流/依赖配置变化。局部成功证据不因 commit/SHA 改变重跑。

## 剩余与生命周期

原375仍28DONE/2IN_PROGRESS/345PLANNED；原59局部4闭环/55余项，125完整目标与247正式验收状态不变。WP12-08仍需原卡逐项核证，不由本项自动关整卡；独立BUG730待修复并实际复现回归。

已登记独立活动 BUG [#730](https://github.com/GTX537/CP6/issues/730)：SFS 原办理意见 HTML maxlength 按 UTF-16 提前截断合法补充字符。W290 APP 页面没有此限制；SFS 问题留独立修复分支，需真实浏览器复现与边界回归后再关闭 Issue。WP12-08 下一窗口按原卡并发/新轮次/稳定 TaskId/CC/直接前置逐项对照既有证据，缺口实际补齐后才可关整卡。

Main/Handoff 远端完整树采用、准确任务分支清理、固定工作区干净及官方 finish/席位释放，以 `C:/Users/tt/CP6-planning-reports/20261017-foundation/wave290-final-verification.json` 核销。无 Actions、生产部署或额外席位；根目录用户归档和固定缓存保留。本窗完成不等于24小时总目标完成。
