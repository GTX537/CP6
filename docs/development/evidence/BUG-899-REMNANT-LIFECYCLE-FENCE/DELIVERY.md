# BUG-899：余料当前授权、原子写入与统一输入边界

2026-11-10 UTC。[Issue #899](https://github.com/GTX537/CP6/issues/899)。COORD 单人复用 DEV-2，分支 `codex/bug-899-remnant-lifecycle-fence`，基线 `f43c8714398854b8ffb517a82322733eda387a91`。start 成功一次，租期始于 19:12:04.967047Z；开工前原生样本 137463，估计 20–40 分钟实际 Codex。按用户确认的 48 小时 / 172800 秒执行，租期及验证阶段不另计目标时间。

## 根因及修复

原三个读取只有登录检查，七种写入依赖早期权限。实际十个受阻请求在暂停先提交后均返回 200，七种写入改变原行；缺少 view 的读取同样返回 200，add 撤销后仍能创建。REM 编号独立保存，后续创建失败不能共同回滚；失败跟踪状态还会被后续日志保存重刷。实际 INSERT/UPDATE 一次原生故障均返回 500，分别残留编号/余料或改写原余料。

原修改接口没有创建接口已有的正尺寸、正数量及仓库/库位输入校验。五类实际请求——零宽、零长、零数量、空仓库、空库位——均返回 200 并改写原行。新 ValidateMetadata 是这些不变量唯一的业务输入校验点，创建及修改各在拥有该输入的入口调用一次。修改先保留原结束状态检查，再校验、再改写；Used/Disposed 的错误优先级及合法 Reserved 修改保持，没有新增任意尺寸/数量上限、单位或来源资格检查。

十个服务入口现在采用既有 IamAdministrationV1.ExecuteCurrentAsync、同一请求 Context 和实际签名会话，保留原 add/edit/reserve/use/dispose/del 动作。三个读取采用实际 OwnerActions 新增 view，种子不自动授予角色，采用环境须经既有 IAM 显式分配。共同认证根后围栏余料表，创建再围栏编号表；同 Context 的现有编号服务和记录共父事务。最终会话/取消、失败清理及未知提交语义复用既有 Owner，数据库故障为 REMNANT_AUTHORITY_UNAVAILABLE 503，未知提交保持 REMNANT_OUTCOME_UNKNOWN，不盲目重试。

竞争预留保留原 EF 版本检查，并在根/业务围栏后读取当前状态。原受控竞争实际为首个 200、第二个 500/DbUpdateConcurrencyException；修复后为 200/400，保留 FIRST 用途及原数量。新行为没有删除版本检查、改用覆盖或自动重试。这里的 Reserved 是旧元数据状态，不是 RSV 物理预留凭据。

IRemnantService、两参数构造、原 DTO/成功协议及其余业务规则保持。余料数量仍为现有 decimal(18,4)，没有照搬其他模块的八位表示或缩小其合法范围；无关库存仍为八位数量。Controller 采用 OrderOwnerResult，七个原业务异常捕获排除 Owner 错误；Program 注入同 Context/IAM/实际会话和请求取消。未改模型、迁移、平台包、Root/Claim、Stock、RSV/MAT 或外部系统。

## 实际验证

**24 个不同的实际 Main Kestrel / PostgreSQL 18.6 场景，以及 4 项相关本地兼容/种子检查通过，TRX 零跳过。** 余料、来源文字、用途及业务数据明确构造；真实 Cookie/CSRF 登录、原 HTTP 路由、PostgreSQL 锁/触发器和五组完整 JSONB 写集实际执行。

| 场景 | 原产品实际结果 | 修复后实际结果 |
| --- | --- | --- |
| create/update/reserve/unreserve/use/dispose/delete/list/detail/match 暂停先提交 | 十个 200，七种写入改变原行 | 十个等待实际认证根后 401，原行保持；重新激活/登录后恢复 |
| list/detail/match 无 view | 三个 200 | 三个 403，原行保持 |
| add 撤销先提交 / 业务先提交再暂停 | 撤销后仍创建；暂停未等业务根 | 撤销后 403；已先获根的业务先提交并保留历史，暂停随后提交 |
| 原生 create/update 晚期故障 | 500，编号/记录有实际残留或改写 | 503，五组写集保持；已知回滚后正常恢复，创建编号仅 1 |
| 五类非法修改输入 | 均 200，原行改变 | 均 400，原行保持 |
| 两个受控竞争预留 | 200/500，实际 EF 并发异常 | 200/400，首用途 FIRST、状态 Reserved、数量 1.2345 保持 |

五组写集为余料、编号、库存、库存流水、商业出库单。正常周期实际覆盖创建及修改、三种读取、空用途拒绝、重复预留拒绝、合法预留态修改且用途不丢、释放、使用/重复使用拒绝、结束记录优先 WM-MSG-043、处置/重复处置拒绝、软删除及明细 404。MarkUsed、Dispose、Delete 均不暗扣原数量。还实际保存/读回现有数量表示的最大值 99999999999999.9999、最小正数 0.0001 及 int.MaxValue 宽长；七类过滤、实际 Creator/Modifier 与无关八位库存/四位金额保持。

同一原生源码先在未变原产品执行 24 项，23 项完整复现问题、正常周期 1 项通过，最终相同源码 24 项全过。首次 red-build-01 因模板替换过宽误改四处 RollbackAsync，属于夹具编译错误；仅纠正名称后 red-build-02 才执行上述原产品用例，失败日志保留，不把编译错误计为产品问题。xUnit 发现阶段提示一条重复 match 参数声明被忽略；实际 24 个不同输入场景和 24 个真实 Main 宿主均执行，TRX notExecuted=0。重复声明不计额外覆盖；该非阻断警告保留，未为它重复业务验证。

4 项本地为原两种 InMemory 余料生命周期/匹配、新增实际种子幂等且无 view 角色授予、既有 WMS 写权限目录检查。新读取目录回归在原产品实际失败后通过；旧目录检查只扫描写接口，不冒充 GET 采用证据。正常 SDK Core/Web/Tests 相关构建通过，未变依赖复用 W448 正常 SDK 产品，Core 完整分析器沿资源限制关闭；已有警告保留。

本机原件 `D:/CP6-local-test/CP6-72H-20261109/W449-01`：red-build-02 / red-hosted-01 为有效原产品复现，seed-red-01 为目录真实失败；green-build-01、green-hosted-01/actual.json 和 compatibility-01/actual.json 为最终来源，actual.json 汇总当前源码及产物。两次实际 API/PostgreSQL 均已停止，原生触发器按 finally 删除。未触发 Actions、新增 SQL Server 验收或部署。

## 审查、采用及真实余项

COORD 一次任务级 SPEC/QUALITY 集中审查覆盖完整分支：当前 actor/动作、同 Context 与根/余料/编号锁序、父事务和失败跟踪清理、最终会话/取消及未知提交、统一输入边界和结束态错误优先级、合法预留态修改/数量范围、竞争当前状态及原版本检查、原接口/构造/其余业务、原生源码/SDK 来源、台账及固定席位。工作流树保持 `3355235636fd45116f5445fca32fc65e49f13003`；推送/PR 前复核当前引用，复用普通手动入口与生产受保护 Tag 分析。

报告生成时为待远端采用候选。正常合并并核远端 main 后关闭 Issue，再由 finish 准确处置分支、工作区及席位，最终以 PR/远端/finish 为准。台账同时核销 W448 经 #898 进入远端 main、任务分支准确删除、DEV-2 干净释放后复用，WP02-03/WP10-33/WP10-34 三处一致收妥。W448 保守原生活跃 21.90–22.50 分钟，短于 35–55 原估，同框架复用窗口改参考 20–40 分钟；完整 R3 任务仍须按实际范围单独估计。

本窗不完成 WP10-35 的真实 Origin/RootCoverage、UNVERIFIED_INTAKE/零 Stock 激活、RSV 预留/释放、MAT/Stock 实际消费及报损；旧用途文字和手工状态不产生 Root/Claim 或物理数量效果。后续可按用户口径以明确模拟业务/服务执行完整同一合同。完整原任务仍 PLANNED/NOT_CHECKED，直接前置不核销。WP02-03 其余 Owner/字段 V1 与 PGT-017 全 writer 仍待对应范围。原 375 项仍 38 DONE / 1 IN_PROGRESS / 336 PLANNED，余 337；正式 PostgreSQL 16 PASS / 11 NOT_RUN，125 目标与 247 runtime 原验收未核销。48 小时预算不变，36 小时校准保留，下一原生检查点 144000 秒；W436 时钟重建未解状态保留。
