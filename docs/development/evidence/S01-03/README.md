# S01-03 授信精确金额交付证据

任务窗口：`CP6.Entity/Contracts/FinCredit/`、`eng/contracts/exact-values-tests/CreditAmountTests.cs` 和本目录。基线 `8569b669fed56ddc7b53b7bb24666c7fb93d544a`；计划见 [PLAN.md](PLAN.md)。COORD 管理台账、生成页、四状态页与串行合并。

[准入核验](entry-check.json)记录冻结包 `CP6-S01-CONTRACT-INPUTS/1.0.0` 的三个 Git 原字节 pin、原生 FIN-CREDIT Schema、全部 42 个字段绑定以及 25 个固定向量 ID。核验器 [verify-entry.py](verify-entry.py)只读取原合同/主线，不执行归档代码；运行测试使用叶子项目已复制的 vectors，不依赖本机归档目录。原生字段按精确 pointer 选择 signed/nonnegative/positive，不能根据字段名称推断域。

[START 回执](handoff-start.json)保留已发布且按固定提交读回的消息。运输成功不表示 COORD 已接收、准入或 DONE；本任务准入来自已登记的 READY/CONFIRMED 窗口。

本地测试由 [run-tests.ps1](implementation/run-tests.ps1)记录真实日志/TRX、SDK/运行环境、工作文件及产物 SHA-256；运行时的 base_head 仅是基础提交，不冒充尚未提交的实际源码。GREEN 实为 **205/205 通过、0 跳过**，含全部 25 固定向量；[验证回执](validation.json)、[完整用例清单](implementation/CASE-LIST.json)、[独立集中审查](review.md)均已核对通过。RED 与初次日志记录失败原件保留。[源码提交回执](source-receipt.json)将实际测试文件经 Git 过滤后逐一绑定 `036194e33a5c2bfce9f1960f47058e3001328338`，保留原始文件/提交 blob 摘要区别；这是复核既有执行，不是重新运行测试。[交付说明](delivery.md)明确可消费 API 和未覆盖范围。

范围限制：只交付精确值与 JSON 字符串边界。现有授信服务、所有写者、账务舍入、权限/租户/事务、数据库存储、跨仓消费者和生产验收均未采用或运行；完整 DR-OPS-IF-002 POS/NEG 及 247 业务运行场景仍 NOT_RUN。固定包状态、历史接受、41 静态 REFERENCE_ONLY 及旧 30/48 原件缺口不变。后续数据库只面向 PostgreSQL。

本轮不触发或取消 Actions，不改触发器/保护，不发包、不运行 DB/迁移或部署。每次业务候选 push/PR 前通过 [check-publication.py](check-publication.py)读取候选与最新 origin/main 的完整工作流事件及本地 action 依赖。[工作流回执](workflow-check.json)核对每树 13 份工作流：普通入口仅手动，既有 R2 入口仅受保护版本 Tag，分支 push/PR 不触发；不推 Tag。后续仅证据变动不改变被核工作流树，发布操作前仍核实际最终 SHA。
