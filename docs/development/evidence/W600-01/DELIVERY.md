# W600-01 财务科目、银行账户与税码写入的共同事务栅栏

2026-11-15 UTC。开发基线 `753feb85cb11f259cdbbbd2483db7a804992319c`，实际受测 `8976536757af208a337a8db244b99c2c12d74501`。隔离 PostgreSQL 18.6 与实际 Main Kestrel，模拟两个租户、科目、银行账户和税码。 [缺陷 #1112](https://github.com/GTX537/CP6/issues/1112) · [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)

| 路径前缀 | 本批实际业务写者 |
| --- | --- |
| `/api/fin/account` | `POST /`、`PUT /{id}`、`POST /{id}/deactivate`、`POST /import` |
| `/api/fin/ap/master` | `POST /bank-account`、`POST /tax-code` |

六条路由在请求 Core 的同一 PostgreSQL 事务持有 Main 当前权限栅栏，按原动作检查会话。新增科目、银行账户和税码归属认证租户。新选择的科目关系在业务提交内按本租户核对，保留顶级科目、原合法业务错误和税率精度。

- 12 个 HTTP 场景逐一见证业务先提交和暂停先提交。业务先提交的行保留，暂停先提交时返回 401 且没有新写集。科目导入额外实测角色撤权两种顺序。拒绝时保留原权威回执、快照和业务行。
- 用独立连接读取两个租户，分别实测三条新增请求的租户归属、三条科目关系拒绝、原错误码与顶级科目。旧代码基线 20 项复现、1 项原合同通过。
- 最终 **21/21 实际 PostgreSQL 场景及 30/30 相关回归**，零跳过。发布目录的 310 个文件有 SHA-256，三份运行程序集与实际受测版本一致，隔离宿主存活 200、匿名科目写入 401。原 TRX、版本和发布验证在 `D:/CP6-test-artifacts/W600-01`。Actions 的启动、重跑、取消与生产部署均为零。

从 [W597 的 69 条写声明清单](../W597-01/DELIVERY.md) 计，**32 条已接入共同栅栏，34 条可写入口待接入，2 条只读，1 条在写入前拒绝**。后台和跨域写者、Space CAD/Excel 与刊发、CRM 可信切换依各自证据收口。正式 PostgreSQL **26 PASS / 1 NOT_RUN**。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646)
