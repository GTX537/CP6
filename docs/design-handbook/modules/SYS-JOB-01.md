# SYS-JOB-01 · 应用后台作业接入与恢复

整理状态：`core_semantics_consolidated`。精确规则/候选字段与现实现区分；AC和运行资格维持NOT_RUN/UNPROVEN。

## 1. 业务目的、操作者和Owner边界

各应用Owner保有作业和业务状态，SYS-JOB给注册、诊断、当前门和恢复合同，不建统一业务调度主账、不迁移所有worker。注册service identity从Host和部署scope获得身份，不借最后登录用户或字符串system无限授权。新业务效果走Owner当前安全/字段/用途门，接收事实、查原结果、技术投递分别处理。[原文B:5](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:5) [原文B:13](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:13)

源码类型存在不是运行闭环；必须分CODE_PRESENT、HOST_REGISTERED、CONFIG_ENABLED、OBSERVED_RUNNING、LAST_EFFECT_VERIFIED。唤醒、扫描candidate、业务提交、receipt核实各计数独立。[原文B:11](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:11)

## 2. 选定规范与接受范围

正文v0.1，SHA256 `4950396682dc9120f89b79da2c463c7bb4f5cc5225b4bc91628fdcc26c7aa6f8`，87行，5原SPEC/25AC/15任务；S5 v0.2混合十稿原八册字节继承，双认证根复审不等本册重审或改v0.2。[原文A:154](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ea/ea6c45073f0e8db0__UA-20261008-S5-SYS-JOB-01-STATIC-MD02.json:154) [原文R:14](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e3/e3522cf5a9c30a6a__S5_v0.2_认证双根独立复审与最小问题报告_20261008.txt:14) [原文B:87](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:87)

delegate静态接受非亲签；原02:18:29Z逐字决定未恢复，04:09:04Z恢复确认另存。作者STOPPED历史保留；25AC NOT_RUN、任务PLANNED、真实运行/Owner采用UNPROVEN且实施false。[原文A:8](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ea/ea6c45073f0e8db0__UA-20261008-S5-SYS-JOB-01-STATIC-MD02.json:8) [原文A:307](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ea/ea6c45073f0e8db0__UA-20261008-S5-SYS-JOB-01-STATIC-MD02.json:307)

## 3. 作业、运行、单元与各类身份

|对象/DTO（候选）|字段和状态|身份约束|
|---|---|---|
|JobRegistration|jobKey/applicationOwner/hostType或sourceRef/definitionVersion/schedule/timezone/serviceActor/allowedTenants或scopes/configRef+version/singletonOrLeasePolicy/effectOwnerAdapter/requiredDependencies/retentionPolicyRef/enabledAt/by/reason/runtimeEvidenceRef|接入诊断登记，不另造总调度服务。[原文B:45](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:45)|
|TimeoutRunView|runId/definitionVersion/scope/evaluatedAtUtc/sourceClock或timezone/candidateCount/dueCount/appliedCount/alreadyTerminalCount/blockedCount/failedCount/unknownCount/nextScanHint|最后scan与最后effect分开。[原文B:21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:21)|
|Cleanup输入/结果|policyRef+version/tenant/dataClass/cutoff/eligibleStates/exclusions/holdRefs/referenceGraphVersion/planId/cursor；planned/disposed/held/blocked/unknownUnits/cutoff/policyVersion/receiptRefs/nextCursor|安全metadata，不原PII；逐片真实commit。[原文B:33](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:33) [原文B:35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:35)|
|JobRun / JobUnit|runId/jobKey/version/serviceActor/tenantScope/startedAt/completedAt/watermark/leaseGeneration/unit results/safe error/receiptRef/observedAt/evidenceStatus|RunId调度/观察；UnitOperationRef业务原意图；attemptId技术尝试；messageId运输，重试保持业务键。[原文B:59](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:59)|
|RegistrationStatusView|五层状态/lastObservation/blockedDependencyRefs/activeRuns|config/process观察不冒业务效果。[原文B:51](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:51)|
|RecoveryDecision|originalOperationRef/observedOwnerOutcome/allowedAction唯一枚举/reasonCode/expectedNativeVersion/digest(domain,codec,value)/currentAuthorityDecisionRef/supportRef|原Owner恢复输入输出，UI不能改成资格票。[原文B:79](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:79)|

运行状态为SCHEDULED、CLAIMED、RUNNING、PARTIALLY_COMPLETED、COMPLETED_NO_CHANGES、COMPLETED_WITH_VERIFIED_RESULTS、BLOCKED、FAILED_BEFORE_EFFECT、UNKNOWN、CANCELLED_BEFORE_EFFECT；仅每必要unit真实terminal且汇总一致才成功，catch日志不等任务成功。[原文B:61](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:61)

## 4. 扫描、清理、启停和汇总流程

超时扫描：具名definition/tenant/server clock/水位/dueAt+原状态版本→注册服务接收门→原run/lease→读到期候选→查每任务原operation/terminal→仅未完成者重核Owner当前门/冻结节点/目标版本/系统证据→按OA既有锁序提交唯一终态+receipt/outbox/audit→记录该unit。超时不是自动批准全部；领取后tenant暂停仍须效果提交门阻断。[原文B:17](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:17) [原文B:19](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:19)

清理：冻结policy/cutoff/scope/eligible/exclusion/引用版本/plan→Owner保留依据→排UNKNOWN/PROCESSING/待重排/争议/必要业务或技术receipt引用→当前hold+authority fence→处置unit+receipt→推进原cursor。读取/删除scope一致，跨tenant每域有明确政策，IgnoreQueryFilters不是全域授权。[原文B:33](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:33)

启用前核Owner、注册身份、数据源、时区、单实例或lease、原operation/receipt查询、安全/用途/hold、告警停止恢复。缺安全必需门只关闭对应能力；配置默认不授生产运行。schedule/config修改要expectedConfigVersion/operationId/reason/影响预览，missed-run由Owner定SKIP/CATCH_UP_BOUNDED/CONTINUE_ORIGINAL，不全量补跑停机历史。[原文B:47](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:47) [原文B:49](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:49)

disable停新claim，在途按业务安全点完成或BLOCKED/UNKNOWN；cancel只说明流程停，不证明业务回滚。进程重启从持久run/unit原Ref找Owner receipt，汇总只是派生不覆盖Owner原终态，late receipt追加核实不改旧日志。[原文B:49](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:49) [原文B:65](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:65)

## 5. 原作业保留域、API与跨域依赖

|固定来源行为|不能泛化的边界|
|---|---|
|WfTimeoutScanWorker每分钟TenantScopeRunner逐tenant ScanOnceAsync(DateTime.Now)|原注释v1单实例，多实例锁待增强；异常Host继续不等该run成功。[原文B:9](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:9)|
|WfServiceJobCleanupWorker每日03:00 UTC，CleanupOnceAsync(UtcNow)|终态job/fire180天、在途/占坑不删是注释，业务service未全文取得；占坑老化只报警不释放业务槽。[原文B:9](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:9) [原文B:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:31)|
|OperLogCleanup 7天/24小时，≤0停，应用DateTime.Now cutoff|只操作日志，不覆盖其他类。[原文B:9](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:9) [原文B:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:31)|
|WMS raw scan RetainUntil≤UtcNow，interval1..168小时默认24|业务task events不清，不能并入一般日志保留。[原文B:9](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:9) [原文B:39](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:39)|
|CRM小时扫描/Lead原24日历月/24小时恢复目标|另应用政策，非实际SLA已满足。[原文B:9](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:9) [原文B:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:31)|

没有本轮已证通用HTTP scan/cleanup/总控接口；候选RunList/RunDetail/UnitReceiptLink只读，HTTP route尚未建；手动触发只能导航原Owner已批准入口，不能新RUN按钮任意租户worker。依赖OA终态/锁序、AUD/PRIV/FILE保留图、S6原operation优先+三类typed replay以及INT-COM用途terminal。[原文B:21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:21) [原文B:35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:35) [原文B:63](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:63) [原文B:73](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:73) [原文B:77](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:77)

## 6. 并发、当前门与恢复算法

lease只协调worker，不替业务幂等/授权/用途；多实例需Owner真实互斥+原operation唯一，未证保持单实例配置。旧lease到期worker仍可能执行，真实效果必须原Owner generation/fence阻旧写者；停机超时不自动释放未决用途grant，INT-COM/S6无可信terminal保持PENDING_TERMINAL。[原文B:19](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:19) [原文B:77](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:77)

恢复顺序：当前read定位原operation→同义terminal回原结果→未terminal且Owner证实未应用/可继续→核原input、native version、route generation、当前安全/字段/用途/注册→按Owner原事务继续。APPLIED复用、PROCESSING等、NOT_APPLIED_PROVEN且当前合格才继续、UNKNOWN隔离；只读副本NOT_FOUND或日志缺失不是未做证明。修payload须新业务修订并关联旧结果，不冒原样retry。[原文B:65](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:65) [原文B:75](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:75)

错误分永久输入/版本拒、DENIED、AUTHORITY_FENCE_UNAVAILABLE、可重试临时network/storage、commit UNKNOWN、内容冲突隔离、cancel/停机；不能catch→retry全部或DLQ即重放。可信事实只用当前注册接收门，既有结果投递只current delivery门，不能把原人类新建权硬套全部历史事实。[原文B:73](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:73) [原文B:75](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:75)

清理按原plan/cursor续未terminal单元；hold/行版本每片提交前重核，不缓存一小时许可。分片已提交不可因run失败假全量回滚/可恢复；成功0只能确实无到期，源查询错误不能零成功。[原文B:35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:35) [原文B:37](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:37)

## 7. 权限、tenant、字段与安全观察

worker必须注册executionRef/registrationVersion和allowedScope，UserName=system日志不足证明。TenantScopeRunner只枚举Enable不证明暂停后提交被挡。candidate读取、效果写入、receipt查询的权限语义分别正确；跨库没有权威共同门则禁新业务效果并保safe历史读。[原文B:13](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:13) [原文B:17](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:17) [原文B:77](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:77)

诊断按当前scope+AUD字段/PII过滤，稳定分页；secret/payload不放metrics标签，trace不代业务receipt。清理未批准源库/demo/备份不顺手扫描；未获政策job为DISABLED_POLICY_UNPROVEN。启用job不授权安装软件/网络/凭据/生产发布。[原文B:37](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:37) [原文B:47](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:47) [原文B:63](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:63)

## 8. 页面与服务端校验

首页区分已配置/已启动/最近成功五层，展示最后scan和最后业务effect；unit时间线分claim/attempt/Owner结果。可跳原OA任务但不本页标批准/标完成。UNKNOWN给核原结果，当前拒绝给刷新身份，dependency给Owner修复；长期blocked告警责任Owner/影响，避免无界重复通知。[原文B:23](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:23) [原文B:51](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:51) [原文B:63](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:63) [原文B:79](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:79)

cleanup预览显示dataClass/scope/cutoff/hold及不可逆，计划数量与实际处置分列；部分完成/unknown不提示全成功。schedule变更显missed-run政策而非默认catchup；过期预览不授执行，disable保在途真实状态。[原文B:35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:35) [原文B:37](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:37) [原文B:49](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:49)

## 9. 实现顺序与固定证据缺口

固定Main90c871...四worker、CRM固定c778a3来源只证明部分CODE_PRESENT，其他运行层Required；WFS180天业务服务正文缺失，多实例协调未证，源码注释和Host类型不是生产证明。[原文B:5](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:5) [原文B:9](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:9) [原文B:11](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:11)

15任务：①OA实际超时冻结规则/系统证据/锁序和unit幂等；②各job保留域/排除全集/hold+分片receipt；③各应用Host/config/依赖/单实例或lease/missed-run；④原Owner结果adapter和五层安全诊断；⑤S6精确恢复、generation/fence与长期blocked。保持各Owner执行账，不先建统一业务调度。[原文B:27](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:27) [原文B:41](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:41) [原文B:55](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:55) [原文B:69](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:69) [原文B:83](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:83)

## 10. 验收场景与状态

25AC全NOT_RUN：JB01冻结timeout/真实actor、人工worker单结果、撤权/停tenant门、分计数、unknown先查；JB02各保留锚点/占坑和引用/hold竞争、分片真相、WMS events不删；JB03五层不冒running、门缺关闭、版本missed-run不全重放、disable不假回滚、原operation不双consumer；JB04身份分开/catch和Count不成功/APPLIED不重做/安全观察/late追加；JB05错误分类/原operation typed恢复/旧epoch不能写/跨库不足关闭/无terminal用途不TTL释放。[原文B:25](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:25) [原文B:39](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:39) [原文B:53](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:53) [原文B:67](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:67) [原文B:81](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:81)

## 11. 未完成与不能推定

每任务真实Host注册/config/生产观察/效果receipt、业务service完整保留逻辑、共同writer/generation/hold采用仍需Owner证据。规范明确无采用就关闭相应新效果，不是允许先运行试探；安全历史查保留。Run汇总与业务Owner两账不能相互写假终态。[原文B:11](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:11) [原文B:47](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:47) [原文B:65](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:65) [原文B:77](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:77)

## 12. 原件与阅读覆盖

正文全文1–87，全部5SPEC/25AC/15任务实读；S5最终复核全文1–128；接受JSON完整决定/正文/组合/ReviewQualifications/执行边界键结构读。未扩为真实Host/配置/生产作业检查，不运行定时器、测试、构建、脚本或任何业务任务。[原文B:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/49/4950396682dc9120__S5_SYS-JOB-01_完整设计候选_v0.1.md:1) [原文R:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e3/e3522cf5a9c30a6a__S5_v0.2_认证双根独立复审与最小问题报告_20261008.txt:1) [原文A:8](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ea/ea6c45073f0e8db0__UA-20261008-S5-SYS-JOB-01-STATIC-MD02.json:8)

机器合同见 [platform-engineering-contracts.json](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/platform-engineering-contracts.json)，阅读范围见 [platform-engineering-reading.json](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)。

**当前必要材料归并状态。** 本册为 `required_materials_consolidated`：选定正文、规范附件及准确接受/复核范围已按直接实读、结构化语义或精确复用归并。身份清单核对不等底层源码全文阅读；历史草稿、候选实现和Office/HTML视觉未核范围仍单列。可复查[逐件阅读与处置](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)及[本组闭包台账](D:/CP6/docs/CP6_开发设计文档_20261010/worklogs/platform-engineering-progress.json)。当前必需规范未读清单为空；具体Owner采用、参数/物理接口、真实运行和本节已有来源缺口不因整理闭包而改变。
