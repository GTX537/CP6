# BUG-895：承运元数据当前授权、原子写入与并发追加

2026-11-10 UTC。[Issue #895](https://github.com/GTX537/CP6/issues/895)。COORD 单人复用 DEV-2，任务分支 `codex/bug-895-carrier-lifecycle-fence`，基线 `e2bfbf6978318599fda8910f4f3d7cf5f52365b2`。start 成功一次，租期始于 18:08:22.504476Z，开工前原生样本 133640；估计 35–55 分钟实际 Codex。总预算按用户确认的 48 小时 / 172800 秒，租期、编译和验证阶段不另计目标时间。

## 根因与修复

原承运服务八个入口中，两种读取只有登录检查，六种写入依赖早期权限。暂停先提交后，实际八个请求仍返回 200，六种写入改变原行；没有 view 的读取同样返回 200，原生撤销 add 后已进入请求仍能创建运单。创建先独立保存 SHIP 编号，后续业务失败无法一起回滚。

实际原生故障进一步证明：创建返回 500 后，编号与失败运单均由 0→1；状态更新返回 500 后，原运单的状态及事件仍被后续日志保存改写。这是请求跟踪器残留导致的实际写入，完整 JSONB 前后写集保留了结果。

八个服务方法现在复用 IamAdministrationV1.ExecuteCurrentAsync、同一请求 Context 和实际签名会话。创建仍用 `wms-carrier:add`，五种追加/状态操作仍用 `event`；两个读取使用新 `view`，在实际 WmsPermissionSeed OwnerActions 登记，种子不自动授予角色。采用环境须经既有 IAM 显式分配。共同认证根后先围栏实际承运表，创建再围栏编号表；同 Context 的现有编号服务与运单共父事务。最终会话/取消及回滚清理复用既有 Owner；数据库故障返回 CARRIER_AUTHORITY_UNAVAILABLE 503，未知提交保留 CARRIER_OUTCOME_UNKNOWN，不盲目重试。

围栏位于加载可变事件列表之前，保留现有 EF 版本检查。实际原问题不是“两次均成功却静默丢失事件”：受控并发中首个返回 200，第二个返回 500，实际日志为 DbUpdateConcurrencyException，证明旧版本检查仍有效。修复后两个合法追加均返回 200，first/second 两条原事件均保留；未删除并发标记、放宽冲突检查或添加重试。

ICarrierService、两参数构造、原成功协议、手工状态和业务错误保持。原 Core 业务主体只有私有化重命名，状态、事件 JSON、重量/费用、追踪号生成与过滤均未改写；非 PostgreSQL 仍走原业务主体。Program 显式注入同 Context/IAM/实际会话及请求取消，Controller 采用既有 OrderOwnerResult，六个原业务异常捕获排除 Owner 错误，保留 401/403/503 和 no-store。没有模型、迁移、平台包、源绑定、POD、财务、库存或外部承运 API 改动。

## 实际验证

**16 项实际 Main Kestrel / PostgreSQL 18.6 场景及 5 项相关本地兼容/种子检查通过，零跳过。** 业务、运单/包装/追踪号、地址及事件明确构造；真实登录 Cookie/CSRF、原 HTTP 路由、实际数据库锁/触发器和完整写集实际执行。旧手工元数据不因此取得真实承运或 POD 资格。

| 场景 | 原产品实际结果 | 修复后实际结果 |
| --- | --- | --- |
| create/event/pickup/in-transit/delivered/fail/list/detail 暂停先提交 | 八个 200，六种写入改变原行 | 八个实际等待共同根并返回 401；原行保持，重新激活/登录后恢复 |
| list/detail 无 view | 两个 200 | 两个 403，原行保持 |
| add 撤销先提交 | 200，仍创建 | 等实际认证根后 403，原行保持 |
| 业务先提交再暂停 | 暂停未等待业务根 | 暂停等待共同根，已提交运单历史保留 |
| 创建原生 INSERT 一次故障 | 500，编号与失败运单各残留一条 | 503，五组写集保持；移除测试故障后同输入正常创建且编号仅 1 |
| 状态原生 UPDATE 一次故障 | 500，状态与事件被日志重刷 | 503，五组写集保持；已知回滚后恢复为一次有效 pickup/事件 |
| 先控制首个 UPDATE，再放入第二个事件追加 | 200 / 500，第二个实际 EF 并发异常 | 200 / 200，两条原事件均保留 |

五组完整写集为运单、编号、库存、库存流水、商业出库单。并发用例在 PostgreSQL trigger/advisory barrier 控制首个更新，再观察第二个实际阻塞关系；不依靠延时猜测竞争结果。首次采集脚本曾错误预期原两个响应均为 200，实际 200/500 与异常日志纠正了该假设；保留首次脚本断言/terminal，修改采集预期后直接核验同一次 16 项原件，未重跑原产品。原生测试源码未改：原 16 项中 15 项实际复现产品问题，正常手工周期 1 项通过，最终同源码 16 项全过。独立 observer-expectation-correction.json 记录纠正，原 actual.json 不覆盖。

正常周期覆盖空包装/承运代码 400、显式追踪号、自动追踪号格式、实际 Creator/Modifier、重量 3.456 与费用 98.76、提前 in-transit 拒绝、重复 pickup/Delivered 拒绝、Delivered 后 fail 拒绝、PickedUp 可直接 Delivered、合法重复 Failed 各保留事件、五类过滤与列表/明细、缺失单号 404。手工添加 Status="Delivered" 文本后，运单仍为 Created；这只证明原手工事件不触发状态转换，不能作为 POD 形成或财务应用验收。无关库存八位数量和四位金额、库存流水保持。

相关本地 5 项为原 3 个两参数构造 InMemory 承运行为、实际种子幂等且不授予 view 的新增回归、既有 WMS 写权限目录检查。新种子回归在原产品实际失败，修复后通过；旧目录检查仅覆盖写入口，不当作读取授权证据。

本机原件 `D:/CP6-local-test/CP6-72H-20261109/W447-01`：red-build-01 / red-hosted-01 保存原 16 项，seed-red-01 为缺目录真实失败，observer-expectation-correction.json 为原观察预期纠正；green-build-01 为最终相关正常 SDK 构建，green-hosted-01/actual.json 为最终 16 项，compatibility-01/actual.json 为最终 5 项，actual.json 汇总当前源码和产物。未变依赖复用 W446 正常 SDK 产品；Core 完整分析器沿资源限制关闭，Core/Web/Tests 已有警告保留。两次实际 API/PostgreSQL 均已停止，测试触发器按 finally 删除；无远程 Actions、SQL Server 新验收或部署。

## 审查、采用及余项

COORD 一次任务级 SPEC/QUALITY 集中审查覆盖完整分支：当前 actor/动作、根与表/编号锁序、同 Context 父事务、失败跟踪清理及未知提交、读取目录无自动授权、原始版本检查及实际双追加、原接口/构造/错误/状态/精度、未变原生测试与正常 SDK 来源、台账和固定席位。调用链检索确认当前消费者为现有控制器及兼容测试。工作流树保持 `3355235636fd45116f5445fca32fc65e49f13003`；推送/PR 前复核当前引用，复用未变普通手动入口及生产受保护 Tag 过滤分析。

报告生成时为待远端采用候选。正常合并并核远端 main 后关闭 Issue，再由 finish 准确处置本任务分支、工作区及席位，最终以 PR/远端/finish 为准。台账同步核销 W446 经 #894 进入远端 main、任务分支准确删除和 DEV-2 干净释放后复用；WP02-03 / WP10-43 两处交付一致收妥。W446 保守原生活跃 40.26–44.30 分钟，落在原估 40–60 内；开工前约 219 秒实际采样间隔已从下界扣除，未冒用不足一分钟假设。W447 最终耗时待闭窗核算。

本窗为 WP02-03 现存 Owner 采用，不完成 WP10-52 的真实销售 SourceBinding、承运 Attempt/Observation、逐行 POD/不可变 Fact、连续累计 Proof、F0 当前源或财务原结果/Outbox 同事务。Delivered 文本不能生成 POD，手工状态不能证明物理签收；后续可按用户口径用明确模拟业务/服务执行完整相同合同，不能用本窗替代未实现的源提供者。完整原任务仍 PLANNED/NOT_CHECKED，其直接前置不因本窗核销。其余 query/write/export/download 与字段 V1 来源、PGT-017 全 writer 仍待对应范围。原 375 项仍 38 DONE / 1 IN_PROGRESS / 336 PLANNED，余 337；正式 PostgreSQL 16 PASS / 11 NOT_RUN，125 目标和 247 runtime 原验收未核销。48 小时预算不变，36 小时校准保留，下一原生检查点 144000 秒；W436 时钟重建未解状态保留。
