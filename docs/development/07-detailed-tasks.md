# 细分任务进度

台账日期：2026-10-21。由 roadmap.json 生成；S01 已交付：12/12。S02-00 多租户设计前置保留。

全部已登记任务 375 项，已交付 24 项。任务大小和类型不同，这不是总工时百分比。设计任务完成不代表业务代码或运行验收通过。

任务卡和目标主体责任见[工作包任务索引](10-work-package-tasks.md)，分配步骤见[多对话协作](09-parallel-development.md)。所有状态/领取/证据只在JSON登记；未领取的候选不自动READY。

## WP01 现有实现与设计对照

| 任务 | 仓库 | 状态 | 直接前置 | 类型/规模 | 对话 |
|---|---|---|---|---|---|
| [WP01-01 替代验收采纳后的WP01差异与清单收口](../../docs/development/task-plans/WP01.md#wp01-01) | Main | 待安排 | WP05-01 | DESIGN / M | A / WP01-01 第九轮 验收工具按阶段加载信任配置 |
| [WP01-02 首批多对话交付汇总与下一轮分配依据](../../docs/development/task-plans/WP01.md#wp01-02) | Main | 已交付 | S01-01, S02-00, WP05-01, WP14-01 | DESIGN / M | COORD / WP01-02 |

## WP02 公共平台与系统基础

| 任务 | 仓库 | 状态 | 直接前置 | 类型/规模 | 对话 |
|---|---|---|---|---|---|
| [S01-01 冻结采用输入和共用向量](../../docs/superpowers/plans/2026-10-11-s01-task-breakdown.md) | Main | 已交付 | 无任务前置，仍需准入核对 | DESIGN / S | A / S01-01（兼协调） |
| [S01-02 严格 JSON、NullOnly 与结构相等](../../docs/superpowers/plans/2026-10-11-s01-task-breakdown.md) | Main | 已交付 | S01-01 | CODE / M | A / S01-02（沿用原S01-01对话） |
| [S01-03 FIN-CREDIT 精确金额](../../docs/superpowers/plans/2026-10-11-s01-task-breakdown.md) | Main | 已交付 | S01-02 | CODE / M | A / S01-03（沿用原S01-02对话） |
| [S01-04 FIN-INV 数量、金额与分数](../../docs/superpowers/plans/2026-10-11-s01-task-breakdown.md) | Main | 已交付 | S01-02 | CODE / M | B / S01-04（沿用原S01-05对话） |
| [S01-05 Main TypeScript 精确值](../../docs/superpowers/plans/2026-10-11-s01-task-breakdown.md) | Main | 已交付 | S01-01 | CODE / M | B / S01-05（沿用原S02-00对话） |
| [S01-06 ATP StableId](../../docs/superpowers/plans/2026-10-11-s01-task-breakdown.md) | Main | 已交付 | S01-02 | CODE / M | C / S01-06（沿用原S01-09对话） |
| [S01-07 原生 Ref/Scope 与 NCR/IPQC 类型](../../docs/superpowers/plans/2026-10-11-s01-task-breakdown.md) | Main | 已交付 | S01-02 | CODE / M | D / S01-07（沿用原S01-08对话） |
| [S01-08 CRM INQ/LEAD 合同采用](../../docs/superpowers/plans/2026-10-11-s01-task-breakdown.md) | CRM | 已交付 | S01-02 | CODE / L | D / S01-08（沿用原WP14-01对话） |
| [S01-09 Platform 准确合同路由与原字节](../../docs/superpowers/plans/2026-10-11-s01-task-breakdown.md) | Platform | 已交付 | S01-01 | CODE / M | C / S01-09（沿用原WP05-01对话） |
| [S01-10 PostgreSQL 精确存储适配](../../docs/superpowers/plans/2026-10-11-s01-task-breakdown.md) | Main | 已交付 | S01-03, S01-04, S01-06 | CODE / L | B / S01-10（沿用原S01-04对话） |
| [S01-11 ERP-CREDIT 原生金额与冻结重放](../../docs/superpowers/plans/2026-10-11-s01-task-breakdown.md) | Main | 已交付 | S01-02 | CODE / M | A / S01-11（沿用原S01-03对话） |
| [S01-12 采用证据与消费者交接](../../docs/superpowers/plans/2026-10-11-s01-task-breakdown.md) | Main | 已交付 | S01-02, S01-03, S01-04, S01-05, S01-06, S01-07, S01-08, S01-09, S01-10, S01-11 | DESIGN / M | A / S01-12（沿用原S01-11对话） |
| [S02-00 PostgreSQL 多租户存储与事务布局设计](../../docs/superpowers/plans/2026-10-11-s01-task-breakdown.md) | Main | 已交付 | 无任务前置，仍需准入核对 | DESIGN / M | B / S02-00 |
| [WP02-01 Platform可信请求、消息与后台租户上下文](../../docs/development/task-plans/WP02.md#wp02-01) | Platform | 已交付 | S01-02 | CODE / M | C / WP02-01（沿用原S01-06对话） |
| [WP02-02 Main PostgreSQL租户存储布局与隔离采用](../../docs/development/task-plans/WP02.md#wp02-02) | Main | 开发中 | S02-00, WP02-01 | CODE / M | COORD / W20-02 目录显式安装检查 |
| [WP02-03 Main当前授权与撤权最终门](../../docs/development/task-plans/WP02.md#wp02-03) | Main | 待安排 | WP02-02 | CODE / M | COORD / W72-01 |
| [WP02-04 租户创建、暂停恢复与管理审计](../../docs/development/task-plans/WP02.md#wp02-04) | Main | 待安排 | WP02-03 | CODE / M | COORD / W25-01 租户管理当前平台actor共同事务 |
| [WP02-05 Main身份权威快照及完整目录发布](../../docs/development/task-plans/WP02.md#wp02-05) | Main | 待安排 | S01-09, WP02-03, WP02-04 | CODE / M | COORD / W22-01 Main身份写者覆盖 |
| [WP02-06 CRM身份投影、撤权fence与会话恢复](../../docs/development/task-plans/WP02.md#wp02-06) | CRM | 待安排 | WP02-05, WP02-07 | CODE / M | COORD / W58-01 |
| [WP02-07 Platform调用方同事务Inbox与原结果槽](../../docs/development/task-plans/WP02.md#wp02-07) | Platform | 已交付 | S01-09, WP02-01 | CODE / M | C / WP02-07（沿用原WP02-01对话） |
| [WP02-08 Platform Outbox领取、发送与Unknown恢复](../../docs/development/task-plans/WP02.md#wp02-08) | Platform | 已交付 | WP02-07 | CODE / M | C / WP02-08（沿用原WP02-07对话） |
| [WP02-09 CRM首个Owner真实共同事务与授权门](../../docs/development/task-plans/WP02.md#wp02-09) | CRM | 待安排 | S01-08, WP02-06, WP02-07 | CODE / M | COORD / W59-01 |
| [WP02-10 Main Owner共同事务、原结果与授权参与](../../docs/development/task-plans/WP02.md#wp02-10) | Main | 待安排 | WP02-02, WP02-03, WP02-07 | CODE / M | COORD / W50-01 |
| [WP02-11 Platform HTTP错误、安全重试和截止时间](../../docs/development/task-plans/WP02.md#wp02-11) | Platform | 已交付 | WP02-01 | CODE / M | D / WP02-11（沿用原S01-07对话） |
| [WP02-12 消息producer资格、Kafka/Dapr传输与死信原体](../../docs/development/task-plans/WP02.md#wp02-12) | Platform | 已交付 | S01-09, WP02-01, WP02-08 | CODE / M | C / WP02-12 |
| [WP02-13 审计写入、授权查询和保留清理](../../docs/development/task-plans/WP02.md#wp02-13) | Main | 待安排 | WP02-10 | CODE / M | COORD / W64-01 |
| [WP02-14 文件不可变版本、扫描和Owner授权绑定](../../docs/development/task-plans/WP02.md#wp02-14) | Main | 待安排 | WP02-10 | CODE / M | COORD / W66-01 |
| [WP02-15 隐私subject目录、导出与擦除编排](../../docs/development/task-plans/WP02.md#wp02-15) | Main | 待安排 | WP02-04, WP02-13, WP02-14 | CODE / M | 未领取 |
| [WP02-16 编号原命令分配和生成器安全预览](../../docs/development/task-plans/WP02.md#wp02-16) | Main | 待安排 | WP02-10 | CODE / M | COORD / W65-01 |
| [WP02-17 多语言维护、发布CAS与客户端精确缓存](../../docs/development/task-plans/WP02.md#wp02-17) | Main | 待安排 | WP02-10, WP02-14 | CODE / M | 未领取 |
| [WP02-18 工作台菜单CAS与授权KPI查询](../../docs/development/task-plans/WP02.md#wp02-18) | Main | 待安排 | WP02-03 | CODE / M | D / WP02-18 第十三轮 菜单保存后的列表与当前浏览器导航刷新恢复 |
| [WP02-19 客户端激活、nonce与身份完成门](../../docs/development/task-plans/WP02.md#wp02-19) | Main | 待安排 | WP02-03, WP02-04 | CODE / M | 未领取 |
| [WP02-20 客户端升级渠道、签名与失败恢复](../../docs/development/task-plans/WP02.md#wp02-20) | Main | 待安排 | WP02-19, WP02-28 | CODE / M | 未领取 |
| [WP02-21 后台作业版本、领取终态与恢复](../../docs/development/task-plans/WP02.md#wp02-21) | Main | 待安排 | WP02-08, WP02-10 | CODE / M | 未领取 |
| [WP02-22 商业交接原生wire、来源保护与原结果原语](../../docs/development/task-plans/WP02.md#wp02-22) | Main | 待安排 | S01-09, WP02-10 | CODE / M | 未领取 |
| [WP02-23 INT-OPS Main原结果目录与受控重放](../../docs/development/task-plans/WP02.md#wp02-23) | Main | 待安排 | WP02-10, WP02-22 | CODE / M | 未领取 |
| [WP02-24 INT-OPS CRM身份与商业原结果消费者接入](../../docs/development/task-plans/WP02.md#wp02-24) | CRM | 待安排 | WP02-06, WP02-23, WP03-07, WP03-24, WP03-25 | CODE / M | 未领取 |
| [WP02-25 运行身份、健康与遥测脱敏](../../docs/development/task-plans/WP02.md#wp02-25) | Platform | 已交付 | WP02-01 | CODE / M | D / WP02-25（沿用原WP02-11对话） |
| [WP02-26 SLO封存、故障注入与精确断言辅助](../../docs/development/task-plans/WP02.md#wp02-26) | Platform | 已交付 | S01-03, S01-04, WP02-25 | CODE / M | D / WP02-26 |
| [WP02-27 Platform包能力矩阵与不可变信任验证](../../docs/development/task-plans/WP02.md#wp02-27) | Platform | 已交付 | S01-09, WP02-01 | CODE / M | B / WP02-27（沿用原S01-10对话） |
| [WP02-28 Main宿主实际加载身份与兼容回退验证](../../docs/development/task-plans/WP02.md#wp02-28) | Main | 开发中 | WP02-25, WP02-27 | CODE / M | C / WP02-28 第十三轮 实际publish制品绑定的本地启动与身份readiness验证器 |
| [WP02-29 CRM宿主平台合同与观测包采用](../../docs/development/task-plans/WP02.md#wp02-29) | CRM | 待安排 | WP02-09, WP02-11, WP02-12, WP02-25, WP02-27 | CODE / M | COORD / W62-01 |

## WP03 CRM客户经营

| 任务 | 仓库 | 状态 | 直接前置 | 类型/规模 | 对话 |
|---|---|---|---|---|---|
| [WP03-01 公开表单Bundle与配置生效](../../docs/development/task-plans/WP03.md#wp03-01) | CRM | 待安排 | S01-08, S02-00, WP02-09 | CODE / M | COORD / W74-01 |
| [WP03-02 浏览器绑定公开提交与H01自动受理](../../docs/development/task-plans/WP03.md#wp03-02) | CRM | 待安排 | WP02-09, WP02-16, WP03-01 | CODE / L | 未领取 |
| [WP03-03 询盘审核队列、放行拒绝与到期](../../docs/development/task-plans/WP03.md#wp03-03) | CRM | 待安排 | WP02-09, WP03-02 | CODE / M | 未领取 |
| [WP03-04 询盘同意、保留匿名化与隐私导出](../../docs/development/task-plans/WP03.md#wp03-04) | CRM | 待安排 | WP02-09, WP02-14, WP02-15, WP03-03 | CODE / M | 未领取 |
| [WP03-05 账户创建、维护和独立生命周期](../../docs/development/task-plans/WP03.md#wp03-05) | CRM | 待安排 | S01-08, S02-00, WP02-09 | CODE / M | 未领取 |
| [WP03-06 账户来源证明和旧C03结果历史](../../docs/development/task-plans/WP03.md#wp03-06) | CRM | 待安排 | WP02-09, WP02-23, WP03-05, WP04-03 | CODE / M | 未领取 |
| [WP03-07 账户Main当前映射裁决与资格竞争](../../docs/development/task-plans/WP03.md#wp03-07) | CRM | 待安排 | WP02-09, WP03-06, WP04-03 | CODE / M | 未领取 |
| [WP03-08 独立联系人身份、归属与H02参与](../../docs/development/task-plans/WP03.md#wp03-08) | CRM | 待安排 | WP02-09, WP03-05 | CODE / M | 未领取 |
| [WP03-09 联系人关系完整集合预览与主要期间](../../docs/development/task-plans/WP03.md#wp03-09) | CRM | 待安排 | WP02-09, WP03-08 | CODE / M | 未领取 |
| [WP03-10 联系人渠道值与用途证据策略](../../docs/development/task-plans/WP03.md#wp03-10) | CRM | 待安排 | WP02-09, WP02-15, WP03-08 | CODE / M | 未领取 |
| [WP03-11 联系人互动历史与受控导出](../../docs/development/task-plans/WP03.md#wp03-11) | CRM | 待安排 | WP02-09, WP02-14, WP02-15, WP03-09, WP03-10, WP03-17 | CODE / M | 未领取 |
| [WP03-12 线索手工进入、列表和归属协作](../../docs/development/task-plans/WP03.md#wp03-12) | CRM | 待安排 | S01-08, S02-00, WP02-09 | CODE / M | 未领取 |
| [WP03-13 线索原生活动、首响及SLA双时钟](../../docs/development/task-plans/WP03.md#wp03-13) | CRM | 待安排 | WP02-09, WP03-01, WP03-12 | CODE / M | 未领取 |
| [WP03-14 线索查重观察与资格决定](../../docs/development/task-plans/WP03.md#wp03-14) | CRM | 待安排 | WP02-09, WP03-12, WP03-13 | CODE / M | 未领取 |
| [WP03-15 Lead到Account Contact及可选商机原子转换](../../docs/development/task-plans/WP03.md#wp03-15) | CRM | 待安排 | WP02-09, WP03-05, WP03-09, WP03-14, WP03-21 | CODE / L | 未领取 |
| [WP03-16 线索通知、保留清除与转换后读取](../../docs/development/task-plans/WP03.md#wp03-16) | CRM | 待安排 | WP02-09, WP02-15, WP03-04, WP03-15 | CODE / M | 未领取 |
| [WP03-17 账户联系人原生活动登记与时间线](../../docs/development/task-plans/WP03.md#wp03-17) | CRM | 待安排 | WP02-09, WP03-05, WP03-10 | CODE / M | 未领取 |
| [WP03-18 活动内容、事实更正与Void](../../docs/development/task-plans/WP03.md#wp03-18) | CRM | 待安排 | WP02-09, WP03-17 | CODE / M | 未领取 |
| [WP03-19 活动引用集合与附件字节访问](../../docs/development/task-plans/WP03.md#wp03-19) | CRM | 待安排 | WP02-09, WP02-14, WP03-17 | CODE / M | 未领取 |
| [WP03-20 活动工程来源快照与原键收件](../../docs/development/task-plans/WP03.md#wp03-20) | CRM | 待安排 | WP02-09, WP03-19, WP03-26, WP05-61 | CODE / M | 未领取 |
| [WP03-21 商机创建、需求预测与关联人](../../docs/development/task-plans/WP03.md#wp03-21) | CRM | 待安排 | WP02-09, WP03-05, WP03-09 | CODE / M | 未领取 |
| [WP03-22 商机资格、阶段与销售决定](../../docs/development/task-plans/WP03.md#wp03-22) | CRM | 待安排 | WP02-09, WP03-21 | CODE / M | 未领取 |
| [WP03-23 商机商业交接与补件同事务](../../docs/development/task-plans/WP03.md#wp03-23) | CRM | 待安排 | WP02-09, WP02-22, WP03-07, WP03-22, WP04-13 | CODE / L | 未领取 |
| [WP03-24 商机报价及商业责任完整投影](../../docs/development/task-plans/WP03.md#wp03-24) | CRM | 待安排 | WP02-09, WP03-23, WP04-13, WP04-16, WP04-18 | CODE / M | 未领取 |
| [WP03-25 商业交接投递重试与安全原键查询](../../docs/development/task-plans/WP03.md#wp03-25) | CRM | 待安排 | WP02-09, WP02-11, WP02-21, WP02-23, WP03-23, WP04-13 | CODE / M | 未领取 |
| [WP03-26 工程Case自然槽、准备与新提交](../../docs/development/task-plans/WP03.md#wp03-26) | CRM | 待安排 | WP02-09, WP02-14, WP03-21, WP05-60 | CODE / M | 未领取 |
| [WP03-27 工程受理、进度与精确纠错恢复](../../docs/development/task-plans/WP03.md#wp03-27) | CRM | 待安排 | WP02-09, WP03-26, WP05-61 | CODE / M | 未领取 |

## WP04 商业交易与订单

| 任务 | 仓库 | 状态 | 直接前置 | 类型/规模 | 对话 |
|---|---|---|---|---|---|
| [WP04-01 交易伙伴不可变资料和注册控制](../../docs/development/task-plans/WP04.md#wp04-01) | Main | 待安排 | S02-00, WP02-10, WP02-13, WP11-65 | CODE / M | 未领取 |
| [WP04-02 伙伴用途参与门与旧写者收敛](../../docs/development/task-plans/WP04.md#wp04-02) | Main | 待安排 | WP02-10, WP02-13, WP04-01, WP11-65 | CODE / M | 未领取 |
| [WP04-03 Main账户映射、ReverseClaim和纠错目录](../../docs/development/task-plans/WP04.md#wp04-03) | Main | 待安排 | WP02-10, WP02-13, WP03-05, WP04-02 | CODE / L | 未领取 |
| [WP04-04 价格Profile元数据与完整文件预检](../../docs/development/task-plans/WP04.md#wp04-04) | Main | 待安排 | WP02-10, WP02-13, WP02-14, WP04-01, WP04-33, WP10-47 | CODE / M | 未领取 |
| [WP04-05 价格批次原子提交、版本查询与恢复](../../docs/development/task-plans/WP04.md#wp04-05) | Main | 待安排 | WP02-10, WP02-13, WP04-02, WP04-04 | CODE / M | 未领取 |
| [WP04-06 PRICE到EST完整coverage与最终采用](../../docs/development/task-plans/WP04.md#wp04-06) | Main | 待安排 | WP02-10, WP02-13, WP04-05, WP04-33, WP10-47, WP11-01 | CODE / M | 未领取 |
| [WP04-07 估算完整草稿、来源关联与复制](../../docs/development/task-plans/WP04.md#wp04-07) | Main | 待安排 | WP02-10, WP02-13, WP04-02, WP04-06, WP04-33, WP05-04, WP05-08, WP05-09, WP05-11, WP11-10, WP11-39 | CODE / M | 未领取 |
| [WP04-08 估算CALC2精确计算与完整来源保护](../../docs/development/task-plans/WP04.md#wp04-08) | Main | 待安排 | WP02-10, WP02-13, WP04-07 | CODE / L | 未领取 |
| [WP04-09 估算封存、控制与QTN实际采用](../../docs/development/task-plans/WP04.md#wp04-09) | Main | 待安排 | WP02-10, WP02-13, WP04-08 | CODE / M | 未领取 |
| [WP04-10 正式报价版本、审查与来源披露](../../docs/development/task-plans/WP04.md#wp04-10) | Main | 待安排 | WP02-10, WP02-13, WP04-02, WP04-09, WP11-04, WP11-10, WP12-07 | CODE / M | 未领取 |
| [WP04-11 报价签发、客户决定与接受更正](../../docs/development/task-plans/WP04.md#wp04-11) | Main | 待安排 | WP02-10, WP02-13, WP04-10 | CODE / M | 未领取 |
| [WP04-12 报价数量坐标分配与同事务Sales草稿](../../docs/development/task-plans/WP04.md#wp04-12) | Main | 待安排 | WP02-10, WP02-13, WP04-11, WP04-14 | CODE / L | 未领取 |
| [WP04-13 Main商业交接受理与原键完整目录](../../docs/development/task-plans/WP04.md#wp04-13) | Main | 待安排 | WP02-10, WP02-13, WP02-22, WP02-23, WP04-03, WP05-60 | CODE / L | 未领取 |
| [WP04-14 销售草稿、稳定行交期与导入](../../docs/development/task-plans/WP04.md#wp04-14) | Main | 待安排 | WP02-10, WP02-13, WP02-14, WP04-02, WP04-32, WP04-33, WP05-11, WP11-04, WP11-10 | CODE / M | 未领取 |
| [WP04-15 销售ATP读取及人工承诺采纳](../../docs/development/task-plans/WP04.md#wp04-15) | Main | 待安排 | S01-06, WP02-10, WP02-13, WP04-14, WP06-06 | CODE / M | 未领取 |
| [WP04-16 销售审批与信用参与的商业确认](../../docs/development/task-plans/WP04.md#wp04-16) | Main | 待安排 | WP02-10, WP02-13, WP04-12, WP04-15, WP04-32, WP05-32, WP11-07, WP12-07, WP12-19 | CODE / L | 未领取 |
| [WP04-17 Sales Release底座、初次合法用途与H10接收贯通](../../docs/development/task-plans/WP04.md#wp04-17) | Main | 待安排 | WP02-10, WP02-13, WP04-16, WP04-32, WP05-32, WP06-13 | CODE / L | 未领取 |
| [WP04-18 订单变化、取消与迟到执行控制](../../docs/development/task-plans/WP04.md#wp04-18) | Main | 待安排 | WP02-10, WP02-13, WP04-17, WP06-03, WP07-05, WP08-15, WP10-20, WP12-07, WP12-19 | CODE / L | 未领取 |
| [WP04-19 订单剩余份额后继组启用协议](../../docs/development/task-plans/WP04.md#wp04-19) | Main | 待安排 | WP02-10, WP04-32 | DESIGN / M | 未领取 |
| [WP04-20 订单后继组准备、启用与原谱系退休](../../docs/development/task-plans/WP04.md#wp04-20) | Main | 待安排 | WP02-10, WP02-13, WP04-19, WP06-02 | CODE / M | 未领取 |
| [WP04-21 履约发运签收退回的准确坐标投影](../../docs/development/task-plans/WP04.md#wp04-21) | Main | 待安排 | WP02-10, WP02-13, WP04-17, WP10-19, WP10-22, WP10-52, WP11-17, WP11-26, WP11-27, WP11-29, WP11-31, WP11-38 | CODE / M | 未领取 |
| [WP04-22 履约权威目录与完整快照](../../docs/development/task-plans/WP04.md#wp04-22) | Main | 待安排 | WP02-10, WP02-13, WP04-18, WP04-21, WP04-32, WP10-05, WP10-20, WP10-52, WP11-35, WP11-62 | CODE / M | 未领取 |
| [WP04-23 履约四项绩效、追踪和安全导出](../../docs/development/task-plans/WP04.md#wp04-23) | Main | 待安排 | WP02-10, WP02-13, WP02-14, WP04-22, WP04-32, WP10-47 | CODE / M | 未领取 |
| [WP04-24 FSC真实模板和格式版本核定](../../docs/development/task-plans/WP04.md#wp04-24) | Main | 待安排 | WP02-14, WP04-09, WP04-10 | DESIGN / M | 未领取 |
| [WP04-25 FSC来源双Guard、原件签发与下载](../../docs/development/task-plans/WP04.md#wp04-25) | Main | 待安排 | WP02-10, WP02-13, WP02-14, WP02-16, WP04-09, WP04-10, WP04-24 | CODE / L | 未领取 |
| [WP04-26 FSC逐项批次恢复与旧编号投影](../../docs/development/task-plans/WP04.md#wp04-26) | Main | 待安排 | WP02-10, WP02-13, WP04-25 | CODE / M | 未领取 |
| [WP04-27 售后CommercialWork受理与商业决定](../../docs/development/task-plans/WP04.md#wp04-27) | Main | 待安排 | S01-11, WP02-10, WP02-13, WP04-02, WP09-12, WP10-22, WP12-07 | CODE / L | 未领取 |
| [WP04-28 售后贷项准备、独立过账与原结果采用](../../docs/development/task-plans/WP04.md#wp04-28) | Main | 待安排 | WP02-10, WP02-13, WP04-27, WP11-32 | CODE / M | 未领取 |
| [WP04-29 售后退款授权与真实现金结果跟踪](../../docs/development/task-plans/WP04.md#wp04-29) | Main | 待安排 | WP02-10, WP02-13, WP04-28, WP11-33, WP11-64 | CODE / M | 未领取 |
| [WP04-30 售后补发和替换的新需求交接](../../docs/development/task-plans/WP04.md#wp04-30) | Main | 待安排 | WP02-10, WP02-13, WP04-14, WP04-27, WP04-36, WP10-22, WP10-52 | CODE / M | 未领取 |
| [WP04-31 售后更正撤权与全成员恢复](../../docs/development/task-plans/WP04.md#wp04-31) | Main | 待安排 | WP02-10, WP02-13, WP04-28, WP04-29, WP04-30, WP09-12, WP10-22 | CODE / L | 未领取 |
| [WP04-32 Sales稳定来源目录、原结果与H10 H32发布参加](../../docs/development/task-plans/WP04.md#wp04-32) | Main | 待安排 | WP02-10, WP02-13, WP02-22, WP04-02 | CODE / M | 未领取 |
| [WP04-33 ERP经营产品物料身份与工程映射读取提供者](../../docs/development/task-plans/WP04.md#wp04-33) | Main | 待安排 | WP02-10, WP02-13 | CODE / M | 未领取 |
| [WP04-34 原发运商业可退资格与独立退货来源授权](../../docs/development/task-plans/WP04.md#wp04-34) | Main | 待安排 | WP02-10, WP02-13, WP04-32, WP10-19, WP12-07 | CODE / M | 未领取 |
| [WP04-35 售后新需求真实Producer与独立创建授权采用设计](../../docs/development/task-plans/WP04.md#wp04-35) | Main | 待安排 | WP04-32, WP04-34, WP12-07 | DESIGN / M | 未领取 |
| [WP04-36 售后新需求合法Producer与原创建结果提供者](../../docs/development/task-plans/WP04.md#wp04-36) | Main | 待安排 | WP02-10, WP02-13, WP02-22, WP04-32, WP04-34, WP04-35, WP12-07 | CODE / L | 未领取 |
| [WP04-37 销售Release正式用途许可采用与完整需求贯通](../../docs/development/task-plans/WP04.md#wp04-37) | Main | 待安排 | WP02-10, WP02-13, WP04-17, WP05-42, WP06-02 | CODE / L | 未领取 |

## WP05 工程与产品生命周期

| 任务 | 仓库 | 状态 | 直接前置 | 类型/规模 | 对话 |
|---|---|---|---|---|---|
| [WP05-01 S00：ITER/REL缺失原验收的替代依据设计](../../docs/development/task-plans/WP05.md#wp05-01) | Main | 已交付 | 无任务前置，仍需准入核对 | DESIGN / M | C / WP05-01 |
| [WP05-02 工程产品身份与经营料号双Owner映射](../../docs/development/task-plans/WP05.md#wp05-02) | Main | 待安排 | S01-02, S01-07, S02-00, WP02-10, WP02-13, WP04-33 | CODE / M | 未领取 |
| [WP05-03 工程修订、工作快照与组成候选](../../docs/development/task-plans/WP05.md#wp05-03) | Main | 待安排 | S01-02, S01-07, S02-00, WP02-10, WP02-13, WP05-02 | CODE / L | 未领取 |
| [WP05-04 旧版模写入口的版本与原事务兼容](../../docs/development/task-plans/WP05.md#wp05-04) | Main | 待安排 | S01-02, S01-07, S02-00, WP02-10, WP02-13 | CODE / L | 未领取 |
| [WP05-05 版模商业联动改为原订单Owner意图](../../docs/development/task-plans/WP05.md#wp05-05) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP04-32, WP05-04 | CODE / M | 未领取 |
| [WP05-06 工具规格与实例双Role当前关联](../../docs/development/task-plans/WP05.md#wp05-06) | Main | 待安排 | S01-02, S01-07, S02-00, WP02-10, WP02-13, WP05-02, WP05-04, WP10-48 | CODE / L | 未领取 |
| [WP05-07 工具文档关联、影响闭合和旧标签输出](../../docs/development/task-plans/WP05.md#wp05-07) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP05-06, WP05-16, WP07-12, WP08-03, WP08-04, WP10-36 | CODE / M | 未领取 |
| [WP05-08 版本化工艺操作与工具规格引用](../../docs/development/task-plans/WP05.md#wp05-08) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP05-03, WP05-04, WP08-16 | CODE / M | 未领取 |
| [WP05-09 版本化BOM、材料替代与有效性](../../docs/development/task-plans/WP05.md#wp05-09) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP05-03, WP05-08, WP10-47 | CODE / M | 未领取 |
| [WP05-10 配置与逐输出输入工序关系](../../docs/development/task-plans/WP05.md#wp05-10) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP05-08, WP05-09 | CODE / L | 未领取 |
| [WP05-11 工程源固定证明与完整性检查](../../docs/development/task-plans/WP05.md#wp05-11) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP05-03, WP05-06, WP05-10, WP05-16, WP09-11 | CODE / L | 未领取 |
| [WP05-12 工程基线冻结、后继与准确发布委托](../../docs/development/task-plans/WP05.md#wp05-12) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP05-11, WP05-20 | CODE / L | 未领取 |
| [WP05-13 工程需求来源、分解与可评价判据](../../docs/development/task-plans/WP05.md#wp05-13) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP02-14, WP03-17, WP03-21, WP04-32, WP05-02 | CODE / M | 未领取 |
| [WP05-14 需求集批准、冲突裁决与后继影响](../../docs/development/task-plans/WP05.md#wp05-14) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP05-13, WP12-06, WP12-07 | CODE / M | 未领取 |
| [WP05-15 需求分配覆盖与满足性签署](../../docs/development/task-plans/WP05.md#wp05-15) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP05-14, WP05-22, WP05-24 | CODE / M | 未领取 |
| [WP05-16 受控技术文档版本与原件派生](../../docs/development/task-plans/WP05.md#wp05-16) | Main | 待安排 | S01-02, S01-07, S02-00, WP02-10, WP02-13, WP02-14, WP02-15, WP12-06, WP12-07 | CODE / M | 未领取 |
| [WP05-17 技术包发放、副本替换与影响义务](../../docs/development/task-plans/WP05.md#wp05-17) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP05-12, WP05-16, WP08-04, WP10-09, WP10-12 | CODE / M | 未领取 |
| [WP05-18 工程项目、迭代输入与任务确认](../../docs/development/task-plans/WP05.md#wp05-18) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP02-16, WP05-01, WP05-03, WP05-14 | CODE / L | 未领取 |
| [WP05-19 迭代方案全集、评审包与OA持久提交](../../docs/development/task-plans/WP05.md#wp05-19) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP05-03, WP05-18, WP12-06 | CODE / L | 未领取 |
| [WP05-20 迭代决定应用、问题关闭与撤回竞争](../../docs/development/task-plans/WP05.md#wp05-20) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP05-19, WP12-07, WP12-19 | CODE / L | 未领取 |
| [WP05-21 迭代评价、结果型关闭与原操作恢复](../../docs/development/task-plans/WP05.md#wp05-21) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP05-12, WP05-20, WP05-24, WP08-15 | CODE / L | 未领取 |
| [WP05-22 验证计划、受测对象与原始证据登记](../../docs/development/task-plans/WP05.md#wp05-22) | Main | 待安排 | 共13项，详见任务卡 | CODE / L | 未领取 |
| [WP05-23 试制申请、真实执行关联和取消恢复](../../docs/development/task-plans/WP05.md#wp05-23) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP05-22, WP05-30, WP05-32, WP05-33, WP06-11, WP08-03, WP08-04, WP08-15 | CODE / L | 未领取 |
| [WP05-24 专业评价、复测和带条件设计确认](../../docs/development/task-plans/WP05.md#wp05-24) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP05-22, WP05-23, WP08-07, WP08-08, WP09-06 | CODE / L | 未领取 |
| [WP05-25 工程变更ECR受理和分域影响扫描](../../docs/development/task-plans/WP05.md#wp05-25) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP05-02, WP05-03, WP06-02, WP07-04, WP08-03, WP10-05 | CODE / M | 未领取 |
| [WP05-26 ECO批准范围、义务和受控修订](../../docs/development/task-plans/WP05.md#wp05-26) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP05-25, WP12-06, WP12-07 | CODE / L | 未领取 |
| [WP05-27 ECN准确发布、准备与采用跟踪](../../docs/development/task-plans/WP05.md#wp05-27) | Main | 待安排 | 共14项，详见任务卡 | CODE / L | 未领取 |
| [WP05-28 变更范围关闭、取消终结与迟到事实](../../docs/development/task-plans/WP05.md#wp05-28) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP05-27, WP05-36 | CODE / M | 未领取 |
| [WP05-29 临时偏离范围、限额政策与决定](../../docs/development/task-plans/WP05.md#wp05-29) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP05-03, WP12-06, WP12-07 | CODE / L | 未领取 |
| [WP05-30 DEV真实Claim与并发最大暴露保护](../../docs/development/task-plans/WP05.md#wp05-30) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP05-29, WP05-33, WP08-17 | CODE / L | 未领取 |
| [WP05-31 DEV实际使用、更正、安全释放与关闭](../../docs/development/task-plans/WP05.md#wp05-31) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP05-30, WP08-06, WP08-07, WP08-14, WP08-15, WP08-18, WP09-09, WP10-24 | CODE / L | 未领取 |
| [WP05-32 REL用途政策与准确使用授权](../../docs/development/task-plans/WP05.md#wp05-32) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP05-01, WP05-12, WP12-06, WP12-07 | CODE / L | 未领取 |
| [WP05-33 REL预约额度与原执行意图准入](../../docs/development/task-plans/WP05.md#wp05-33) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP05-32, WP08-17 | CODE / L | 未领取 |
| [WP05-34 技术发布目标定义与可靠交付](../../docs/development/task-plans/WP05.md#wp05-34) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-12, WP02-13, WP05-32 | CODE / M | 未领取 |
| [WP05-35 REL真实消费、超用更正与安全结清](../../docs/development/task-plans/WP05.md#wp05-35) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP05-33, WP08-07, WP08-14, WP08-15, WP08-18 | CODE / L | 未领取 |
| [WP05-36 逐Target真实采用、切换与独立后果关闭](../../docs/development/task-plans/WP05.md#wp05-36) | Main | 待安排 | 共13项，详见任务卡 | CODE / L | 未领取 |
| [WP05-37 量产反馈归集、真实群体与原因分流](../../docs/development/task-plans/WP05.md#wp05-37) | Main | 待安排 | 共13项，详见任务卡 | CODE / M | 未领取 |
| [WP05-38 反馈改进承接、效果观察与知识关闭](../../docs/development/task-plans/WP05.md#wp05-38) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP05-24, WP05-26, WP05-36, WP05-37, WP08-12, WP09-09, WP09-10 | CODE / M | 未领取 |
| [WP05-39 配置替代规则、方向性与有界解析](../../docs/development/task-plans/WP05.md#wp05-39) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP05-09, WP05-10, WP10-47 | CODE / L | 未领取 |
| [WP05-40 生命周期停用、继任采用与最后活动责任](../../docs/development/task-plans/WP05.md#wp05-40) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP05-25, WP05-34, WP05-39, WP06-03, WP07-05, WP08-15, WP10-05, WP10-36 | CODE / L | 未领取 |
| [WP05-41 旧版支持、生命周期恢复与受控归档](../../docs/development/task-plans/WP05.md#wp05-41) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP02-14, WP02-15, WP05-17, WP05-31, WP05-36, WP05-40 | CODE / M | 未领取 |
| [WP05-42 REL专业后验条件采用与正式用途授权](../../docs/development/task-plans/WP05.md#wp05-42) | Main | 待安排 | S01-07, S02-00, WP02-03, WP02-10, WP02-13, WP05-24, WP05-32, WP09-06 | CODE / M | 未领取 |
| [WP05-60 Main工程自然Case NextContext与M02 M03受理门](../../docs/development/task-plans/WP05.md#wp05-60) | Main | 待安排 | WP02-10, WP02-13, WP02-22, WP05-13 | CODE / M | 未领取 |
| [WP05-61 工程Intake Progress规范流与精确纠错提供者](../../docs/development/task-plans/WP05.md#wp05-61) | Main | 待安排 | WP02-10, WP02-12, WP02-13, WP05-20, WP05-24, WP05-32, WP05-60 | CODE / M | 未领取 |

## WP06 计划计算与决策

| 任务 | 仓库 | 状态 | 直接前置 | 类型/规模 | 对话 |
|---|---|---|---|---|---|
| [WP06-01 版本化计划策略与正式解析](../../docs/development/task-plans/WP06.md#wp06-01) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP05-02, WP05-03, WP10-47 | CODE / M | 未领取 |
| [WP06-02 真实Sales Release采用与不可拆需求组启用](../../docs/development/task-plans/WP06.md#wp06-02) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP04-17, WP04-19, WP06-13 | CODE / L | 未领取 |
| [WP06-03 需求后继、取消与真实发运责任核减](../../docs/development/task-plans/WP06.md#wp06-03) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP04-32, WP06-02, WP10-19 | CODE / L | 未领取 |
| [WP06-04 供给来源账、互斥代表与固定cut提供者](../../docs/development/task-plans/WP06.md#wp06-04) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP06-01 | CODE / L | 未领取 |
| [WP06-05 采购、制造及库存事实采用和供给回流](../../docs/development/task-plans/WP06.md#wp06-05) | Main | 待安排 | 共13项，详见任务卡 | CODE / L | 未领取 |
| [WP06-06 ATP R11真实评估、恢复与销售查询](../../docs/development/task-plans/WP06.md#wp06-06) | Main | 待安排 | S01-02, S01-05, S01-06, S02-00, WP02-10, WP02-13, WP06-01, WP06-05 | CODE / L | 未领取 |
| [WP06-07 MRP责任展开与NET/GROSS无损计算](../../docs/development/task-plans/WP06.md#wp06-07) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP05-10, WP05-12, WP05-39, WP06-01, WP06-02, WP06-04, WP08-05 | CODE / M | 未领取 |
| [WP06-08 MRP输入捕获与正式Seal](../../docs/development/task-plans/WP06.md#wp06-08) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP06-05, WP06-07, WP08-05 | CODE / L | 未领取 |
| [WP06-09 MRP尝试、发布、取消与迟到worker恢复](../../docs/development/task-plans/WP06.md#wp06-09) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP06-08 | CODE / L | 未领取 |
| [WP06-10 CP22计划单来源份额、委托与转换意图](../../docs/development/task-plans/WP06.md#wp06-10) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP06-09 | CODE / L | 未领取 |
| [WP06-11 CP22真实PR、WO与调拨转单并替换Stub](../../docs/development/task-plans/WP06.md#wp06-11) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP06-10, WP07-01, WP08-03, WP10-10 | CODE / L | 未领取 |
| [WP06-12 CP12缺料异常、完整影响图与有证重算协调](../../docs/development/task-plans/WP06.md#wp06-12) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP06-03, WP06-05, WP06-09, WP06-11, WP08-13 | CODE / L | 未领取 |
| [WP06-13 Planning H10原生接收槽与源控制最终门](../../docs/development/task-plans/WP06.md#wp06-13) | Main | 待安排 | WP02-10, WP02-13, WP04-32 | CODE / M | 未领取 |

## WP07 采购执行

| 任务 | 仓库 | 状态 | 直接前置 | 类型/规模 | 对话 |
|---|---|---|---|---|---|
| [WP07-01 PR人工与CP22来源接收Owner](../../docs/development/task-plans/WP07.md#wp07-01) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP04-02 | CODE / L | 未领取 |
| [WP07-02 PR审批应用、来源保护与转PO](../../docs/development/task-plans/WP07.md#wp07-02) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP07-01, WP07-04, WP12-07, WP12-19 | CODE / L | 未领取 |
| [WP07-03 RFQ修订、供应报价与选定依据](../../docs/development/task-plans/WP07.md#wp07-03) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP04-02, WP07-01, WP11-04, WP11-10 | CODE / L | 未领取 |
| [WP07-04 PO来源最终门、创建发行与确认](../../docs/development/task-plans/WP07.md#wp07-04) | Main | 待安排 | S01-02, S01-05, S01-10, S02-00, WP02-10, WP02-13, WP04-02, WP07-01, WP07-03, WP11-04, WP11-10 | CODE / L | 未领取 |
| [WP07-05 PO变更、取消与供应延期反馈](../../docs/development/task-plans/WP07.md#wp07-05) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP07-04, WP07-06 | CODE / L | 未领取 |
| [WP07-06 采购到货源、GR商业根与收货授权提供者](../../docs/development/task-plans/WP07.md#wp07-06) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP07-04, WP09-02 | CODE / L | 未领取 |
| [WP07-07 IQC结果在GR/PO真实应用及采购来料闭环](../../docs/development/task-plans/WP07.md#wp07-07) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP07-06, WP09-04, WP10-14, WP10-46 | CODE / L | 未领取 |
| [WP07-08 供应商发票、原GR匹配与AP释放](../../docs/development/task-plans/WP07.md#wp07-08) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP07-07, WP07-14, WP11-18, WP11-66 | CODE / L | 未领取 |
| [WP07-09 MATCH纠正取消、MRX与全腿关闭提供者](../../docs/development/task-plans/WP07.md#wp07-09) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP07-08, WP07-14, WP11-24 | CODE / L | 未领取 |
| [WP07-10 采购退货Case、冻结范围与执行授权](../../docs/development/task-plans/WP07.md#wp07-10) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP04-02, WP07-07, WP12-07, WP12-19 | CODE / L | 未领取 |
| [WP07-11 供应商实际退运、结算调整与取消恢复](../../docs/development/task-plans/WP07.md#wp07-11) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP07-09, WP07-10, WP10-19, WP10-20, WP11-23, WP11-24, WP11-66 | CODE / L | 未领取 |
| [WP07-12 委外材料计划、发料预算与真实移交](../../docs/development/task-plans/WP07.md#wp07-12) | Main | 待安排 | 共14项，详见任务卡 | CODE / L | 未领取 |
| [WP07-13 委外财务采用、剩余义务与完整关闭](../../docs/development/task-plans/WP07.md#wp07-13) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP07-07, WP07-12, WP07-15, WP10-11, WP11-19, WP11-42 | CODE / L | 未领取 |
| [WP07-14 MATCH完整FinanceInput与Admission控制提供者](../../docs/development/task-plans/WP07.md#wp07-14) | Main | 待安排 | WP02-10, WP02-13, WP07-07 | CODE / M | 未领取 |
| [WP07-15 委外实际消耗退回与成品Receipt来源提供者](../../docs/development/task-plans/WP07.md#wp07-15) | Main | 待安排 | WP02-10, WP02-13, WP07-07, WP07-12, WP10-11 | CODE / L | 未领取 |

## WP08 制造执行

| 任务 | 仓库 | 状态 | 直接前置 | 类型/规模 | 对话 |
|---|---|---|---|---|---|
| [WP08-01 设备工作中心版本、归属与当前资源门](../../docs/development/task-plans/WP08.md#wp08-01) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP05-08, WP08-16 | CODE / L | 未领取 |
| [WP08-02 资源运行停机事实、冲突与时间线](../../docs/development/task-plans/WP08.md#wp08-02) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP08-01 | CODE / L | 未领取 |
| [WP08-03 真实WO创建、工程快照及CP22接收Owner](../../docs/development/task-plans/WP08.md#wp08-03) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP05-10, WP05-12, WP05-30, WP05-32, WP05-33, WP05-39, WP08-17 | CODE / L | 未领取 |
| [WP08-04 WO下达与当前执行准入](../../docs/development/task-plans/WP08.md#wp08-04) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP05-30, WP05-32, WP05-33, WP08-01, WP08-03, WP09-02 | CODE / L | 未领取 |
| [WP08-05 材料义务账与MAT-MRP-RESIDUAL只读提供者](../../docs/development/task-plans/WP08.md#wp08-05) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP05-10, WP05-12, WP05-39, WP08-03, WP10-05 | CODE / L | 未领取 |
| [WP08-06 生产领退料、消耗与反冲真实数量事务](../../docs/development/task-plans/WP08.md#wp08-06) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP08-04, WP08-05, WP10-03, WP10-08 | CODE / L | 未领取 |
| [WP08-07 工序执行Basis、报工与计量预算](../../docs/development/task-plans/WP08.md#wp08-07) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP08-02, WP08-04, WP08-05 | CODE / L | 未领取 |
| [WP08-08 最终产出谱系、入库allowance与质量总体](../../docs/development/task-plans/WP08.md#wp08-08) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP08-07 | CODE / L | 未领取 |
| [WP08-09 产出缩量纠正、全执行关闭与制造完成](../../docs/development/task-plans/WP08.md#wp08-09) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP08-06, WP08-08, WP09-06, WP10-15 | CODE / L | 未领取 |
| [WP08-10 排程候选、完整Root组与资源队列](../../docs/development/task-plans/WP08.md#wp08-10) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP08-01, WP08-03 | CODE / M | 未领取 |
| [WP08-11 排程应用、换机六阶段与恢复](../../docs/development/task-plans/WP08.md#wp08-11) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP08-04, WP08-07, WP08-10 | CODE / L | 未领取 |
| [WP08-12 制造绩效与OEE准确窗计算发布](../../docs/development/task-plans/WP08.md#wp08-12) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP08-02, WP08-07, WP08-08, WP09-06 | CODE / L | 未领取 |
| [WP08-13 生产追加料从责任后继到采购及重新领料闭环](../../docs/development/task-plans/WP08.md#wp08-13) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP06-05, WP06-11, WP07-07, WP08-06, WP10-14, WP10-46 | CODE / L | 未领取 |
| [WP08-14 报工更正、Basis无缝后继与持久副作用恢复](../../docs/development/task-plans/WP08.md#wp08-14) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP08-06, WP08-07, WP08-08, WP09-06, WP11-40 | CODE / L | 未领取 |
| [WP08-15 工单取消、剩余责任与完整关闭](../../docs/development/task-plans/WP08.md#wp08-15) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP08-09, WP08-11, WP08-14 | CODE / L | 未领取 |
| [WP08-16 RES资源身份与独立能力目录提供者](../../docs/development/task-plans/WP08.md#wp08-16) | Main | 待安排 | S01-02, S02-00, WP02-03, WP02-10, WP02-13 | CODE / M | 未领取 |
| [WP08-17 MES原执行意图、原键查询与最终准入参加者](../../docs/development/task-plans/WP08.md#wp08-17) | Main | 待安排 | S01-02, S01-07, S02-00, WP02-03, WP02-10, WP02-13, WP05-12 | CODE / M | 未领取 |
| [WP08-18 MES正式生产与DEV多限制采用闭环](../../docs/development/task-plans/WP08.md#wp08-18) | Main | 待安排 | 共13项，详见任务卡 | CODE / M | 未领取 |

## WP09 质量管理

| 任务 | 仓库 | 状态 | 直接前置 | 类型/规模 | 对话 |
|---|---|---|---|---|---|
| [WP09-01 质量Plan、Policy与豁免Ground生命周期](../../docs/development/task-plans/WP09.md#wp09-01) | Main | 待安排 | S01-02, S01-05, S01-07, S02-00, WP02-10, WP02-13, WP05-02, WP05-03, WP05-12, WP05-16, WP05-32, WP09-11 | CODE / L | 未领取 |
| [WP09-02 QA任务族、源映射与同事务最终质量门](../../docs/development/task-plans/WP09.md#wp09-02) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP05-12, WP05-30, WP05-32, WP09-01 | CODE / L | 未领取 |
| [WP09-03 IQC真实来料任务、采样与完整测量证据](../../docs/development/task-plans/WP09.md#wp09-03) | Main | 待安排 | S01-02, S01-05, S01-07, S02-00, WP02-10, WP02-13, WP02-14, WP07-06, WP09-02, WP10-14 | CODE / L | 未领取 |
| [WP09-04 IQC独立决定、GR/Stock双交接与当前屏障](../../docs/development/task-plans/WP09.md#wp09-04) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP09-03 | CODE / L | 未领取 |
| [WP09-05 过程与最终检验任务、测量与复核](../../docs/development/task-plans/WP09.md#wp09-05) | Main | 待安排 | S01-02, S01-05, S01-07, S02-00, WP02-10, WP02-13, WP08-07, WP08-08, WP09-02 | CODE / L | 未领取 |
| [WP09-06 IPQC/FQC判定、库存质量应用与失效恢复](../../docs/development/task-plans/WP09.md#wp09-06) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP09-05, WP10-03, WP10-15 | CODE / L | 未领取 |
| [WP09-07 NCR三源建单、处置分区与独立批准](../../docs/development/task-plans/WP09.md#wp09-07) | Main | 待安排 | S01-02, S01-05, S01-07, S02-00, WP02-10, WP02-13, WP05-02, WP05-12, WP09-04, WP09-06, WP10-22 | CODE / L | 未领取 |
| [WP09-08 NCR返工准入、实际完成和重检闭环](../../docs/development/task-plans/WP09.md#wp09-08) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP08-14, WP09-07 | CODE / L | 未领取 |
| [WP09-09 NCR处置财务采用、H30与全义务关闭](../../docs/development/task-plans/WP09.md#wp09-09) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP04-31, WP09-07, WP09-12, WP10-04, WP11-44, WP11-63 | CODE / L | 未领取 |
| [WP09-10 质量工程反馈、变更影响和技术偏离双授权](../../docs/development/task-plans/WP09.md#wp09-10) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP05-25, WP05-26, WP05-29, WP05-30, WP05-33, WP05-37, WP09-07 | CODE / L | 未领取 |
| [WP09-11 独立质量规格、Requirements与固定源证明](../../docs/development/task-plans/WP09.md#wp09-11) | Main | 待安排 | S01-07, S02-00, WP02-03, WP02-10, WP02-13, WP02-14, WP05-16, WP12-07 | CODE / M | 未领取 |
| [WP09-12 NCR专业物理应用与当前来源结果提供者](../../docs/development/task-plans/WP09.md#wp09-12) | Main | 待安排 | WP02-10, WP02-13, WP09-07, WP10-04 | CODE / M | 未领取 |

## WP10 仓储与物流

| 任务 | 仓库 | 状态 | 直接前置 | 类型/规模 | 对话 |
|---|---|---|---|---|---|
| [WP10-01 仓库库位身份、typed capacity与初始化隔离](../../docs/development/task-plans/WP10.md#wp10-01) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP10-47 | CODE / L | 未领取 |
| [WP10-02 库存身份、来源根及组合资源注册内核](../../docs/development/task-plans/WP10.md#wp10-02) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP05-02, WP10-01, WP10-47 | CODE / L | 未领取 |
| [WP10-03 库存唯一数量过账、移动与原结果恢复](../../docs/development/task-plans/WP10.md#wp10-03) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP09-02, WP10-02 | CODE / L | 未领取 |
| [WP10-04 库存Claim、限制并集、纠正与安全释放](../../docs/development/task-plans/WP10.md#wp10-04) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP10-03 | CODE / L | 未领取 |
| [WP10-05 库存完整cut、源事实读取与SUP/分析提供者](../../docs/development/task-plans/WP10.md#wp10-05) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP10-04 | CODE / M | 未领取 |
| [WP10-06 库位停用、恢复与退役全Owner闭包](../../docs/development/task-plans/WP10.md#wp10-06) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP10-04, WP10-08, WP10-10, WP10-25 | CODE / L | 未领取 |
| [WP10-07 库存预留请求、Backing与Allocation](../../docs/development/task-plans/WP10.md#wp10-07) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP10-04 | CODE / L | 未领取 |
| [WP10-08 预留Use登记、关闭与不丢保护的释放](../../docs/development/task-plans/WP10.md#wp10-08) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP10-07 | CODE / L | 未领取 |
| [WP10-09 移动作业信封、扫码指令与标签任务基础](../../docs/development/task-plans/WP10.md#wp10-09) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP02-14, WP02-16, WP02-19 | CODE / L | 未领取 |
| [WP10-10 调拨Request/Stage、来源预算与本地移动Owner](../../docs/development/task-plans/WP10.md#wp10-10) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP10-03, WP10-08, WP10-09 | CODE / L | 未领取 |
| [WP10-11 跨仓在途接收、调拨差异与全路径恢复](../../docs/development/task-plans/WP10.md#wp10-11) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP10-10 | CODE / L | 未领取 |
| [WP10-12 移动作业采用IN/OUT/Transfer及领料真实Owner](../../docs/development/task-plans/WP10.md#wp10-12) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP08-06, WP10-09, WP10-10, WP10-14, WP10-15, WP10-19 | CODE / L | 未领取 |
| [WP10-13 IN不可变请求、来源计划与原结果协议](../../docs/development/task-plans/WP10.md#wp10-13) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP10-03 | CODE / L | 未领取 |
| [WP10-14 采购实际收货、GR/PO共同门与质量应用](../../docs/development/task-plans/WP10.md#wp10-14) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP07-06, WP09-02, WP10-13 | CODE / L | 未领取 |
| [WP10-15 生产产出分批入库与OUTPUT预算采用](../../docs/development/task-plans/WP10.md#wp10-15) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP08-08, WP09-02, WP10-13 | CODE / L | 未领取 |
| [WP10-16 盲盘轮次、冻结Review与整组调整](../../docs/development/task-plans/WP10.md#wp10-16) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP10-04 | CODE / L | 未领取 |
| [WP10-17 批次追溯图与永久召回范围](../../docs/development/task-plans/WP10.md#wp10-17) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP09-04, WP09-06, WP10-04 | CODE / L | 未领取 |
| [WP10-18 LPN/序列/托盘成员树与整树动作](../../docs/development/task-plans/WP10.md#wp10-18) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP02-19, WP05-02, WP10-10, WP10-17, WP10-47 | CODE / L | 未领取 |
| [WP10-19 出库授权、拣配暂存包装与真实发运Owner](../../docs/development/task-plans/WP10.md#wp10-19) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP04-32, WP06-13, WP07-10, WP10-03, WP10-08 | CODE / L | 未领取 |
| [WP10-20 出库取消、剩余Scope与迟到执行恢复](../../docs/development/task-plans/WP10.md#wp10-20) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP10-19 | CODE / L | 未领取 |
| [WP10-21 交付H26 H27实际采用、完整财务轴与恢复页面](../../docs/development/task-plans/WP10.md#wp10-21) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP02-14, WP04-21, WP10-52, WP11-27, WP11-38, WP11-62 | CODE / L | 未领取 |
| [WP10-22 客退授权、原发运行预算与分批收货](../../docs/development/task-plans/WP10.md#wp10-22) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP04-34, WP10-13, WP10-19 | CODE / L | 未领取 |
| [WP10-23 客退五处置、财务排他H30与完整关闭](../../docs/development/task-plans/WP10.md#wp10-23) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP04-31, WP09-09, WP10-22, WP11-44, WP11-63 | CODE / L | 未领取 |
| [WP10-24 过期损坏处置Review、物理应用与财务恢复](../../docs/development/task-plans/WP10.md#wp10-24) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP09-09, WP10-04 | CODE / L | 未领取 |
| [WP10-25 仓库scope授权、功能变更与真实DRAIN](../../docs/development/task-plans/WP10.md#wp10-25) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP10-04, WP10-09 | CODE / L | 未领取 |
| [WP10-26 套件配方版本、订单与完整组成计划](../../docs/development/task-plans/WP10.md#wp10-26) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP05-10, WP05-12, WP05-30, WP05-32, WP05-39, WP10-01 | CODE / M | 未领取 |
| [WP10-27 套件组拆整组数量事务与旧部分成功对账](../../docs/development/task-plans/WP10.md#wp10-27) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP05-42, WP10-04, WP10-08, WP10-26 | CODE / L | 未领取 |
| [WP10-28 库位优化固定cut分析、独立ABC与批准](../../docs/development/task-plans/WP10.md#wp10-28) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP05-02, WP10-01, WP10-05, WP10-47 | CODE / M | 未领取 |
| [WP10-29 库位优化Transfer派发、停止与结果采用](../../docs/development/task-plans/WP10.md#wp10-29) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP10-10, WP10-28 | CODE / L | 未领取 |
| [WP10-30 补货规则、目标义务与多源建议](../../docs/development/task-plans/WP10.md#wp10-30) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP10-05 | CODE / M | 未领取 |
| [WP10-31 补货调拨执行、取消和完整Stage恢复](../../docs/development/task-plans/WP10.md#wp10-31) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP10-10, WP10-30 | CODE / L | 未领取 |
| [WP10-32 越库真实来源匹配、分阶段收发与恢复](../../docs/development/task-plans/WP10.md#wp10-32) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP10-08, WP10-14, WP10-15, WP10-19 | CODE / L | 未领取 |
| [WP10-33 卷材真实登记、几何映射与匹配](../../docs/development/task-plans/WP10.md#wp10-33) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP05-12, WP05-39, WP10-05 | CODE / M | 未领取 |
| [WP10-34 卷材分切、消费与真实废边组成事务](../../docs/development/task-plans/WP10.md#wp10-34) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP05-12, WP05-32, WP05-39, WP05-42, WP09-02, WP10-04, WP10-33 | CODE / L | 未领取 |
| [WP10-35 余料来源激活、预留消耗与处置](../../docs/development/task-plans/WP10.md#wp10-35) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP08-06, WP10-05, WP10-08 | CODE / L | 未领取 |
| [WP10-36 版模实物使用、维护周期与退役](../../docs/development/task-plans/WP10.md#wp10-36) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP05-06, WP05-32, WP05-42, WP10-10, WP10-24, WP10-48 | CODE / L | 未领取 |
| [WP10-37 油墨Stock绑定、首开封和INKQ当前资格](../../docs/development/task-plans/WP10.md#wp10-37) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP05-12, WP05-39, WP09-02, WP10-05 | CODE / L | 未领取 |
| [WP10-38 油墨双父混合、谱系与同事务守恒](../../docs/development/task-plans/WP10.md#wp10-38) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP05-42, WP10-04, WP10-37 | CODE / L | 未领取 |
| [WP10-39 VMI历史保管量、用量与精确计费依据](../../docs/development/task-plans/WP10.md#wp10-39) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP10-05 | CODE / L | 未领取 |
| [WP10-40 VMI计费确认、独立批准与Finance接收](../../docs/development/task-plans/WP10.md#wp10-40) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP10-39, WP11-26, WP11-27, WP12-07 | CODE / L | 未领取 |
| [WP10-41 样品真实保管移动、借还轮次与未归还义务](../../docs/development/task-plans/WP10.md#wp10-41) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP10-05, WP10-10 | CODE / L | 未领取 |
| [WP10-42 仓储设备真实任务、OEM回执与源Owner应用](../../docs/development/task-plans/WP10.md#wp10-42) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP02-19, WP10-09, WP10-12 | CODE / L | 未领取 |
| [WP10-43 遥测真实来源、迟到读数与版本化告警](../../docs/development/task-plans/WP10.md#wp10-43) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-12, WP02-13, WP02-19 | CODE / M | 未领取 |
| [WP10-44 仓储报表固定cut、双时间与权威维度](../../docs/development/task-plans/WP10.md#wp10-44) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP02-23, WP10-05 | CODE / L | 未领取 |
| [WP10-45 仓储ABC、库龄呆滞与分析发布](../../docs/development/task-plans/WP10.md#wp10-45) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP10-44 | CODE / M | 未领取 |
| [WP10-46 采购质量结果在库存正式应用与可用性回流](../../docs/development/task-plans/WP10.md#wp10-46) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP09-04, WP10-14 | CODE / L | 未领取 |
| [WP10-47 既有物料、UOM与容量维度原Owner目录适配](../../docs/development/task-plans/WP10.md#wp10-47) | Main | 待安排 | S01-02, S01-07, S02-00, WP02-03, WP02-10, WP02-13 | CODE / M | 未领取 |
| [WP10-48 WMS版模实例身份、当前头与HoldExact提供者](../../docs/development/task-plans/WP10.md#wp10-48) | Main | 待安排 | S01-07, S02-00, WP02-03, WP02-10, WP02-13, WP05-04 | CODE / M | 未领取 |
| [WP10-49 WMS完整Canonical Target与正式映射提供者](../../docs/development/task-plans/WP10.md#wp10-49) | Main | 待安排 | S01-07, S02-00, WP02-03, WP02-10, WP02-13, WP10-01, WP10-47 | CODE / M | 未领取 |
| [WP10-50 SPACE完整Target在WMS的原子采用Owner](../../docs/development/task-plans/WP10.md#wp10-50) | Main | 待安排 | S01-02, S01-07, S02-00, WP02-03, WP02-10, WP02-13, WP10-04, WP10-25, WP10-49 | CODE / M | 未领取 |
| [WP10-51 SPACE来源停止与恢复的WMS当前控制适配](../../docs/development/task-plans/WP10.md#wp10-51) | Main | 待安排 | S01-02, S01-07, S02-00, WP02-03, WP02-10, WP02-13, WP10-06, WP10-49, WP10-50 | CODE / M | 未领取 |
| [WP10-52 承运POD累计Proof与F0当前源提供者](../../docs/development/task-plans/WP10.md#wp10-52) | Main | 待安排 | WP02-10, WP02-13, WP02-14, WP10-05, WP10-19 | CODE / L | 未领取 |

## WP11 财务核算与结算

| 任务 | 仓库 | 状态 | 直接前置 | 类型/规模 | 对话 |
|---|---|---|---|---|---|
| [WP11-01 财务资料版本工作流与科目时间线](../../docs/development/task-plans/WP11.md#wp11-01) | Main | 待安排 | S02-00, WP02-10, WP02-13 | CODE / M | 未领取 |
| [WP11-02 税码与成本中心专业版本发布](../../docs/development/task-plans/WP11.md#wp11-02) | Main | 待安排 | WP02-10, WP02-13, WP05-08, WP08-16, WP11-01 | CODE / M | 未领取 |
| [WP11-03 供应商受益银行版本与秘密披露](../../docs/development/task-plans/WP11.md#wp11-03) | Main | 待安排 | WP02-10, WP02-13, WP02-14, WP02-15, WP04-02, WP11-01 | CODE / M | 未领取 |
| [WP11-04 FM15同事务采用与旧资料绑定](../../docs/development/task-plans/WP11.md#wp11-04) | Main | 待安排 | WP02-10, WP02-13, WP11-01, WP11-02, WP11-03 | CODE / L | 未领取 |
| [WP11-05 信用政策和Credit到GL身份Registry](../../docs/development/task-plans/WP11.md#wp11-05) | Main | 待安排 | S01-03, S01-10, WP02-10, WP02-13, WP04-02 | CODE / M | 未领取 |
| [WP11-06 信用完整敞口Cut和吸收守恒](../../docs/development/task-plans/WP11.md#wp11-06) | Main | 待安排 | WP02-10, WP02-13, WP04-32, WP11-05, WP11-62 | CODE / M | 未领取 |
| [WP11-07 信用预留、特批与Sales AR最终参与](../../docs/development/task-plans/WP11.md#wp11-07) | Main | 待安排 | WP02-10, WP02-13, WP11-06 | CODE / L | 未领取 |
| [WP11-08 财务日历、期间和共同写入门](../../docs/development/task-plans/WP11.md#wp11-08) | Main | 待安排 | WP02-10, WP02-13, WP11-01 | CODE / M | 未领取 |
| [WP11-09 汇率资料发布与独立恢复批准](../../docs/development/task-plans/WP11.md#wp11-09) | Main | 待安排 | WP02-10, WP02-13, WP11-01 | CODE / M | 未领取 |
| [WP11-10 业务汇率冻结与F09同事务用途](../../docs/development/task-plans/WP11.md#wp11-10) | Main | 待安排 | WP02-10, WP02-13, WP11-09 | CODE / M | 未领取 |
| [WP11-11 本方银行账户门与流水Profile导入](../../docs/development/task-plans/WP11.md#wp11-11) | Main | 待安排 | WP02-10, WP02-13, WP11-04, WP11-08, WP11-64 | CODE / M | 未领取 |
| [WP11-12 预算版本、复制分解与固定导入](../../docs/development/task-plans/WP11.md#wp11-12) | Main | 待安排 | WP02-10, WP02-13, WP11-04, WP11-08 | CODE / M | 未领取 |
| [WP11-13 预算审批独立激活与所有过账共同Guard](../../docs/development/task-plans/WP11.md#wp11-13) | Main | 待安排 | WP02-10, WP02-13, WP11-12, WP12-07 | CODE / L | 未领取 |
| [WP11-14 GL准确来源、完整计划与手工草稿](../../docs/development/task-plans/WP11.md#wp11-14) | Main | 待安排 | WP02-10, WP02-13, WP11-04, WP11-08, WP11-10 | CODE / M | 未领取 |
| [WP11-15 GL冻结审批、决定Inbox与独立应用](../../docs/development/task-plans/WP11.md#wp11-15) | Main | 待安排 | WP02-10, WP02-13, WP11-14, WP12-07 | CODE / M | 未领取 |
| [WP11-16 GL逐腿过账与真实Completion](../../docs/development/task-plans/WP11.md#wp11-16) | Main | 待安排 | WP02-10, WP02-13, WP02-16, WP11-11, WP11-13, WP11-15 | CODE / L | 未领取 |
| [WP11-17 GL整腿冲销、当前结果门与历史Ledger读取](../../docs/development/task-plans/WP11.md#wp11-17) | Main | 待安排 | WP02-10, WP02-13, WP11-16, WP12-07 | CODE / L | 未领取 |
| [WP11-18 应付收票、精确来源与审批](../../docs/development/task-plans/WP11.md#wp11-18) | Main | 待安排 | WP02-10, WP02-13, WP04-02, WP07-14, WP11-04, WP11-10, WP11-66, WP12-07 | CODE / M | 未领取 |
| [WP11-19 应付完整Manifest过账与义务应用](../../docs/development/task-plans/WP11.md#wp11-19) | Main | 待安排 | WP02-10, WP02-13, WP11-16, WP11-18 | CODE / L | 未领取 |
| [WP11-20 应付转账准备、批准与真实出款记录](../../docs/development/task-plans/WP11.md#wp11-20) | Main | 待安排 | WP02-10, WP02-13, WP11-03, WP11-11, WP11-19, WP11-64, WP12-07, WP12-19 | CODE / M | 未领取 |
| [WP11-21 应付现金与支票方式的证据采用](../../docs/development/task-plans/WP11.md#wp11-21) | Main | 待安排 | WP02-10, WP02-13, WP11-20, WP11-64 | CODE / M | 未领取 |
| [WP11-22 应付整批核销与五组成会计恢复](../../docs/development/task-plans/WP11.md#wp11-22) | Main | 待安排 | WP02-10, WP02-13, WP11-16, WP11-21 | CODE / L | 未领取 |
| [WP11-23 应付反核销、贷项和真实退款分轴](../../docs/development/task-plans/WP11.md#wp11-23) | Main | 待安排 | WP02-10, WP02-13, WP07-10, WP07-14, WP11-17, WP11-22, WP11-64, WP11-66 | CODE / M | 未领取 |
| [WP11-24 MATCH更正Admission永久关闭与保留历史](../../docs/development/task-plans/WP11.md#wp11-24) | Main | 待安排 | WP02-10, WP02-13, WP07-14, WP11-23, WP11-66 | CODE / L | 未领取 |
| [WP11-25 应付历史账龄与GL完整勾稽](../../docs/development/task-plans/WP11.md#wp11-25) | Main | 待安排 | WP02-10, WP02-13, WP11-17, WP11-19, WP11-22, WP11-23 | CODE / M | 未领取 |
| [WP11-26 应收计费政策、义务切片与跨版本Claim](../../docs/development/task-plans/WP11.md#wp11-26) | Main | 待安排 | WP02-10, WP02-13, WP04-02, WP04-32, WP10-19, WP10-52, WP11-04, WP11-10, WP11-62, WP12-07 | CODE / M | 未领取 |
| [WP11-27 应收独立收入事件与本域过账应用](../../docs/development/task-plans/WP11.md#wp11-27) | Main | 待安排 | WP02-10, WP02-13, WP11-07, WP11-16, WP11-26 | CODE / L | 未领取 |
| [WP11-28 收款来源认领与独立业务批准](../../docs/development/task-plans/WP11.md#wp11-28) | Main | 待安排 | WP02-10, WP02-13, WP11-11, WP11-27, WP11-64, WP12-07, WP12-19 | CODE / M | 未领取 |
| [WP11-29 收款清算会计与FinalReceipt原子应用](../../docs/development/task-plans/WP11.md#wp11-29) | Main | 待安排 | WP02-10, WP02-13, WP11-16, WP11-28, WP11-64 | CODE / L | 未领取 |
| [WP11-30 应收多款多票整批核销](../../docs/development/task-plans/WP11.md#wp11-30) | Main | 待安排 | WP02-10, WP02-13, WP11-29 | CODE / L | 未领取 |
| [WP11-31 应收已应用反向与未应用补偿](../../docs/development/task-plans/WP11.md#wp11-31) | Main | 待安排 | WP02-10, WP02-13, WP11-17, WP11-30 | CODE / L | 未领取 |
| [WP11-32 应收退货贷项与库存价值独立协调](../../docs/development/task-plans/WP11.md#wp11-32) | Main | 待安排 | WP02-10, WP02-13, WP04-27, WP09-12, WP10-22, WP11-27, WP11-31, WP11-44 | CODE / L | 未领取 |
| [WP11-33 应收退款授权、资金事实与撤销恢复](../../docs/development/task-plans/WP11.md#wp11-33) | Main | 待安排 | WP02-10, WP02-13, WP11-29, WP11-32, WP11-64, WP12-19 | CODE / L | 未领取 |
| [WP11-34 应收历史账龄、清算与GL全量对账](../../docs/development/task-plans/WP11.md#wp11-34) | Main | 待安排 | WP02-10, WP02-13, WP11-17, WP11-27, WP11-30, WP11-32 | CODE / M | 未领取 |
| [WP11-35 库存价值来源、所有权与收货价值](../../docs/development/task-plans/WP11.md#wp11-35) | Main | 待安排 | S01-04, S01-10, WP02-10, WP02-13, WP07-06, WP10-03, WP10-14, WP11-04, WP11-08 | CODE / L | 未领取 |
| [WP11-36 FIFO移动平均标准和个别计价](../../docs/development/task-plans/WP11.md#wp11-36) | Main | 待安排 | WP02-10, WP02-13, WP11-35 | CODE / L | 未领取 |
| [WP11-37 库存GRNI与AP清算价值采用](../../docs/development/task-plans/WP11.md#wp11-37) | Main | 待安排 | WP02-10, WP02-13, WP07-14, WP11-19, WP11-36 | CODE / M | 未领取 |
| [WP11-38 出库层消耗与唯一COGS来源](../../docs/development/task-plans/WP11.md#wp11-38) | Main | 待安排 | WP02-10, WP02-13, WP10-19, WP10-52, WP11-16, WP11-36 | CODE / L | 未领取 |
| [WP11-39 制造成本费率版本和标准Basis](../../docs/development/task-plans/WP11.md#wp11-39) | Main | 待安排 | WP02-10, WP02-13, WP05-08, WP08-16, WP11-04, WP11-10 | CODE / M | 未领取 |
| [WP11-40 制造实际来源归集与完整成本计算](../../docs/development/task-plans/WP11.md#wp11-40) | Main | 待安排 | WP02-10, WP02-13, WP07-15, WP08-06, WP08-07, WP08-08, WP11-36, WP11-39 | CODE / M | 未领取 |
| [WP11-41 多输出成本分配与完整结转Plan](../../docs/development/task-plans/WP11.md#wp11-41) | Main | 待安排 | WP02-10, WP02-13, WP08-08, WP09-12, WP11-40, WP12-07 | CODE / L | 未领取 |
| [WP11-42 成本逐腿结转、OwnerApply和更正](../../docs/development/task-plans/WP11.md#wp11-42) | Main | 待安排 | WP02-10, WP02-13, WP11-17, WP11-41 | CODE / L | 未领取 |
| [WP11-43 成本结果到FG WIP价值准确采用](../../docs/development/task-plans/WP11.md#wp11-43) | Main | 待安排 | WP02-10, WP02-13, WP11-36, WP11-42 | CODE / M | 未领取 |
| [WP11-44 库存价值更正、退回谱系与H30专用绑定](../../docs/development/task-plans/WP11.md#wp11-44) | Main | 待安排 | WP02-10, WP02-13, WP09-12, WP10-22, WP11-17, WP11-37, WP11-38, WP11-43, WP11-63 | CODE / L | 未领取 |
| [WP11-45 库存价值对账、冷恢复与旧桥采用](../../docs/development/task-plans/WP11.md#wp11-45) | Main | 待安排 | WP02-10, WP02-13, WP10-05, WP11-44 | CODE / L | 未领取 |
| [WP11-46 历史敞口重估计划与会计应用](../../docs/development/task-plans/WP11.md#wp11-46) | Main | 待安排 | WP02-10, WP02-13, WP11-10, WP11-16, WP11-25, WP11-34, WP11-64, WP12-07 | CODE / L | 未领取 |
| [WP11-47 重估次期镜像义务与原期履行](../../docs/development/task-plans/WP11.md#wp11-47) | Main | 待安排 | WP02-10, WP02-13, WP11-17, WP11-46 | CODE / L | 未领取 |
| [WP11-48 预算与实际完整历史比较](../../docs/development/task-plans/WP11.md#wp11-48) | Main | 待安排 | WP02-10, WP02-13, WP11-13, WP11-17 | CODE / M | 未领取 |
| [WP11-49 银行完整组匹配、原件更正与释放](../../docs/development/task-plans/WP11.md#wp11-49) | Main | 待安排 | WP02-10, WP02-13, WP11-11, WP11-17, WP11-25, WP11-34, WP11-64, WP12-07 | CODE / M | 未领取 |
| [WP11-50 银行单边事项会计Claim与原应用](../../docs/development/task-plans/WP11.md#wp11-50) | Main | 待安排 | WP02-10, WP02-13, WP11-49 | CODE / M | 未领取 |
| [WP11-51 银行冲突组恢复和结账完整Cut](../../docs/development/task-plans/WP11.md#wp11-51) | Main | 待安排 | WP02-10, WP02-13, WP11-50, WP12-07 | CODE / L | 未领取 |
| [WP11-52 资产类别和卡片受控激活](../../docs/development/task-plans/WP11.md#wp11-52) | Main | 待安排 | WP02-10, WP02-13, WP08-16, WP11-04, WP11-08, WP12-07 | CODE / M | 未领取 |
| [WP11-53 资产四法折旧与完整期间Claim](../../docs/development/task-plans/WP11.md#wp11-53) | Main | 待安排 | WP02-10, WP02-13, WP08-07, WP11-16, WP11-52, WP12-07 | CODE / L | 未领取 |
| [WP11-54 资产处置前最终折旧链与会计完成](../../docs/development/task-plans/WP11.md#wp11-54) | Main | 待安排 | WP02-10, WP02-13, WP11-17, WP11-53 | CODE / L | 未领取 |
| [WP11-55 资产更正、处置逆向与Close义务目录](../../docs/development/task-plans/WP11.md#wp11-55) | Main | 待安排 | WP02-10, WP02-13, WP11-54 | CODE / L | 未领取 |
| [WP11-56 月结完整Checklist和持久CloseRun](../../docs/development/task-plans/WP11.md#wp11-56) | Main | 待安排 | 共13项，详见任务卡 | CODE / L | 未领取 |
| [WP11-57 年结全财年成员与锁期专用采用](../../docs/development/task-plans/WP11.md#wp11-57) | Main | 待安排 | WP02-10, WP02-13, WP11-17, WP11-56 | CODE / L | 未领取 |
| [WP11-58 月年反结与Close调整恢复](../../docs/development/task-plans/WP11.md#wp11-58) | Main | 待安排 | WP02-10, WP02-13, WP11-57, WP12-07 | CODE / L | 未领取 |
| [WP11-59 完整GL事实Cut与试算不可变快照](../../docs/development/task-plans/WP11.md#wp11-59) | Main | 待安排 | WP02-10, WP02-13, WP11-17 | CODE / M | 未领取 |
| [WP11-60 财报定义发布与资产负债映射](../../docs/development/task-plans/WP11.md#wp11-60) | Main | 待安排 | WP02-10, WP02-13, WP11-04, WP11-59 | CODE / M | 未领取 |
| [WP11-61 损益连续期间、原知重述与谱系导出](../../docs/development/task-plans/WP11.md#wp11-61) | Main | 待安排 | WP02-10, WP02-13, WP02-14, WP11-60 | CODE / M | 未领取 |
| [WP11-62 应收权威Source目录和历史Cut读取端口](../../docs/development/task-plans/WP11.md#wp11-62) | Main | 待安排 | WP02-10, WP02-13, WP11-04, WP11-14 | CODE / M | 未领取 |
| [WP11-63 FIN INV H30准确绑定与排他资格提供者](../../docs/development/task-plans/WP11.md#wp11-63) | Main | 待安排 | WP02-10, WP02-13, WP09-12, WP10-22, WP11-35 | CODE / M | 未领取 |
| [WP11-64 真实Funds只读证据、完整目录与最终参加适配](../../docs/development/task-plans/WP11.md#wp11-64) | Main | 待安排 | WP02-10, WP02-11, WP02-13, WP11-01 | CODE / M | 未领取 |
| [WP11-65 BP财务引用只读当前源与共同门](../../docs/development/task-plans/WP11.md#wp11-65) | Main | 待安排 | WP02-10, WP02-13, WP11-01 | CODE / M | 未领取 |
| [WP11-66 AP原义务共享预算与无内部提交参加底座](../../docs/development/task-plans/WP11.md#wp11-66) | Main | 待安排 | WP02-10, WP02-13, WP07-14, WP11-04 | CODE / M | 未领取 |

## WP12 流程与审批

| 任务 | 仓库 | 状态 | 直接前置 | 类型/规模 | 对话 |
|---|---|---|---|---|---|
| [WP12-01 不可变流程表单草稿与设计器版本](../../docs/development/task-plans/WP12.md#wp12-01) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13 | CODE / M | 未领取 |
| [WP12-02 定义发布预览、准确依赖pin与独立启停](../../docs/development/task-plans/WP12.md#wp12-02) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP12-01 | CODE / L | 未领取 |
| [WP12-03 个人草稿生命周期与显式无损重基](../../docs/development/task-plans/WP12.md#wp12-03) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP02-14, WP02-15, WP12-01 | CODE / M | 未领取 |
| [WP12-04 DIRECT/DRAFT提交的五上下文原子准入](../../docs/development/task-plans/WP12.md#wp12-04) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP12-02, WP12-03 | CODE / L | 未领取 |
| [WP12-05 表单目录收藏、历史查询和最终下载披露](../../docs/development/task-plans/WP12.md#wp12-05) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP02-14, WP02-15, WP12-04, WP12-07 | CODE / M | 未领取 |
| [WP12-06 审批业务绑定与冻结提交TxS/TxE](../../docs/development/task-plans/WP12.md#wp12-06) | Main | 待安排 | S01-02, S01-07, S02-00, WP02-10, WP02-13, WP12-02 | CODE / L | 未领取 |
| [WP12-07 审批决定TxD、Owner交付与回执连续链](../../docs/development/task-plans/WP12.md#wp12-07) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP12-06 | CODE / L | 未领取 |
| [WP12-08 待办办理双CAS、真实Actor与会签轮次](../../docs/development/task-plans/WP12.md#wp12-08) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP12-04, WP12-07, WP12-10 | CODE / L | 未领取 |
| [WP12-09 退回重送、转交加签和无人审批恢复](../../docs/development/task-plans/WP12.md#wp12-09) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP12-08 | CODE / L | 未领取 |
| [WP12-10 委派授权修订与历史职责映射](../../docs/development/task-plans/WP12.md#wp12-10) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP12-02 | CODE / M | 未领取 |
| [WP12-11 准确截止策略与超时worker竞争](../../docs/development/task-plans/WP12.md#wp12-11) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP02-21, WP12-08, WP12-09 | CODE / M | 未领取 |
| [WP12-12 既有审批Owner路由迁移与单一写者切换](../../docs/development/task-plans/WP12.md#wp12-12) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP05-20, WP12-07, WP12-08, WP12-19 | CODE / L | 未领取 |
| [WP12-13 触发器与连接器不可变版本及控制审批](../../docs/development/task-plans/WP12.md#wp12-13) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP12-02, WP12-07 | CODE / L | 未领取 |
| [WP12-14 定时事件消息与手动触发真实实例](../../docs/development/task-plans/WP12.md#wp12-14) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP02-21, WP12-04, WP12-13 | CODE / L | 未领取 |
| [WP12-15 流程预演与cron解释的无效果边界](../../docs/development/task-plans/WP12.md#wp12-15) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP12-13 | CODE / M | 未领取 |
| [WP12-16 连接器外呼的本地准入与稳定effectKey](../../docs/development/task-plans/WP12.md#wp12-16) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP12-07, WP12-13 | CODE / L | 未领取 |
| [WP12-17 外部结果持久接收与本地流程/Owner应用](../../docs/development/task-plans/WP12.md#wp12-17) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP12-07, WP12-16 | CODE / L | 未领取 |
| [WP12-18 连接器终态失败、错误边与原键恢复](../../docs/development/task-plans/WP12.md#wp12-18) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP12-17 | CODE / L | 未领取 |
| [WP12-19 可信业务撤回与阻塞回执恢复](../../docs/development/task-plans/WP12.md#wp12-19) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-13, WP12-07 | CODE / L | 未领取 |
| [WP12-20 通知偏好、投递和已读水位](../../docs/development/task-plans/WP12.md#wp12-20) | Main | 待安排 | S01-02, S02-00, WP02-10, WP02-12, WP02-13, WP12-08, WP12-11 | CODE / M | 未领取 |

## WP13 空间设计与运营

| 任务 | 仓库 | 状态 | 直接前置 | 类型/规模 | 对话 |
|---|---|---|---|---|---|
| [WP13-01 空间源身份与正式WMS映射目录](../../docs/development/task-plans/WP13.md#wp13-01) | Main | 待安排 | S01-02, S01-05, S01-07, S02-00, WP02-10, WP02-13, WP10-49 | CODE / M | 未领取 |
| [WP13-02 设计永久冻结门与全部写者参加](../../docs/development/task-plans/WP13.md#wp13-02) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP02-21, WP13-01 | CODE / L | 未领取 |
| [WP13-03 编码拖拽重建的完整预览与原命令](../../docs/development/task-plans/WP13.md#wp13-03) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP13-01, WP13-02 | CODE / M | 未领取 |
| [WP13-04 S5完整成员grant集合和61字段映射底座](../../docs/development/task-plans/WP13.md#wp13-04) | Main | 待安排 | S01-02, S01-05, S01-07, S02-00, WP02-10, WP02-13 | CODE / L | 未领取 |
| [WP13-05 外部门户全出口撤权重核与安全分页](../../docs/development/task-plans/WP13.md#wp13-05) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP10-05, WP10-09, WP10-50, WP13-04, WP13-10 | CODE / L | 未领取 |
| [WP13-06 CAD/Excel来源解析、映射与完整预览](../../docs/development/task-plans/WP13.md#wp13-06) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP02-14, WP02-21, WP13-01 | CODE / M | 未领取 |
| [WP13-07 导入队列到实际提交的原子应用](../../docs/development/task-plans/WP13.md#wp13-07) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP02-21, WP13-02, WP13-06 | CODE / L | 未领取 |
| [WP13-08 空间Publication完整封存与独立源授权](../../docs/development/task-plans/WP13.md#wp13-08) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP10-49, WP13-01, WP13-02, WP13-04 | CODE / L | 未领取 |
| [WP13-09 固定空间交付、WMS采用回执与原键恢复](../../docs/development/task-plans/WP13.md#wp13-09) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP10-50, WP13-08 | CODE / L | 未领取 |
| [WP13-10 Runtime真实观察与唯一current晋升](../../docs/development/task-plans/WP13.md#wp13-10) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP10-50, WP13-04, WP13-09 | CODE / L | 未领取 |
| [WP13-11 源停止后继与完整Target未选项KEEP](../../docs/development/task-plans/WP13.md#wp13-11) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP10-51, WP13-08, WP13-09 | CODE / L | 未领取 |
| [WP13-12 冻结后继草稿、模板底图与旧编辑兼容](../../docs/development/task-plans/WP13.md#wp13-12) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP02-14, WP13-02, WP13-08 | CODE / L | 未领取 |
| [WP13-13 布局情景分支、仿真比较和受控采用](../../docs/development/task-plans/WP13.md#wp13-13) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP13-10, WP13-12 | CODE / M | 未领取 |
| [WP13-14 运营事实完整cut与正式容量单位](../../docs/development/task-plans/WP13.md#wp13-14) | Main | 待安排 | 共13项，详见任务卡 | CODE / L | 未领取 |
| [WP13-15 运营建议审核与真实Owner执行回执](../../docs/development/task-plans/WP13.md#wp13-15) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP08-11, WP10-10, WP10-12, WP10-14, WP10-31, WP10-42, WP13-14 | CODE / L | 未领取 |
| [WP13-16 AI真实生成能力、预算与提案来源](../../docs/development/task-plans/WP13.md#wp13-16) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP02-14, WP02-15, WP02-21, WP13-02, WP13-04 | CODE / L | 未领取 |
| [WP13-17 AI逐项审阅到设计原子应用](../../docs/development/task-plans/WP13.md#wp13-17) | Main | 待安排 | S01-02, S01-05, S02-00, WP02-10, WP02-13, WP13-02, WP13-12, WP13-16 | CODE / L | 未领取 |

## WP14 全链集成与交付验证

| 任务 | 仓库 | 状态 | 直接前置 | 类型/规模 | 对话 |
|---|---|---|---|---|---|
| [WP14-01 全链验收映射、数据责任与证据索引](../../docs/development/task-plans/WP14.md#wp14-01) | Main | 已交付 | 无任务前置，仍需准入核对 | DESIGN / M | D / WP14-01 |
| [WP14-02 CRM入站、转化与商业交接闭环](../../docs/development/task-plans/WP14.md#wp14-02) | Main | 待安排 | 共62项，详见任务卡 | VALIDATION / M | 未领取 |
| [WP14-03 报价订单、需求释放与计划转单闭环](../../docs/development/task-plans/WP14.md#wp14-03) | Main | 待安排 | 共48项，详见任务卡 | VALIDATION / M | 未领取 |
| [WP14-04 采购来料、检验入库与供给更新闭环](../../docs/development/task-plans/WP14.md#wp14-04) | Main | 待安排 | 共93项，详见任务卡 | VALIDATION / M | 未领取 |
| [WP14-05 制造追加料、报工质量入库与领料闭环](../../docs/development/task-plans/WP14.md#wp14-05) | Main | 待安排 | 共96项，详见任务卡 | VALIDATION / M | 未领取 |
| [WP14-06 财务原义务、退货纠正与关账闭环](../../docs/development/task-plans/WP14.md#wp14-06) | Main | 待安排 | 共168项，详见任务卡 | VALIDATION / M | 未领取 |
| [WP14-07 工程许可、OA决定与SPACE实际采用闭环](../../docs/development/task-plans/WP14.md#wp14-07) | Main | 待安排 | 共133项，详见任务卡 | VALIDATION / M | 未领取 |
| [WP14-08 跨域撤权、Unknown、迟到及恢复专项](../../docs/development/task-plans/WP14.md#wp14-08) | Main | 待安排 | 共347项，详见任务卡 | VALIDATION / M | 未领取 |
| [WP14-09 PostgreSQL旧写者兼容与迁移回退演练](../../docs/development/task-plans/WP14.md#wp14-09) | Main | 待安排 | 共347项，详见任务卡 | VALIDATION / M | 未领取 |
| [WP14-10 125目标完成门与发布候选交接](../../docs/development/task-plans/WP14.md#wp14-10) | Main | 待安排 | S01-12, WP01-01, WP01-02, WP14-02, WP14-03, WP14-04, WP14-05, WP14-06, WP14-07, WP14-08, WP14-09 | VALIDATION / M | 未领取 |

[PostgreSQL基线](08-postgresql-baseline.md)：后续新增开发不要求SQL Server/双库。单任务通过不关闭整目标。CRM可以在所需合同/身份/事务子集完成后推进，不等待全部财务任务。
