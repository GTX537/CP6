# 第三轮开发派工

用户授权继续推进后，COORD核对第二轮真实交付与新任务输入，现分配四项独立合同实现。当前8/375任务DONE，本轮4项READY/CONFIRMED，尚无实际START；业务目标0/125，247运行场景NOT_RUN、41历史静态场景REFERENCE_ONLY保持原记录。

| 沿用对话 | 新任务 | 执行包 | 仓库 |
|---|---|---|---|
| A（原S01-02） | S01-03 授信精确金额 | [S01-03](S01-03.md) | Main |
| B（原S01-05） | S01-04 库存数量、金额及比例 | [S01-04](S01-04.md) | Main |
| C（原S01-09） | S01-06 ATP稳定标识 | [S01-06](S01-06.md) | Main |
| D（原WP14-01） | S01-08 CRM INQ/LEAD合同采用 | [S01-08](S01-08.md) | CRM |

四棵真实独立工作树、基线、输入pin、全部86条任务向量关联和文件窗口见[dispatch.json](dispatch.json)与[准入证据](../../evidence/wave3-dispatch-20261012/admission.json)。分配条数25+9+22+30；共有profile跨端重复使用不代表新增业务目标。各任务都基于已交付S01-02，不依赖其他未合并候选，CRM不依赖未发布的Platform包。

Main三个任务分别写FinCredit、FinInventory、PlanAtp独立目录及不同测试文件；StableId新增schema仅限单一文件，其余共同输入只读。现有叶子项目已自动发现源码，不改共享csproj或既有wave2资源。CRM独立仓预留原生合同目录与三个有限配置例外，不替换现有first-slice API；原生INQ是38定义、55修复引用、257总引用。机器台账、生成视图和四状态页均由COORD串行处理。

CRM交付前置已独立处理普通验证工作流仅manual及严格历史批准附注兼容；详见[实际主线回执](../../evidence/wave3-dispatch-20261012/crm-ci-prerequisite.json)。保留7个验证jobs、制品身份及未来正式门禁，不触发Actions。新任务仍须每次发布核对候选/main实际工作流。PostgreSQL为后续唯一数据库目标；本轮不建库迁库、发包或部署。

继续使用[Handoff协调入口](https://github.com/GTX537/CP6.AI-Handoff/blob/codex/dev-coordination-20261012/handoffs/CP6-DEV-20261012/README.md)。COORD合并本分配后，在新任务及旧任务replies追加正式包定位；消息只是传递任务，不能冒充对话已收到或已开工。恢复现有对话后读取入口并发START。WP05正式采纳仍PROPOSED，ITER/REL历史30/48缺口不改写。
