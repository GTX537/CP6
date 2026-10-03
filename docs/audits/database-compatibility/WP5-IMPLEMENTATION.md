# DB-COMPAT-01 WP5：Space、发布与报表

日期：2026-10-03。依据[已接受计划](../../superpowers/plans/2026-10-02-database-compatibility.md)，分支`codex/db-compat-wp5-20261003`、工作树`D:\CP6\tmp\worktrees\db-compat-wp5-20261003`从已确认远端main `c6c662f5b744b44a51427e26fb2faff96472fcc2`建立。父[Issue134](https://github.com/GTX537/CP6/issues/134)仍Open；WP5已交付并勾选，WP6继续执行。

当前状态为 **Delivered**：[PR152](https://github.com/GTX537/CP6/pull/152)已正常合入远端main `cbbb7fc8e99290f6aba7589830a98a98726280e9`，包含候选`c9141a558a46586d8343cd1e5b69ce710a468f31`且完整树相同。合并后实际全字段克隆及CAD/Excel同键并发SQL/PG各3/3、零跳过；13个WP5临时库已普通DROP并核对不存在，未强制断连，原receipt及PG角色保留。[22份交付原件](wp5-native/post-merge/manifest.json)包含首次推送前网络失败、正常交付、冒烟、清理和父任务阶段更新，不将这些结果改称新全量矩阵。WP6整体验收及父任务尚未完成，各批真实结果、复用来源及限制如下。

## 已交付前置

[WP4 PR149](https://github.com/GTX537/CP6/pull/149)的候选`cfe479124fb04d770462b874bc2a70508a8bf79e`已被主线包含，候选/合并完整树均为`60114519e08ddd33ee9be122a616d00ae5ce85c3`。合并后SQL/PG原业务快照保存分别2/2、零skip，均包含owner/实际迁移setup；六个WP4自有临时库已普通DROP并确认不存在，receipt和PG角色保留。Actions启动/取消均0，无工作流/保护变更。[交付、冒烟和清理原件](wp4-native/post-merge/manifest.json)与[WP4实施记录](WP4-IMPLEMENTATION.md)说明复用来源及限制；不重复原完整矩阵。

## 当前实施范围

| 入口 | 独占环境与当前状态 |
| --- | --- |
| Space | `CP6Compat_WP5_20261003_cb5b2306`两库实际安装Core/Space profile；克隆原生场景按下表分批通过，容量/文件13两库各通过；AI应用修复后两库各13通过；设计/发布35按首批与定向修复结果分批通过，CAD与克隆新增并发已定向两库各5/5；mapping并发另见BUG150 |
| MES报表、Dashboard、GDPR及Core Space | `CP6Compat_WP5_20261003_87e10506`专用两库实际安装Core/IdentityPriority profile；空报表4、带数据报表4、GDPR清理1与回滚1、Core Space原5各两库通过 |

创建来源为公共[工作树证明](wp5-native/initial/reports/wp5-worktree-created.json)、[Space库证明](wp5-native/initial/reports/wp5-databases-created.json)及[报表库证明](wp5-native/initial/reports/wp5-reports-databases-created.json)；创建证明不代替实际迁移或业务验收。连接与所有权receipt仅本机未跟踪文件，未复制入文档。root统一安排本地构建与数据库执行，必需场景选择Provider后不能Skip。

Space租约、克隆故障/并发、发布恢复及CAD已按下列原批次和定向复测完成本地验证；一次任务级集中审查的两项P2已修复并增量复查，WP5远端交付已完成。WP5交付版本仍保留普通PG API/worker guard；WP6负责开放与验证完整应用启动、初始化、备份恢复及运行配置，没有现有数据搬迁、环境替换或生产部署。

## 首轮真实结果与限定范围

下列已完成批次的219份公共原件已按[显式清单](wp5-native/initial/allowlist.json)归档，含执行前源码/程序集及owner receipt哈希、原始日志/TRX、构建和准备失败、runner/creator脚本及版本快照。[不可变manifest](wp5-native/initial/manifest.json)记录原路径、SHA-256和字节数；[归档核对](wp5-native/initial/verification.json)为219/219，58份报告的日志哈希及TRX计数一致，已记录runner哈希均有对应原件。归档前逐份对照本机凭据值并检查JSON私密字段，连接、密码、receipt及private diagnostics未归档；根工作区保留原件。归档只是证据核对，不计新测试或WP5交付。必需选择均零skip；失败保留，后续修复不改写原结果。Space fixture核对完整有序迁移与pending=0，SQL Core140/Space47、PG Core1/Space1；报表fixture的SQL IdentityPriority归Core，PG独立迁移1。

| 原件label | 真实结果 |
| --- | --- |
| `wp5-clone-sql-first-reference` / `wp5-clone-pg-first-run` | 最初空白草稿与空版本克隆：SQL2/2；PG1/2，clone-snapshot原生42601，经生产processor包装为领域失败 |
| `wp5-clone-pg-business-first-run` | PG完整克隆分支后原13业务11通过/2失败；十二类行及source/calibration/element重映射原用例已通过。两失败分别为夹具100ns时间与PG微秒存储差异、共享库固定全局资产code在seed时23505冲突 |
| `wp5-clone-pg-fixture-followup` | 测试时钟统一到微秒、每个测试资产使用独立code，严格业务断言保留；原失败2项与PG实际catalog专项另3/3。此前11项按不变的生产行为与原数据来源复用，不称单次PG14/14 |
| `wp5-clone-sql-native-matrix` | 原SQL14/14，包含完整克隆、历史规划、幂等、楼层竞争和真实目录；不含该类3项InMemory测试 |
| `wp5-reports-sql-first-reference` / `wp5-reports-pg-first-run` | 空报表SQL4/4；PG0/4，三个原usp为42883，Dashboard实际第一条KPI查询42P01 |
| `wp5-gdpr-sql-first-reference` / `wp5-gdpr-pg-first-run` | 原清理事务SQL1/1；PG联表DELETE处42601，实际Core/priority迁移已通过 |
| `wp5-reports-{pg,sql}-provider` / `wp5-gdpr-{pg,sql}-provider` | 显式PG查询及DELETE USING适配后空报表各4/4、GDPR各1/1；目标租户清理、对照租户保持，5个tombstone＋1个撤销及priority6保留 |
| `wp5-core-space-{sql-first-reference,pg-first-run}` | 原Core Space5项两库各5/5；选中模式使用真实Core迁移、独立case租户，旧SQL EnsureCreated入口仍另保留。覆盖analytics翻译、非空唯一/多个NULL、换码及陈旧token写拒绝 |
| `wp5-capacity-file-{sql-first-reference,pg-first-run}` | 原容量3＋文件10：SQL13/13；PG4/13，2个预算和7个文件场景真实命中旧方括号/锁提示42601；原失败记录保留 |
| `wp5-capacity-file-{pg,sql}-provider` | 显式PG行锁、缺失预算项的tenant事务锁及有界完整事务重试后，两库各13/13、零skip；现有source引用/过期竞争也通过，不据假设扩改CreateSource |
| `wp5-ai-atomic-{sql-first-reference,pg-first-run}` | 原AI应用13：SQL13/13；PG7/13，五项应用/提交/恢复在旧锁SQL处42601，一项同key真实竞争在Queue保存处40001；原失败记录保留 |
| `wp5-reports-parity-{sql-first-reference,pg-first-run}` | 新增真实数据4项两库各4/4；两tenant全局统计、状态/软删除原口径、日期半开边界、小数1.00/1.01、同code异name分组、TOP8与实际缓存命中/移除、days边界与368失败类别 |
| `wp5-gdpr-rollback-{sql-first-reference,pg-first-run}` | 新增原生CHECK晚期失败回滚各1/1；tenant/user/dept DELETE已经完成后失败，fresh context核对双方身份/授权/快照/队列/审计保持，精确移除测试约束后再次业务清理成功 |
| `wp5-ai-atomic-{pg,sql}-provider` | 显式PG Run/Version/Model行锁与Queue/Recovery自有事务有界重试后，两库原13各13/13；含同key并发、Save后Commit前故障及实际恢复。原SQL查询保持，未对整个worker或外部步骤自动重试 |
| `wp5-jobs-generation-retention-{sql-first-reference,pg-first-run}` | 原Job9/Processor3/Generation3/Retention2共17：两库各16通过/1失败；失败为原忽略过滤器的整库计数，改为本case两个tenant且保留精确2条，后续与base组定向通过 |
| `wp5-design-publish-wms-{sql-first-reference,pg-first-run}` | 原35：SQL34通过/1失败；PG31通过/4失败。共同失败为生产跨tenant恢复指标的空库假设，PG另有固定system asset code种子冲突与两个编辑租约实际40001；原批次均零skip |
| `wp5-design-publish-lease-{pg,sql}-followup` | 只复测受影响8项，两库各8/8。metrics保留原全局scope，严格基线增量；asset code按case隔离；租约PG完整自有Serializable命令重试typed40001/40P01。其余原31/34结果按未影响路径复用，不称单次35/35 |
| `wp5-base-space-job-{sql-first-reference,pg-first-run}` | 原base业务13＋上述Job失败1：SQL14/14、PG13/14；Job修复两库通过，PG唯一失败为删除被引用File原生23001尚未分类；schema-only另库专项未混入 |
| `wp5-restrict-file-{pg,sql}-classifier` / `wp5-failure-classifier-unit` | restrict外键失败补入分类后，两库文件删除阻断各1/1，核对精确FK及两行仍存在；分类合同42/42，23001不可重试。原23503映射保持 |
| `wp5-cad-assets-collaboration-{sql-first-reference,pg-first-run}` | CAD/Excel/资产/外部协作原17：SQL17/17、PG16/17；PG仅CAD Retry同key竞争实际40001，适配后待定向复测。mapping原两个事实只覆盖顺序重放，新并发专项另准备 |
| `wp5-clone-edges-{sql-first-reference,pg-first-run}` | 新两个专项：SQL2/2；PG1/2。末表真实CHECK失败→十二表全部回滚、源fingerprint不变、同PID临时表0、正常调度重试成功，两库通过；同operation双连接barrier竞争PG实际40001，修复后待复测 |

PG克隆使用三张每次调用独立命名的事务临时映射表，十二段复制保持原SQL列、顺序、tenant/soft-delete及logical引用；业务值参数化。仅在调用者现有事务中执行，成功后清理map，失败由原caller回滚，不单独提交或重试。SQL原批次保持。

MES与首页继续原全局汇总口径、缓存和授权入口；不以带EF租户过滤的实现替换现有Dapper后声称等价。GDPR才按目标tenant显式删除，保留对照tenant与最小身份事实。首批四个空报表仅证明原入口可执行；后续新增四个真实数据场景分别验证金额、排序、日期、缓存与原全局作用域。

`wp5-mes-sql-boundary-reference-v2.json`实际调用原SQL趋势SP：days=-1/0各一行未来日期，1/366/367分别相同行数，368原生530。原小数表达式元数据为除法decimal(38,17)、乘100后decimal(38,13)；两组边界最终1.00/1.01。PG按实测类型收缩保持舍入，后续带数据原生报表已验证这两组边界。首版sqlcmd参数冲突在执行前失败，原脚本/日志与准备失败报告保留，不计业务RED。

初始Space构建0warning/error；CP6.Tests保留原WP4 Permissions测试的xUnit2017一项警告。克隆/相关Space夹具局部构建使用已验证Core/PG迁移程序集，`BuildProjectReferences=false`及各次二进制来源据实记录；后续正常CP6.Tests构建已包含三处报表/GDPR生产适配，不把局部构建外推为整仓验证。

23001为PostgreSQL定义的restrict_violation，分类按真实FK删除失败及[官方错误码表](https://www.postgresql.org/docs/18/errcodes-appendix.html)核对；不扩大唯一约束或事务重试集合。本机实际错误和精确constraint记录优先于版本推测。

最新两次Space接线构建分别记录条件插值推断为string的两处编译失败，修正显式FormattableString后各v2构建0warning/error，保留首日志。42项分类测试对应CP6.Tests正常构建仍有原WP4 xUnit2017警告。各批只对其明确filter和加载程序集负责；运行时新接线文件如尚未编译，不纳入该批通过范围。

## 本轮迁移与并发定向进展

`wp5-clone-cad-retry-{pg,sql}-followup`两库各5/5、零skip：同operation双物理连接、原幂等/空白/历史规划与CAD同key重试；对应克隆Start与CAD Retry修复，不重跑或扩大十二表复制旧通过范围。

`wp5-space-only-idempotency-sql-first-reference`2/2；`wp5-asset-legacy-failure-sql-first-reference`、`wp5-ai-retention-script-sql-first-reference`、`wp5-publish-recovery-failure-sql-first-reference`及`wp5-publish-recovery-script-sql-first-reference`各1/1。五个独立SQL临时库实际覆盖Space-only目录、历史数据拒绝、两次幂等脚本和活动发布拒绝。PG `wp5-space-only-pg-first-run`的原0/1、42704失败保留：冻结Space baseline所用共享collation/function此前只在Core建立。统一前置路径已补齐可重复执行与旧定义校验，保持已交付baseline和迁移ID。

PG前置专项[Space→Core](wp5-native/initial/reports/wp5-prerequisite-space-core-pg-first-run.json)、[Core→Space](wp5-native/initial/reports/wp5-prerequisite-core-space-pg-first-run.json)、[生成脚本连续执行两次](wp5-native/initial/reports/wp5-prerequisite-space-script-pg-first-run.json)及[Space-only复测](wp5-native/initial/reports/wp5-space-only-pg-prerequisite-followup.json)各1/1、零skip。SQL生成合同原8项[首次8/8](wp5-native/initial/reports/wp5-prerequisite-sql-generation.json)，P2断言修正后[相同8项定向8/8](wp5-native/initial/reports/wp5-prerequisite-sql-generation-review-followup.json)，不相加成16个不同案例。[前置专项审查](wp5-native/initial/reports/wp5-prerequisite-targeted-review.json)的P2已resolved，仅覆盖前置runtime/script、定义校验、profile顺序及该断言修正；不是完整WP5任务审查或交付结论。

`wp5-mapping-concurrency-sql-first-reference`和`wp5-mapping-concurrency-pg-first-run`各0/2，均有两真实会话及first-save barrier。SQL实际deadlock，PG CurrentName唯一键23505；发现时两个映射生产服务为已交付主线原码，因此按BUG规则单独登记[BUG150](https://github.com/GTX537/CP6/issues/150)，从c6c662f5建立修复工作树及独占双库。该依赖现已独立交付并纳入WP5，见下节；原失败结果全部保留。

## 任务审查与定向修复

[Space初始审查](wp5-native/review-followup/reviews/wp5-space-task-review-initial.json)与[Core/报表/迁移/夹具初始审查](wp5-native/review-followup/reviews/wp5-core-task-review-initial.json)共同构成本次任务级审查。两项P2分别为：AI完整命令重试可能清空调用方待保存实体；选择WP5 Space provider会错误启用仍只支持旧SQL入口的备份恢复事实。

AI修复将待保存变化和EF当前、ambient、enlisted调用方事务排除在新增完整命令重试之外，保持原单次操作并向上抛出原生故障，不清空调用方tracker。首RED为0/1；首次修复批次虽命名`green`，实际仍为0/1，原因是断言错误要求EF外层异常为`DbException`，实际为`InvalidOperationException`。后续改为准确核对`GetBaseException()`的原生`PostgresException`，保留Added状态和原生40001断言，再完成15/15。原失败批次未改写。

备份恢复事实改用专门的legacy attribute；选择任一WP5 Space lane时不启用旧SQL生命周期，即使同时配置`CP6_TEST_SQLSERVER`。未选择WP5时保留原入口。两个selector回归只核对选择行为，未执行BACKUP/RESTORE，不能计作WP6恢复验收。

| 新执行label | 真实结果与用例范围 |
| --- | --- |
| `wp5-legacy-recovery-selector-red` / `wp5-legacy-recovery-selector-green` | 同两个selector事实先0/2、修复后2/2，零skip |
| `wp5-ai-caller-pending-pg-red` / `wp5-ai-caller-pending-pg-green` | 同一个真实PG竞争事实两次均0/1；后者为EF外层wrapper类型断言失败，不能称GREEN |
| `wp5-ai-cad-review-followup-pg` | 15/15、零skip：原AI13＋新调用方待保存变化1＋原CAD竞争1；这是本次定向新执行，和之前AI/CAD批次有重叠 |
| `wp5-mapping-bug150-pg-integration` / `wp5-mapping-bug150-sql-integration` | 纳入已交付BUG150后两库各4/4、零skip：原CAD/Excel顺序事实2＋新增同key并发2；两库执行相同四个事实 |

[增量复查](wp5-native/review-followup/reviews/wp5-task-review-targeted-followup.json)确认两项P2均Resolved，无新实质问题；未变化的初始审查范围复用，未启动第二次完整审查。各复测批次也不相加成新的不同用例总数，之前成功批次按其实际源码、程序集和未受影响路径复用，未改称最终构建的新执行。

本轮新增公共原件为[31份显式allowlist](wp5-native/review-followup/allowlist.json)、[独立manifest](wp5-native/review-followup/manifest.json)及[逐字节核对](wp5-native/review-followup/verification.json)，原件共697,644字节，归档核对31/31；7份报告、process.log和TRX一一对应，runner字节均引用初始归档的已核对版本。包括原RED、失败的`green`、最终复测、4份构建日志、3份审查和3份依赖检查；initial219及BUG150交付后15份原件保持不可变。归档核对数量不计测试用例。

## BUG150交付与WP5整合

[PR151](https://github.com/GTX537/CP6/pull/151)已正常交付，远端main为`522433a370c0c247a98b300c69e88e403217d53a`；Issue150于`2026-10-03T14:47:22Z`关闭。BUG独立双库RED/GREEN、原12项单元回归和合并后两库各2/2冒烟的范围见[BUG150审计记录](../2026-10-03-bug-150-mapping-idempotency/README.md)。两自有库已普通DROP并核对不存在，未终止会话；[交付后15份公共原件清单](../2026-10-03-bug-150-mapping-idempotency/native/post-merge/manifest.json)独立于原已交付29份清单。

WP5以`97e4a9f2e0881c4f423111848632bbc821cde11d`整合该main；三处映射生产文件与已交付BUG150字节一致，生产无合并冲突。[整合证明](wp5-native/review-followup/dependency/wp5-bug150-dependency-integrated.json)和[完整Space测试项目构建日志](wp5-native/review-followup/builds/wp5-bug150-integration-build.log)记录0警告、0错误，随后SQL/PG映射各4/4。原不相关WP5成功结果保留原执行输入和来源，不因整合提交或程序集哈希改变而冒称已重新执行。该整合时点为LocalVerifiedRemotePending；随后PR152完成远端交付，见本页顶部及交付原件。

收尾[结果核算](wp5-native/final-result-accounting.json)按Provider、测试类和案例名称关联65份原TRX，所有已记录案例的最后一次记录均为Passed；它是历史记录核算，不是又执行一轮完整矩阵。[最终源码与复用核对](wp5-native/final-source-reuse-review.json)为相对已交付main的15份生产文件绑定相关成功批次，核对当前12个Space程序集与映射整合批次一致，并保留旧runner未直接记录classifier源码时的同程序集交叉证明。准备阶段两次绑定校正、未重新运行的范围及WP6边界均明确记录。
