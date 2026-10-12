# 第二轮开发派工

本轮由用户“可以，继续”授权，COORD完成WP01-02的交付汇总与分配依据。首轮四项已交付，准确回执及验证复用见[第一轮报告](../../evidence/first-wave-20261012/README.md)；本轮准入证据见[admission.json](../../evidence/WP01-02/admission.json)。所有业务验收仍NOT_RUN，WP05正式采纳仍PROPOSED。

| 沿用对话 | 本轮任务 | 执行包 | 实际工作区 |
|---|---|---|---|
| A（原S01-01） | S01-02 严格 JSON、NullOnly 与结构相等 | [S01-02](S01-02.md) | `D:/CP6-worktrees/s01-02-20261012` |
| B（原S02-00） | S01-05 Main TypeScript 精确值 | [S01-05](S01-05.md) | `D:/CP6-worktrees/s01-05-20261012` |
| C（原WP05-01） | S01-09 Platform 准确合同路由与原字节 | [S01-09](S01-09.md) | `D:/CP6.Platform-worktrees/s01-09-20261012` |
| D（原WP14-01） | 本轮暂不分配 | 保留已完成验收准备 | 原工作区保留 |

三个新任务为READY、entry_gate=CONFIRMED，均已有实际独立分支/工作树；尚未收到新任务START，未登记IN_PROGRESS。这里的A/B/C沿用用户已有对话，没有创建新对话或假定后台正在工作。现有工具不能直接唤醒这些对话，Handoff消息需在它们恢复时读取。

## 并行与共享文件

S01-02写CP6.Entity/Contracts/Wire及独立C#叶子测试；S01-05写cp6.web/src/contracts/exact；S01-09只写Platform的Messaging/私有测试资料。三个受权代码区域互不相交。现有Main项目/Vitest均能发现新增文件，不预先修改根sln、依赖锁或测试配置。Platform本轮只有一个任务，UnitTests.csproj已预留给S01-09，仅允许TestData/S01资源包含/输出设置；不改依赖、TFM或其他测试范围。

同一输入包只读。台账、生成视图、四状态页、任何共同合同/项目/依赖文件仍由COORD串行整合；不得为并行删掉原资源约束。S01-09自有既存校验和固定设计资产，不能引用尚未交付的S01-02候选代码；其声明35条输入中21条基础语义与14条路由/字节分别留证，不能只测后者后宣称全部采用。

业务仓实际基线分别为Main dfc043b1473dcea51d34ff87a7808692349c278a和Platform30bd23af6808d217a23878bd9437513043c52834。Platform根工作区旧候选不是主线。后续开工必须刷新远端，区分已核对文档增量与源码/合同漂移。PostgreSQL为后续唯一数据库目标，本轮纯合同代码不建库、迁库或部署。

## 通信与状态

继续使用[本轮Handoff入口](https://github.com/GTX537/CP6.AI-Handoff/blob/codex/dev-coordination-20261012/handoffs/CP6-DEV-20261012/README.md)。新资料分支和messages路径见每任务执行包。COORD在旧任务replies追加转换通知，并在新任务replies追加正式任务包定位；不会覆盖旧消息。具体状态/资源仍以最新roadmap为准，Handoff上传不代替主线包含性、ACK或实际START。

WP01-02本次先交付汇总/派工文档，经远端包含性核对后独立登记DONE；它不是三个代码任务的前置，不能让它们等整轮业务结束才开工。未运行测试不标PASS，无法恢复旧30/48仍保留。执行对话只提交本人代码与证据，四份Main状态页由COORD在交付窗口统一同步。
