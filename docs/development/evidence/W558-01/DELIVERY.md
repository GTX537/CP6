# W558-01 · 现行 MRP 写入与角色撤权

2026-11-14 UTC。Main 基线 `b91125954e13f9f73c9d6438a03453ae6d396ce8`，受测源码 `0ccf8408cb0e36e154561311b67379260325160c`。隔离 PostgreSQL 18.6、真实 Main HTTP 和模拟租户、需求、计划订单。 [Issue #1042](https://github.com/GTX537/CP6/issues/1042)

原运算、确认、忽略和现行转单只在 HTTP 入口校验权限。修复前五个并发用例失败。运算等待净需求写入时撤权可先提交。撤权持根时四个入口均提前提交并返回成功。 [实测代码](../../../../CP6.Tests/Erp/Pgt017MrpPostgreSqlTests.cs)

四个入口复用 Main 共同事务根及事务内当前动作、租户和会话核对。运算的三次 `SaveChangesAsync` 留在同一外层事务，批号、旧建议与 Pegging 失效、新建议、净需求、Pegging、批次终态同时提交。 [HTTP 入口](../../../../CP6.WebApi/Controllers/Plan/MrpController.cs)

- 撤权先提交，四个请求返回 403，业务写集保持，重新授予各动作后原请求成功。运算先持根，在净需求 INSERT 处受控等待时撤权等同一根，旧建议仍可见。释放后完整结果先提交，之后旧授权请求不能再写。两序均核对 IAM 版本、租户授权代次、身份快照、优先 Outbox、审计、原回执。
- 第三次保存中 Pegging 触发 fixture 约束失败，响应 503。批号、原建议、Pegging、净需求及批次整笔回滚，撤除 fixture 故障后重试只提交一套完整结果，数量 `7.12345678` 保持八位。
- 候选 6 项 PostgreSQL、29 项组件通过，零跳过。另有四项通用 `PlanPubPermissionAttributeTests` 失败，在 W556 已核验的基线 WebApi 上用同一测试程序集复测，失败名称及消息完全一致，涉及 CodeGen、FileContent、NumberRule 的静态约定。保留两个 TRX。构建零错误，现有告警保留。
- 本地发布 310 文件，WebApi SHA-256 `0b2220b32fc0f20966d3b4281b8b5439a18e9626a941cba1fc3065880129b96c` 与受测程序集一致。隔离发布建库退出 0、存活 200、匿名租户管理和 MRP 401。原始日志及摘要在 `D:/CP6-test-artifacts/W558-01`。Actions 启动、重跑和取消均为零。

现行转单继续由已注册的桩返回参考号，本次验证实际计划订单状态写入。真实 PR/工单接收和正式 PLAN-MRP/PLAN-PO 发布按各自合同验收。正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。独立 Space Design、旧 OA、财务、采购、普通 Stock 数据库角色，以及两库恢复后重新开放及对账仍待核销。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646) · [跨库与剩余写者 #1012](https://github.com/GTX537/CP6/issues/1012)

Main 分支、工作区及席位处置见远端 PR 和 `main-finish.json`。
