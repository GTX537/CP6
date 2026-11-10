# W441 · BUG-883-SERIAL-LIFECYCLE-FENCE

2026-11-10 UTC。[Issue #883](https://github.com/GTX537/CP6/issues/883)，原 WP02-03 / PGT-017 的现存序列写者。COORD 单人复用 DEV-2，分支 `codex/bug-883-serial-lifecycle-fence`，基线 `cca06558efeea692895c2c757f6c5ff0b796b3d9`。范围是已有 enable-tracking 与 RECEIVE/PUTAWAY/MOVE/PICK/SHIP/COUNT/RETURN 的最终授权、库存事务和错误原子性。

## 实际故障与修复

原序列服务自己开始事务，再调用公开的独立 Stock Owner。五种有库存效果的请求实际返回 400 AUTHORITY_FENCE_UNAVAILABLE；独立 Owner 正确拒绝已有事务。RECEIVE 在失败前已经跟踪两个新序列及两条逐序列流水，原 catch 回滚却保留跟踪对象，随后 OperLogFilter 在同一 Context 保存操作日志时又把它们刷入数据库。原 HTTP 返回错误后实际留下四条业务记录，库存和回执均没有对应结果。

转换、PICK、COUNT 没有数量效果，不经过独立 Stock Owner。原版在真实租户暂停先提交后仍返回 200 并写入产品/序列/流水/回执；回放也在自有事务和当前 Owner 之前读取。

两个入口现复用既有 OperationsOwnerV1.Mobile:serial-manage，使用实际 actor 和同一 PostgreSQL 共同认证根，在根内读取回执、当前权限、实际库存桶与各序列源/目的库区，最终再检查所用资格。既有 Owner 已围住库存、产品、任务、回执等表，本域补充两张序列表的原生表围栏，覆盖直接 SQL 写者竞争。无需新增 EF 模型、迁移或新的权威定义。

有库存效果时核对具体 StockMovementService 与父 Context 一致，通过已有程序集内部 ApplyWithinOwnerAsync / MoveWithinOwnerAsync 复用库存原语、元数据与实际部门继承。保留 Stock adjust/move 动作、库存对象与库区资格，Apply 保留价格写权要求；普通新库存使用保留当前库位封锁准入。序列、产品 tracking 锁、库存腿、流水、继承映射/审计及原回执在同一父事务提交。外部 bridge/notifier 仅在父 Owner 确认提交后按既有协议调用，回放无新流水，不再次调用。外部系统送达未作本次业务验收。

转换与 PICK/COUNT 不产生 Stock 数量流水，不新增无关价格写要求。MOVE 不改价格，保留原可在价格只读时移动的合同。非 PostgreSQL 历史路径保持其原范围，未新增 SQL Server 开发或验收。Owner 的 finally 移除本次业务跟踪对象，确保回滚后日志保存不能再提交失败的序列或库存。原 400/409 业务错误和回执 JSON 保留；新 Owner 的 401/403/503 及提交未知不再被 Controller 错改为普通 400。

## 实际验证

明确模拟租户、普通角色、产品、两个库位、序列、库存及真实部门映射。实际 Main Kestrel HTTP、PostgreSQL 18.6 隔离实例，最终 9 项执行、9 通过、0 跳过，9 个实际宿主。八个参数场景逐一覆盖转换及七种操作，第九项在同一组序列上连续执行完整七步生命周期。

| 场景 | 原版实测 | 最终实测 |
| --- | --- | --- |
| RECEIVE / PUTAWAY / MOVE / SHIP / RETURN 正常请求 | 均 400；RECEIVE 残留序列与流水 | 均 200，真实 Stock 结果与序列/回执共同提交 |
| 转换 / PICK / COUNT 的暂停先提交 | 200、原写集改变 | 401、实际等待共同根、完整原行保持 |
| 其余五种操作的暂停先提交 | 原版正常路径已失败，未冒充此阶段执行 | 均 401、实际等待共同根、完整原行保持 |
| 同一序列完整七步周期 | 首个 RECEIVE 即失败 | 7 条真实库存流水、14 条逐序列流水、7 个原回执，逐步数量对账通过 |

每个参数场景先成功处理一组产品，再以另一组产品控制在途请求。实际原生会话锁定目标租户行、观测暂停已持共同根并等待该行；独立会话持产品表锁，观测真实请求 backend/blocker 后先提交暂停、再释放业务锁。八条修复后路径均在原生共同根等待，返回 401 REAUTH_REQUIRED；暂停后旧回放也被拒绝。合法 reactivate 并重新登录后，同一已知拒绝操作键可以成功执行，原成功键仍恢复完全相同 JSON。

完整 JSONB 原行比较十类实际写集：产品、序列、逐序列流水、Stock、StockTransaction、回执、WMS 编号行、movement_sequences、库存流水部门映射和继承审计。模拟数据通过现有实际部门映射管理入口绑定源/目的库存，最终核实每条 Stock 流水继承其对应库存的真实部门和当前操作者。MOVE/PUTAWAY 两腿、IN/OUT 单腿及无数量效果三条路径，按实际返回 StockTxnNos 与数据库行核对。

范围验证包括失去全部库区时新请求/旧回放拒绝，MOVE/PUTAWAY/RETURN 仅有源或仅有目的资格都拒绝。封锁目的库位拒绝 MOVE，缺 Stock 动作拒绝实际库存路径，价格只读拒绝 RECEIVE；转换/PICK/COUNT/MOVE 在价格只读时仍合法。转换拒绝非整量库存和已经锁定的 tracking，新产品零库存转换不新造库存；重复 RECEIVE 保持原 409。回放不改变上述原行。

在转换和两腿 MOVE 的最后回执插入处注入实际 PostgreSQL 异常，故障发生在前面业务 SaveChanges 之后，返回 503 AUTHORITY_FENCE_UNAVAILABLE，十类原行保持。原生日志确认故障触达；测试 finally 准确删除本次触发器/函数，后续操作以原键继续成功。完整周期为 RECEIVE→PUTAWAY→PICK→MOVE→COUNT→SHIP→RETURN，每步逐库位核对 non-shipped serial 数与真实 physical 数量，最终退回两件至 DST；最后回放仍恢复原 JSON，无新效果。

## 证据、失败记录与审查

原始证据 `D:/CP6-local-test/CP6-72H-20261109/W441-01`。有效原版为 `red-hosted-05/actual.json`，9 项预期失败、0 跳过；最终绿色为 `green-hosted-03/actual.json`，SHA-256 `1b2221761b9c1135c747b9fe597f2bb005a7a0e82cd6d2cd96dbf1b7adedd085`。TRX、实际宿主日志、原生等待记录和 PostgreSQL 日志同目录。最终测试源码 SHA-256 `466c628f41e942261f3bdf69faa3680fd07db7ea20b848c51bbd7916614c10cd`；最终原版与绿色使用同一最终测试源码；原版在既有故障处停止，不把绿色后续的范围、封锁、只读和非整量断言说成原版也执行完成。

首个测试编译把既有字段 UnitCd 误写成 UomCd，记录失败后修正。首轮宿主运行真实复现 RECEIVE 的部分效果，但其余夹具共用 product code 违反既有 ProductMaster alternate key，改用每租户唯一模拟 code。随后原生屏障错误地从 pg_stat_activity.query 搜索产品表名：该产品 SELECT 字段很多，实际 query 被 PostgreSQL 截为 1023 字符，FROM 表名未进入观察。改为直接匹配独占控制事务的 native blocker，保留实际 SQL 片段和 PID，原八秒期限不变。有效红色记录证明三条旧路径等待产品业务锁；绿色记录证明八条新路径等待共同根。上述失败均保留，未记为成功或跳过。 首轮绿色有五项通过，三种 Apply 操作与完整周期被夹具误设的 Access=3（隐藏）拒绝；按真实合同改为 1=读写，限制场景使用 2=只读。最终红色在隔离 original-reference-build 中用已验证 W440 原版产品及引用正常编译 Tests，并实际重现全部九项故障；修复源码未恢复或覆盖。原版编译完成后的宿主运行因用户暂停中断，原未完成目录保留，未计成功；恢复时核实其 postmaster 不存在且端口可独占后，只保存并移除该隔离 PGDATA 的陈旧 PID 标记，保留全部数据，再用成功编译的同一产物运行全新夹具。并行编译尝试因可用物理内存不足 1 GiB 返回 75、没有启动编译器；随后串行完成 Tests 构建，不重复编译产品。

正常 SDK 编译相关 Core、Web API、Tests；PostgreSQL 及其他依赖复用已采用 W440 的实际成功构建、精确源和锁定 restore 输入。Core 完整分析器延续已记录的内存限制，RunAnalyzersDuringBuild=false；完整分析器未运行。集中审查后保留原两参数构造方法，新增显式三参数 Owner 构造方法；只重编 Core 与 Tests，复用未变 Web API/PostgreSQL，再按最终程序集重新执行全部九项，验证实际注入仍使用有 Owner 的入口。没有全仓回归、SQL Server 新验收、Actions 或部署。全部 fixture 与宿主已停止，端口 55568 已释放。

COORD 一次集中任务审查完整 diff：实际 Owner/Context、当前 actor 与各子动作、转换和 serial 源/目的范围、库存字段/封锁资格、父事务和原生锁序、逐条状态及库存对账、真实部门继承、回放与错误协议、回滚后跟踪对象处置、提交未知、提交后外部效果、模拟范围、真实失败/通过来源、工作流与台账及固定席位。未修改共享 Stock 原语或已有 Owner 定义；相同实现的有效证据直接复用。

## 工时、采用与余项

start 为 2026-11-10 09:24:28 UTC，开工估计 70–110 分钟，依据 W440 实测 85分39秒、W439 实测 26分36秒及八类操作范围。实际复用了完整构建依赖、现有库存父事务和身份 Owner，无需新增迁移；席位墙钟跨用户暂停，start/finish 差值只记录占用时间；实际执行以原生目标时钟为准。没有开工瞬间原生样本，使用上个窗口关闭样本到本窗收尾样本的差作为含准备工作的上界，不伪造本窗精确活跃秒数，不为估计制造等待。48 小时总预算仍使用用户确认的 172800 秒，席位和子进程时长不叠加原生时钟，下次四小时校准 115200 秒。

原 375 项保持 38 DONE、1 IN_PROGRESS、336 PLANNED；正式 PostgreSQL 16 PASS、11 NOT_RUN。本窗仅封闭现存写者故障。完整 TRACE R3-GM1.1 的来源原件、输入摘要、新版合同、UI、正式收发/盘点 Owner 采用，以及 PGT-017 全写者和 PGT-021/022 恢复验收均未由此核销。当前 legacy RECEIVE/SHIP 的 Stock 效果不作为商业收发来源证明，PICK 不冒充企业 OUT，COUNT 不冒充正式盘点完成。 证据限当前默认 Main 数据源，尚未证明租户 schema 映射/切换后 Serial 与 Stock 的同源采用，后续 PGT-017 必须按实际配置验证。

本窗同步 W440 / #882 的远端采用、准确任务分支删除、DEV-2 释放及 5138.823 秒实测。当前任务正常 PR 合入后核对远端，最终分支处置、工作区状态和席位释放以正式 finish.json 为准；下一窗口核销采用事实。
