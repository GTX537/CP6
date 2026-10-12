# S02-00 独立集中设计审查

审查日期：2026-10-12。候选：`e8303d3dd75e9607f4705a1cbcd6e181d63d0388`。指定主线：`8570384f1e28065afee4ce416c9f492dde734131`。审查工作树：`D:/CP6-worktrees/s02-00-20261012`。

结论：**With fixes（有 1 项 P2 设计规则需修正后合入）**。这是静态设计审查，不是业务验收、数据库隔离实测、生产批准或已交付状态确认。候选全部 27 个运行场景仍应保持 NOT_RUN。

## 审查范围与优点

- 读取 D:/CP6/AGENTS.md 与 requesting-code-review 技能/审查模板；核 S02-00 任务卡、PostgreSQL 六项设计要求及原设计版本规则。
- 审查候选自身完整 9 文件增量。候选与指定最新 main 的共同祖先为 `92fb94db40c54be90f2fbb18b0922d264c060796`；两端直接 diff 出现的 S01 删除是 main 后续进展产生的比较差异，不是候选删除。实际候选没有业务代码、公共模型、迁移、工作流或台账改动。
- 完整阅读设计及 27 场景；审查 README、自审报告、verify.py、来源清单、验证报告的范围/当前文本摘要和工作流预检证据。来源语义复核定向覆盖 SYS-TENANT、SYS-IAM、PLT-CTX、PLT-DEL 相关身份、原子性、恢复及锁序段落，不把摘要核验算成所有原件全文审查。
- 定向核对固定 SHA 的 Main CP6Context/ERP 消息映射、Platform Inbox 自建事务/消息唯一索引、CRM Runtime 映射/BusinessOutbox SqlTransaction 与 sp_getapplock 依赖；既有源码到指定最新 main 的变化限于规划/交付资料，没有本轮源码证据漂移。
- 方案明确保留 TenantId/权限/Owner/精度；全局键对象默认留共享域；消息 queueDomain 原唯一域不缩水；角色、共享 RLS、池、模型缓存和同 DbConnection/DbTransaction 的边界明确。迁移历史按 lineage 隔离，恢复包含共享切片和当前撤权/已发送事实对账；没有拿 schema dump 或静态 PASS 冒充恢复通过。
- session_user 与 SECURITY DEFINER 边界符合 [PostgreSQL 会话身份语义](https://www.postgresql.org/docs/18/functions-info.html)；helper 的安全 search_path/pg_temp/PUBLIC EXECUTE 处理符合 [CREATE FUNCTION](https://www.postgresql.org/docs/18/sql-createfunction.html)。EF 对 schema-per-tenant 的支持限制、自管模型键、共享连接事务的要求与 [EF 多租户](https://learn.microsoft.com/en-us/ef/core/miscellaneous/multitenancy)、[动态模型](https://learn.microsoft.com/en-us/ef/core/modeling/dynamic-model)、[事务](https://learn.microsoft.com/en-us/ef/core/saving/transactions)一致。

## 必须修正：R1（P2）补齐 UPDATE 的旧行 USING 规则

位置：`docs/design-handbook/architecture/02-postgresql-tenancy.md:88`；关联 `docs/development/acceptance/postgresql-tenancy.json:217`、`:219`（PGT-008）。

第 88 行只规定 SELECT/DELETE 的 USING 与 INSERT/UPDATE 的 WITH CHECK，遗漏 UPDATE 对旧行的 USING。WITH CHECK 只检查更新后的新行，不能提供旧行可更新范围；若按文字建立四类独立策略，UPDATE 仅有 WITH CHECK、没有其他 UPDATE/ALL 旧行许可，合法 A 行更新也不可进行。不能靠 SELECT 策略补齐：不读取旧列、没有 WHERE/RETURNING 的常量 UPDATE 不要求这类 SELECT 检查。官方 [CREATE POLICY 的 UPDATE 规则及 Table 300](https://www.postgresql.org/docs/18/sql-createpolicy.html)区分这两道门；[官方 rowsecurity.c](https://doxygen.postgresql.org/rowsecurity_8c_source.html)的 add_security_quals 在没有 permissive qual 时加入拒绝条件。

该缺口属于设计可实现性问题，不是已发现的当前生产越权。初审沟通中曾推断“缺 USING 自动允许全部旧行”，进一步官方源码核对后已撤回；缺省并不等于 USING(true)。但实现者若为了恢复正常更新而补 USING(true)，无旧行读取的 TenantId 常量重写就可能把 B 行改属 A，单测 A→B 新行拒绝不足以发现。

建议：明文要求 UPDATE 同时有基于 session_user 可信绑定的旧行 USING 和新行 WITH CHECK；记录所有适用 UPDATE/ALL、PERMISSIVE/RESTRICTIVE 策略组合，防止其他宽策略用 OR 扩大范围。PGT-008 增加：A 合法常量更新只影响 A；没有 WHERE/RETURNING 的 TenantId=A 常量赋值不能把 B 吸入 A；管理员只作外部观测，原 B 行/内容/归属保持不变。若 TenantId 列不可更新，保列权限拒绝证据，并保普通可写列的无旧列读取更新正例。无需创建数据库或运行业务来修正文档。

## 次要建议（不单独阻止合入）

M1：设计 `02-postgresql-tenancy.md:123` 的 CRM 锁序从 ReconciliationState 开始，原 PLT-CTX 正文第 108 行在其前还有 Registration 登记保护门。本文其他段落已经继承原合同及当前授权门，未据此认定合同已被删除；建议顺手把 Registration 明写到序列前，并在 PGT-014 留登记保护证据，以免实施者只复制这一条锁序。

## 实际验证及未验证范围

独立只读检查结果：31 个固定 Git blob 的 SHA-256/字节数一致；12 条原设计/组合/接受来源的 SHA-256/字节数一致；validation-delivery 中 6 个当前作者文件文本摘要一致；13 个本地链接存在；PGT-001 至 PGT-027 唯一完整且全部 NOT_RUN、execution_evidence 为空。候选完整增量 `git -c core.longpaths=true diff --check` 通过。最终只读 git status 干净。

未执行 verify.py 的写文件入口；独立使用只读脚本复核上述内容，未修改任何仓库文件。没有数据库连接/操作、构建、业务测试、包恢复、迁移、备份恢复演练、Actions、部署或远端设置操作。已有 workflow-preflight 仅按原时间证据阅读，本次未新查询远端运行队列。合入前后的台账同步、保护合并和远端 main 包含性由协调交付完成。

修正后仅需定向复查 R1 的设计文字和 PGT-008，以及相应文档摘要/链接；不要求重启完整审查或业务测试。
