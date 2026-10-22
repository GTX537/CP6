# W49-01：计划看板与达成率独立Owner

COORD本聊天单人；基线`2f135d6b91d520cbe0c431df677654f5611aabb4`；DEV-1 `codex/w49-01`、DEV-2 `codex/w49-handoff`，2026-10-22 10:15:11Z START，预计实际90–150分钟，实际耗时以最后finish登记为准。W37–W48已经完成，不重复；七天目标ACTIVE。

## 已交付的入口与来源边界

[Issue413](https://github.com/GTX537/CP6/issues/413)记录PG计划/达成率服务无Owner仍可读取、导出和改期，实际登录但无独立动作的三个HTTP入口仍返回200。计划bars/kpi/capture与达成率summary/export现在要求同一实际Context的OperationsOwner、原会话当前Actor、独立query/export和范围；导出同时核query。Controller保留Owner错误401/403/503并no-store。新增301 query、313 query/export仅目录，不自动给任何角色授权；迁移缺件补齐且不改变现有行/授予，没有运行既有环境迁移。

对象范围复用W42实际工单UUID部门绑定，交叉工单与报工的独立query和完整来源。含不可见报工的整个工单排除，不能先过滤隐蔽报工再把余量当总计。订单/工单父图和租户范围保持当前语义，不按名字或机器编号臆造部门/资源身份。所有读取先限范围再聚合，导出完整生成后仍经过Owner及独立来源最终权限检查，失败不返回已准备的数据。

新增capture在一次原生权限/业务图锁事务中返回同一查询的bars和KPI。过程、状态、WG、设备、拠点和时间过滤共同约束；KPI沿用排除取消工单及既有状态/完成率定义，PG延迟天数按日历日期算，不调用SQLServer DateDiffDay。ReadId/ReadAt只标识本次临时参考读取，不是持久正式SCHED BoardCapture。旧bars/kpi入口同样受Owner控制；非PG历史代码保留，不新增SQLServer实现。

达成率保留现有工单累计数量与分组/日期/比率口径，页面明确参考来源，不能当作正式PERF执行事实或分窗分母。CSV数量完整保留合法8位小数；文本以=、+、-、@开头（含前置空白）时加文本前缀，保留CSV转义。API/页面既有数字表示未升级为正式十进制线协议，不宣称JS覆盖任意大decimal精度。

## 正式排程前置与页面

已核对MES-SCHED-01已接受R2原文L124–153、L246–251：无Snapshot/Occurrence的历史数据必须LEGACY_READ_ONLY；TIME_ONLY需要完整Root、五域、源窗口、资源/执行门和真实WO共同UoW；换机另需六阶段和WO/RES共同事务。当前模型没有这些实际采用，旧ProcessStatus=0、MachineCd和reschedule/arrange动作不能替代。因此PG改期先核现权和实际目标范围，再返回SCHED_OWNER_PROTOCOL_NOT_READY；自动配置核现权/来源后同样拒绝，数量/日期/设备/审计均不改写。此次拒绝依据已接受合同，不以数据库选择任意缩小合法输入。正式新排程/资源变更和36AC仍NOT_RUN。

计划页使用单次capture，参照模式下禁用编辑字段/保存/自动配置；页面身份切换清bar、KPI和已打开详情，旧身份/旧筛选的迟到响应丢弃，401/403清保护值并停轮询，卸载停止定时器。写请求冻结输入、防重复，UNKNOWN不重发；已知成功后单独刷新失败不冒充提交未知。达成率同样隔离身份/请求顺序，撤权清统计与明细；CSV同时查query/export、冻结条件、防重复，迟到旧身份或旧条件结果不创建下载，ObjectURL用后释放。

## 验证与审查

34项相关.NET用例、8项Vitest及完整vue-tsc -b通过；同一testId复用最新有效结果，未把旧执行改称新执行。证据`D:/CP6-local-test/wave49/foundation/final-validation.json`。真实PG覆盖完整来源/可信部门、所有过滤与KPI、8位数量/公式文本、独立读导出动作、无正式写前置不产生效果、目录升级不授角色和最终权限故障丢弃结果；实际Program HTTP覆盖拒绝/允许/no-store/撤权/503。复用本窗旧两个服务9项非PG历史测试，未将其称PG验证。

最初7项真实服务/HTTP红测和8项旧组件行为红测保留；首次UI红测中的错误响应形状属测试夹具，修正后才记录真实8项产品失败。最终HTTP增量第一次错用POST而实际路由PUT导致405，修正测试方法后只重跑该用例。所有失败日志保留。一次作者集中SPEC/QUALITY审查覆盖全部产品与相关测试diff、Owner/来源/租户、PG目录、精度、前置拒绝、HTTP/UI；没有未处理的合并阻断。

5处自有PG/API已停止并核对ownership/绝对路径/进程/重解析点，回收可再生data 572559469字节，日志/TRX/runtime/ownership/缓存保留。无Actions触发/更改/取消，无代理、全仓回归、浏览器E2E、既有环境迁移或生产部署。合并仅核对远端采用和必要集成，输入相同复用成功证据。

## 剩余范围与生命周期

正常产品PR、Handoff采用、原任务最后状态PR后，核对远端、准确删除任务分支并manage finish；Issue413在生命周期完成后关闭。当前DEV-1/2仅本窗占用，集成席空闲；根用户未跟踪资产保留。正式RES/PERF/SCHED前置、Main可靠投递及CRM实际身份/共同事务等仍按七天任务继续。整卡24/375、125目标0交付、247运行NOT_RUN不因本窗提升。

## 远端采用与原任务收尾

产品[PR](https://github.com/GTX537/CP6/pull/414)及[Handoff](https://github.com/GTX537/CP6.AI-Handoff/pull/25)采用，source`d5e8fb07231cdbd5016cb028dd9ea6bab34fec51`、远端main`6cc3c5e3d205b43eded0e398ef673a3a29f5b5b6`，32任务文件blob一致；Handoff source`2ec59c822c266115bc26a50d0463374170133fc4`、目标`942eac113b9c456e37c0fea7ca4137b07eb8f80d`，准确分支删除/DEV-2释放。

34项相关.NET用例、8项Vitest及完整vue-tsc -b通过；同一testId复用最新有效结果，未把旧执行改称新执行；最后仅文档diff/链接/台账，DEV-1最后状态PR后finish/Issue413关闭。分支、工作区、席位及实际耗时以最终PR、实时登记与`C:\Users\tt\CP6-planning-reports\20261017-foundation\wave49-final-verification.json`为准，不提前宣称释放。七天目标ACTIVE，继续下一实际前置。
