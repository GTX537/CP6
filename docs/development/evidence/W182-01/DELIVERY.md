# W182-01 — 业务审批事实接收

COORD 本聊天单人；Main 基线 `94beafc2bccc9c2b5d0d3c3386609d8b7c02ad4d`，复用 DEV1/DEV2。W181 已完整采用、准确删除任务分支和释放席位，仅归档一次。本轮只完成实际 Tool Owner 对原生 Workflow 决定的接收与原键查询，不执行报废。

## 实际采用

接收端使用独立 C02 服务登记、当前有效 token、准确 Scope 和原 Submission 的显式 Owner 读写权；不沿用送审服务的旧权限，也不把消息中声称的身份当成当前主体。原始入站 bytes、规范包、实际 Workflow 决定/证据原件、原事件映射、唯一应用流、Inbox 及审计在同一 PostgreSQL 事务保存。

相同事件同包返回原记录；相同事件异包保存冲突且不覆盖原件。不同事件、相同决定及相同核心内容只登记新入站消息，共享唯一事实/应用流。GAP 持久记录但没有决定事实或应用流。确认丢失后以原事件查询；原接收写权或旧人类提交权撤销不丢失原结果，当前对象读权仍强制检查。

数据库不可变约束、原生来源核对及延迟完整性约束防止遗漏 Inbox/流；真实授权源变更推进 Scope epoch，撤销再恢复仍使当前事务回滚。DEMO 原件不能提升为 REAL。

实际 HTTP 为 `POST /internal/wms/approval-results/v2`、同路径下事件查询、独立应用流结果查询。当前返回 NOT_APPLIED；未知流返回 404。完整配置、权限来源和行为边界见[接收说明](../../tool-approval-facts-v2.md)。迁移不种入服务身份/权限，功能默认关闭。

## 验证与审查

17 项有效本地验证，其中 17 项 PostgreSQL18/HTTP、0 项单元，0 skip。来源和源码绑定为 `D:/CP6-local-test/wave182/foundation/final-validation.json`。覆盖实际批准与驳回原件、原包及证据保存、两级重复、异包冲突、两个真实连接并发一次接收、缺口、跨租户及准确对象授权、原生证据伪造、DEMO 隔离、触发器失效、提交确认丢失和撤权后的原键查询。

故意在 Inbox 前抛错、遗漏 Inbox、事务中撤销再恢复权限，均核实际数据库完整回滚。真实 Kestrel/C02 JWT/CSRF 路径验证服务身份、未登记/已撤销 token、Cookie 冒充失败及接收/事件/应用结果查询。同步回归受影响的 W181 决定与既有 Intake HTTP；不运行无关前端和全仓测试。

一次作者集中 SPEC/QUALITY 审查完整任务 diff，核原合同、当前权限、历史原件、原查询与新执行的区别、数据库原子性和明确保留范围。测试句柄运行中没有修改后端输入；原始执行记录保留，不把复用结果改称新执行。

## 保留范围

收到审批事实不等于业务适用或完成。实际工单/借用/库存等关闭来源缺失时，不生成报废事实或 APPLIED。决定可靠发送、GAP 恢复、实际业务适用性与 ApplicationReceipt 共同事务、回执链、原生办理页面/当前业务投影、服务/撤回实际来源生产者继续接入。原 ROU/BOM/WO/SCHED 及 D2/D3/D5/D6/D7 全部出口保持。真实服务登记、企业政策及运行资格不由隔离测试夹具代替；完整目标和正式运行验收不提升。

## 生命周期

只归档 W181，关闭其限定 `WP12-06.wave181_approval_decision`。本轮新增限定 `WP10-48.wave182_owner_decisions`，完整任务未完成；原七天持续目标 ACTIVE。

原估 120–210 分钟，当前 Main 登记 29.83 分钟；最终 START→finish 计一次，重叠 Handoff 不累加。Handoff https://github.com/GTX537/CP6.AI-Handoff/pull/158，source `f17bd8872bc7d0a7aaa44e63582323a3300f2c6e` → target `0cc3a6b29785802f635bb9b4fdbc6afe9525716d`，准确树采用、本地/远端任务分支删除和 DEV2 释放已核对。Main 此提交待正常 PR/采用/finish，最终核销以 `wave182-final-verification.json` 为准，不提前宣称完成。

用户原件、固定目录和复用缓存保留；无新增源码工作树、Actions 触发/取消、既有环境迁移、生产部署或保护绕过。
