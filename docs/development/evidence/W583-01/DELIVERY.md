# W583-01 旧 OA 流程触发配置使用当前 IAM 事务

对应 [缺陷 #1082](https://github.com/GTX537/CP6/issues/1082) 和正式 PGT-017。基线 `69b374478569049029a96e6466dc2b3b0b0b69da`，受测源码 `54661b00ccff7c95bf47516eac478b585136ec65`。PostgreSQL 18.6 和实际 Main Kestrel，隔离模拟租户与登录会话。

四个配置入口复用既有 `CurrentWriteAuthority`，沿用 `oa-flow-admin:FlowTrigger.Edit`。在请求的共享 Core 事务中先取 Main IAM 根，再读取当前权限，按原服务保存配置及密钥摘要并持门至提交。密钥明文按原合同只在合法新增或重置的成功响应中出现。手动试发有独立占位、流程提交和失败回写，需要在其提交边界验收。

- 用独立连接在事务内暂停角色策略写入或配置表 DML，通过 `pg_blocking_pids` 核对真实等锁。基线 8 项均未建立期望等待关系。修复后撤权先提交时四条写入均为 403，原行、启用状态、配置、密钥摘要不变。配置先提交时保留原变更，之后同一会话再次请求为 403。每种顺序检查 IAM 的版本、代次及对应 Outbox、审计和回执。
- 本地目标 **8/8** 通过，零跳过。附带运行的 21 项旧验证有 2 项通过、19 项失败，其中 17 项 SQLite 夹具在建表时缺少 `Latin1_General_100_BIN2`，另 2 项为既有全局 OA 路由检查。以同源码及依赖重新编译修复前控制器，逐项结果相同，失败证据保留。整个最终运行是 10 通过、19 失败、零跳过。
- 发布的 WebApi 和 Core 程序集摘要等于受测副本。310 个文件 SHA-256，发布宿主在隔离 PostgreSQL 的存活 200、匿名创建 401。原始 TRX、基线与回归比较、发布摘要、冒烟和停止记录在 `D:/CP6-test-artifacts/W583-01`。Actions 启动、重跑及取消均为零，未部署。

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。手动试发、消息、事件与定时触发、财务、独立 Space Design 和其余写者清单，以及 CRM 缺失写集时的恢复与安全切换待验。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646) · [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)
