# W168-01：维护实际结果认定与一次正式完成

COORD 单人继续原 CP6-7D-20261022。当前已完成代码、相关本地验证和一次集中 SPEC/QUALITY；Handoff 已合并释放，Main 在此提交时待正常 PR 合并和官方 finish。最终合并/分支/席位以官方 state 与 wave168-final-verification.json 为准。

## 实际业务效果

现有原维护报告（Main.Wms.ToolMaintenanceReport.v1）→稳定 MaintenanceOccurrence→显式 MAINTENANCE_RESULT_ACTUAL 观察→独立维护事实认定政策/决定→同一 PostgreSQL 事务的原 Owner COMPLETE 和应用回执。没有恢复政策时仍可正式完成已认定维护，PASS 保持 COMPLETED_PASS_PENDING_RESET、原生 MAINTENANCE，周期和终身次数不变。后续必须分别具备恢复政策、MASTER 当前资格、准确完成/周期引用和 maintenance-restore 才能归零并建立新周期。实际验证包含250次使用→无恢复政策正式完成→独立评估→一次恢复；不把补录追授为正常使用许可。

REPAIR_REQUIRED、UNSERVICEABLE 关闭原维护结果但保持不可直接使用；后继维护必须显式引用原维护。观察形成的 UNSERVICEABLE 没有正常完成的恢复政策引用，审查因此补上“新修理政策须晚于该完成事实”的检查，防止旧允许修理政策自动放行。

稳定发生键包含可信宿主环境、租户、Site、模式、维护 ID、原报告 Owner/UUID/RESULT/单结果序号。现有原生报告每份只有一个不可变结果；使用其真实 UUID，不声称不同报告是同一报告的修订。相同报告换操作键返回同一发生/观察；不同报告不能产生第二 COMPLETE。已有正常 COMPLETE 仅在原报告、结果与实际时刻完全相符时关联，绝不再写完成或恢复。

共用 W167 可信运行绑定和原操作日志，新增维护种类不改写旧原件字节/Owner。四种 actual-record/policy/recognize/apply 权限继续独立，不新增默认角色授权。认定人员与原报告及观察登记人不同；检查项、原件、最新认定/政策、原发布人与认定人当前权限/组织/Site均核对。缺认定政策或检查项时只能 NEEDS_EVIDENCE。UNKNOWN 历史周期/终身值不造数，事实完成与恢复门禁分别处理。

前向迁移20261028040000增加维护实际种类、唯一键和第8个原件保护触发器；既有不可变保护保持。正式完成、OPEN占用关闭、应用事实与原操作回执同事务；晚期原生修改整体回滚，真实另一连接写入等待提交。查询和重放仍受原Actor/当前读取权限/对象范围限制；写权限撤销不抹除已提交回执，CLOSED不可再次提交。

## API、页面与兼容

新增 actual-maintenance-context、maintenance-occurrence-options、maintenance-occurrences/register、maintenance-recognition-policies 和单发生原件读取；actual-evidence-options?kind=MAINTENANCE 返回原生报告。既有 observations/recognition-decisions/application-context/apply 根据原观察种类处理维护分支；观察输入使用严格维护联合类型，拒绝混入 shots 等使用字段。旧使用请求保持兼容，原操作仍在 tool-actual-usage/v1/operations 查询或关闭。

工具独立入口新增维护实际结果区：选择原报告并显示原检查内容、显式保存发生与观察、发布独立认定政策、读取原应用条件、独立认定和显式正式完成。原维护区继续负责恢复。412保留输入/原引用，不隐式重试；跨页互斥、身份/对象切换和撤权清理，浏览器仅缓存 operation/object/kind。使用与维护共用同一原操作互斥通道，维护回执必须匹配维护种类与Owner。

## 验证与审查

75 项唯一后端本地检查（75 PostgreSQL18/真实HTTP）、29项页面测试、TypeScript和Vite通过，无跳过。完整原始运行、每项来源与最终源码绑定见 D:/CP6-local-test/wave168/foundation/final-validation.json。

保留首轮9项中4通过/5失败记录：测试在停用保护触发器后未恢复，污染同一测试库后续用例。仅修正测试清理和探针对象范围，在新隔离PG复测10项全部通过。集中审查补强不可用结果的新政策边界后，仅重测受影响两项；其余成功运行按真实源码范围复用。兼容性回归另有一次30项通过后因90秒无进展终止，挂起于未改动的注册种子检查；同一时段Windows PostgreSQL记录子进程内存预留错误487。停止精确的本轮隔离实例，保留30项已终态通过来源，在新隔离实例完成原种子检查及未完成部分。未把失败或中止运行称为通过，未触发Actions。

一次完整SPEC/QUALITY关注来源身份、原报告/正式完成Owner、认定与恢复权限分离、唯一效果/事务回滚/并发、历史未知、原操作恢复、API/UI和台账。无未解决必需问题。受影响旧注册、使用和维护验证有明确来源，不因提交或合并重复运行。

## 交付与剩余范围

预计70–110分钟；此提交前Main登记已用 28.53 分钟，最终 START→finish 以本地官方证明记录，Handoff并行占用不重复相加。

Handoff https://github.com/GTX537/CP6.AI-Handoff/pull/144；源 57bd9dddfde194a7fa1fd5b296704bc9e4bed3a6；目标 21432eaeee48ab1344751bd5db7bd4d9f8c99196。本轮Main任务分支codex/w168-01；固定DEV-1；Handoff分支codex/w168-handoff，固定DEV-2已释放；不新增物理源码树，缓存及用户未跟踪文件保留。Main合并后精确删除本任务本地/远端分支并finish释放。

W167归档一次，仅关闭WP10-48.wave167_actual_usage；本轮仅新增WP10-48.wave168_maintenance_observation。375卡正式状态、125完整目标、247运行验收及41历史静态验收保持。D1/D4完成，D2/D3/D5/D6/D7继续；不将2/7阶段数当成实际工作量百分比。

剩余：正常使用ENG技术批准/用途资格、外部作业Owner原件、正式纠正链、未知历史证据、报废审批与义务关闭，以及原D2/D3/D5/D6/D7全部出口。WP05-05订单CHANGE/CANCEL仍REQUESTED，WP04-32正式销售继续开放。本地隔离SIMULATION验证不代表企业验收、浏览器E2E、既有环境迁移或生产部署。
