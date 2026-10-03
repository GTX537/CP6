# DB-COMPAT-01 WP3：运行时数据库能力

日期2026-10-03；主任务 [Issue #134](https://github.com/GTX537/CP6/issues/134) 保持Open。本阶段从已确认main `605246ca83cbb8e1a89d3ff7209ba9df65176e98` 建立独立分支 `codex/db-compat-wp3-20261003`，工作树 `D:\CP6\tmp\worktrees\db-compat-wp3-20261003`。本地实施、适用双库门禁、集中审查和测试库清理已完成，提交/PR/远端交付待核对；普通PostgreSQL API/worker门禁保留。

WP2已由 [PR #145](https://github.com/GTX537/CP6/pull/145) 正常合并，远端main完整树与已验证candidate45cf2ca一致。合并后实际同一API709710…740A的普通PG启动guard冒烟：拒绝运行、非零退出、未开HTTP；没有重新执行已通过的35/36原生门禁。[WP2记录](WP2-IMPLEMENTATION.md) 的交付Pending保留提交前的历史时点，当前父任务已勾选WP2。

本阶段按原 [计划](../../superpowers/plans/2026-10-02-database-compatibility.md) 连续处理锁与时钟、错误分类、ORD原子编号、同连接Context事务和generation/v2 cursor，再集中审查。完整身份/消息/库存业务、Space/CAD/容量/租约和恢复由后续阶段验收，通用能力通过不外推为消费者业务通过。

ORD保留按FuncCode的全局永不重置计数，跨租户共用原号码流；年月只影响显示前缀。已有非ORD Local批量/deferred保存约定保留。锁保留原资源大小写、租户/全局作用域、顺序、有限超时及释放生命周期；Session-owned backfill不能替换为每批Transaction-owned锁。数据库UTC读实际推进的时间，业务GETDATE/wall-clock字段不变。

本地locked restore exit0。两库专用owner数据库 `CP6Compat_WP3_20261003_92d070db` 实际安装SQL Core140/Space47与PG Core1/Space1，最后由真实API完成首次/重复DatabaseInit；所有门禁结束后已普通DROP并确认两库不存在，[清理原件](wp3-native/final/wp3-cleanup-final-owned-two.json)记录两库owner/无活动会话检查、原receipt字节与PG测试role保留。连接/密码只在忽略的本地所有权文件，不归档凭据。

实际执行来源原位于根工作区 `D:\CP6\tmp`，现以原字节归档；[manifest](wp3-native/manifest.json)逐一映射原路径、归档路径、SHA256与范围，重复内容保留来源别名。

| 范围 | 实际结果与来源 |
| --- | --- |
| 锁/UTC/分类单测 | `wp3-capabilities-first-red.log` / 对应TRX：74F、0P、0Skip；`wp3-capabilities-first-green.log`：75/75，新增typed applock deadlock用例解释计数增加。 |
| 共享Context单测 | `wp3-shared-context-first-red.log`：18F；`wp3-shared-context-first-green.log`：18/18，0Skip。 |
| ORD原始差异及修复 | `wp3-orders-immediate-before-adapter-red.json` PG原实现未立即保存；原SQL参考3/3。改后`wp3-orders-atomic-first-green.json`、`wp3-orders-atomic-sql-regression.json`各7/7；五行为项加owner/history两项。 |
| ORD补充 | `wp3-orders-extra-{sql,pg}-first-native.json`各5/5：已存在计数的真实双会话2/3、Modified/Deleted拒绝、非ORD同Local延迟保存。原五行为项源码保持，不重称新执行。 |
| 锁与推进时钟 | PG `wp3-locks-clock-first-native.json`5/5。SQL原3P2F及`wp3-locks-sql-cancellation-diagnostic.json`确认两种owner在取消时均SqlException0/原生取消消息/token已取消；单点归一化为同token OCE后，原D40验证代码复查`wp3-locks-clock-sql-cancellation-fixed.json`5/5。 |
| 原生约束分类与完整重试 | SQL最初English预检查失败，去掉预检查后真实中文PK名称匹配RED。依据本机sys.messages2052固定模板补齐中文解析。`wp3-constraints-and-transactions-sql-native.json`4/4，含中文/英文各PK2627、unique2601、FK/check547共8负案例与真实双会话1205。PG原约束3/3，以及`wp3-transactions-pg-native.json`4/4含23505/FK23503/check23514四负案例、真实40001；失败事务全部回滚，fresh Context完整重读/重写，非失败事务内重试单条语句。未知SQL语言保持未知名称，不能扩大为全部locale已支持。 |
| 共享物理事务 | `wp3-shared-context-{sql,pg}-native.json`各3/3：七个子Context的实际provider/profile/同连接事务写入，正常及失败child dispose保留parent，整事务回滚及fixture清理；完整业务SQL未被此项覆盖。 |
| 身份游标原生RED | `wp3-identity-cursor-sql-original-red.json`2P1F：现有rowversion边界不同于持久tenant generation；PG对应原件2P1F：旧TSQL42601。当前已改Reader v2 protector与generation LINQ读取，PG同页RepeatableRead/SQL保持Serializable；`wp3-cursor-sql-lifecycle.json`实际3/3、`wp3-cursor-pg-lifecycle-and-page-snapshot.json`4/4：各真实子进程续读和变更拒绝通过，PG真实generation查询后并发提交仍保持同页旧generation/版本，下页拒绝旧游标。 |
| 锁调用方与启动回归 | `wp3-startup-lock-caller-tests.log`30P/2Skip：筛选同时命中两个需显式SQL连接的原生用例；随后显式连接执行`wp3-startup-sql-native-tests.log`2P/0Skip，真实生产回填501行跨批次互斥、异常释放通过。用例自行建GUID数据库/EnsureCreated，事后该前缀数据库0，不作为migration验收。两次原始结果均保留，事后字节观察见`wp3-startup-sql-native-postrun-observation.json`。 |
| 容量领取原始行为 | `wp3-claim-pg-original-red.json`3P1F：真实Core1/Space1安装通过后原ledger在TSQL42601失败；`wp3-claim-sql-original-reference.json`4/4：真实Core140/Space47及原ledger首次领取通过。改后`wp3-claim-{pg,sql}-concurrency-first.json`各10/10：实际生产服务首次创建、双会话max1/max2、同run单例、到期/token/owner/run fence、租户独立和两秒预算skip-locked均通过。Renew/Release及budget源码保持，budget/full生成业务待WP5。 |
| 最终工具修正与定向复测 | 审查发现ORD测试超时后worker可能晚于fixture清理；现取消barrier并等待worker/Context结束后清理，生产DocNumber未改。`wp3-orders-{sql,pg}-final-review.json`再次各7/7。共享fixture参数化后的`wp3-shared-{sql,pg}-parameterized-final.json`各3/3。强制超时路径仅源码复核，未冒称故障注入通过。 |
| 四个分类调用方回归 | [Core/ERP实际TRX](wp3-native/final/wp3-core-classifier-caller-tests.trx)54/54、[Space实际TRX](wp3-native/final/wp3-space-classifier-caller-tests.trx)22/22，均0Skip；包含14个新增SaveChanges异常注入用例，验证原业务错误及非unique透传，不作为真库完整业务验收。Space测试lock原NU1004保留，仅补已固定Npgsql8.0.8/EF8.0.11传递要求后locked restore通过，根工作区其他人的lock未改。 |
| 最终API初始化冒烟 | `wp3-init-{sql,pg}-{first,repeat}.json`四份实际报告均exit0、completion marker成功、无HTTP。独立端口SQL57883/PG57882；仅DatabaseInit，后台服务及未验业务feature关闭。输出DLL前后hash一致，原始stdout/stderr只留本机，归档安全摘要及日志hash；不扩大为再次全目录/种子数据一致性或普通API业务验收。 |

所有原生probe JSON伴随同名log/input，包含实际加载二进制路径/hash、HostUtc和命名源码观察；它们不代替完整compiler input manifest或完整业务验收。首轮两次带共享fixture插值的probe build各有1条EF1002，随后将fixture rowId改为EF参数，`wp3-runtime-probe-claim-red-build.log`实际0warning/0error；原warning记录保持原样。

16处锁调用已转统一能力（13 Space、ERP helper、seed及跨批次Session backfill），资源名/等待值及域错误的定向表达式比较通过，实际集成构建及上述启动回归通过。Validation/Republish原SQL THROW51000/51021改为同SPACE原因码的InvalidOperationException。六处Space时钟采用数据库UTC，测试provider原clock fallback保留。完整容量、租约、身份写入、消息以及PG Serializable竞争后的业务事务重试归属明确，未以primitive成功外推为消费者业务成功。

一次任务级集中审查分为Core/共享事务/游标与Space/启动/claim两部分，原件为`wp3-core-task-review.json`、`wp3-space-task-review.json`。唯一P2工具生命周期问题按`wp3-core-review-resolution.json`定向复核解决；原生成功结果保持原范围。root另审四predicate、四测试及锁文件差异，实际50个审查输入字节比较只有已解决的ORD工具变化，见[最终调用方观察](wp3-native/final/wp3-final-caller-review-and-test-observation.json)。未逐个小改动重开全仓审查。最终probe与API构建均0warning/0error。

[调用方矩阵](WP3-CALLERS.md)与[Provider分支逐文件登记](WP3-PROVIDER-BRANCHES.md)明确WP4/5/6责任；后者扫描13个生产目录的1571个C#文件，登记指定Provider命中的34文件，并补枚举路由至46文件/79定位行。未把命中数称为测试数。WP1/WP2已交付的token模型/触发器证据按原范围复用，本阶段真实claim fence也验证实际生产token变化。

归档151个原始路径、147份唯一原件，逐一hash/bytes相同；manifest SHA256为`873EEFC919057995C9A3BA0E58F26A6BB2BB38C195793AFAA46B71C706EBD8F5`。首轮RED stub仅有原hash观察，没有保留独立stub源码；各次原始失败、2Skip及后续2Pass均保持来源。`.gitattributes`仅新增WP3证据路径的字节保留规则。收尾9份Markdown/409个本地链接检查0错误，diff检查通过；文档补记复用该结果，不重复业务测试。当前LocalVerified/RemotePending，没有触发Actions、改变保护、修改既有业务库或部署生产。
