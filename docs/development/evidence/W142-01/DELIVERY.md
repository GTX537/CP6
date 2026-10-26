# W142-01 — 调拨本地需求预算持久前置

2026-10-26，COORD 单人。Main 基线 befe791d7600f39d15007f8058ee29562b321e04。原估70–100分钟；官方 START 11:36:24Z，最终 START→finish 时间以外部 wave142-final-verification.json 核实，包括验证/运输等待，不把此前只读调查混入纯编码时间。

## 交付范围

Core 内部 TransferDemandBudgetV1 与 PostgreSQL 前向迁移：WMS_TRANSFER 本地 SourceDemand 原文/版本，准确显式双轴需求区间，跨请求/版本共享预算保护，原绑定恢复、准确释放与终态防复活。完整预算键一次声明、同实际 Main 连接/事务、环境/沙箱/租户首键，固定8位数量不舍入；数据库补充原文不可变、重叠、版本与终态约束。SourceDemand/Binding 使用共同 evidence ID 唯一域。

[增量合同与上层责任](../../../design-handbook/remediation/main-transfer-local-demand-v1.md)。没有新增公开 API、DI 注册、角色权限或主数据适配；实际 T04 的 context/catalog、当前权限、可信部署及源原文验证尚未采用。组件结构验证不能冒充授权。没有 Stock 数量写入，也未解除 REP/RMA 命令围栏。

## 实际验证

唯一最终有效运行 `D:/CP6-local-test/wave142/foundation/final-20261026T114851Z.json`：11项全部通过，0失败、0跳过；源码和迁移最终输入实际编译，隔离 PostgreSQL18 全部迁移实际执行。覆盖非零需求坐标/分段双轴、最大21,8数量往返、版本变化不重置预算、停止来源、旧绑定原结果与关闭状态、真正数据库锁等待后重叠拒绝、当前事务/完整锁声明、租户/环境/沙箱隔离、与另一实际写入共同回滚、原文篡改/SQL重叠/NULL关闭修订/终态复活拒绝、跨类型同 evidence ID 冲突。未重复运行未受影响的前端/全仓套件。

失败保留在同目录：最初误并发两个构建导致 red113852 的 CS2012 文件占用；red-behavior114001 遇 Space 名称冲突，两次均未进入 PostgreSQL 测试。之后已观察终止，再串行运行；未删除任何失败日志。red-native114132 的2项未实现行为失败；green114414 暴露 numrange[] 被驱动推断成 multirange；boundaries114600 暴露触发器 old 别名与 OLD 冲突。均已修复。中间10项成功不重复计数，最终11项覆盖随后关闭NULL约束修复。

一次执行者 SPEC/QUALITY 审查完整任务差异；修正关闭修订号 NULL 可以绕 CHECK 的风险并通过定向复测。未启动额外代理、Actions、既有环境迁移或部署。工作流与依赖未改变，推送前复用 W118 内容一致的事件审查并核实本任务运行数0。

## 状态与收尾

W141 final proof 已核销一次并关闭其分页窗口。W142 Handoff 运输和 Main 产品采用分别核对；本报告提交时正常 PR/官方 finish 仍在进行，准确 source/merge SHA、分支处置、固定席位和进程结果以规划目录 wave142-main/handoff-delivery.json、wave142-final-verification.json 与官方 state.json 为准。固定目录保留复用，用户文件与依赖缓存保留。

原七天目标仍 ACTIVE，D1/D4 已关闭，其余阶段不提升。正式375卡、125完整目标及247运行验收不因本内部组件计为完成。下一步接入正式 Transfer context/主数据/当前权限与 Request/Draft 原操作生命周期，再推进 REP SourceDemand/目标预算/Transfer→Stock、RMA IN、D3/D5/D6/D7剩余出口。
