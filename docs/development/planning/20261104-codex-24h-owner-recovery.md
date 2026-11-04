# CP6 第二轮24小时业务Owner恢复执行计划

> 使用 writing-plans / executing-plans，由COORD本对话单人执行，沿仓库精简验证和固定席位规则；无需再选执行模式。

**Goal:** 从原375真实余项推进可信业务撤回、受控字段处置及OA整卡剩余，完成约24小时有效投入及预算末尾当前完整任务交付。

**Architecture:** 复用现Main当前IAM/WF_AUTH、Owner原命令/审计、oa.app/2.0提交与撤回合同。业务当前权限、共同控制头、不可变撤回事实及应用效果在同一PostgreSQL事务裁决；跨事务运输复用原body/原键，不能把机器ACK当业务应用。业务与外部输入可模拟，内部Owner实际实现与验证。

**Tech Stack:** PostgreSQL18、.NET8/EF Core、Vue3，实际Main Program登录HTTP及必要真实Chrome。

## 实测校准与完成定义

上一轮29窗及9个BUG已按W296最终证明收妥，原12窗实测11.41h，仍低于原15.75–29.75h初估；28个前序窗中位39.9分钟、范围6.3–119.7分钟。不得继续把一个小切片或计划提交叫三天完成。本轮从新目标创建重新计时，不含两轮之间用户离线间隔。

24h是有效投入预算，不保证所有下列储备窗同时完成。D1/D2/D3对应累计0–8/8–16/16–24h，按依赖动态移动。表中P50/P80为低置信度工程估计，并非校准过的概率；区间合计只是工作队列量级，不能把逐项P80相加宣称总体80%概率。先落实际调用链，再根据偏差调整。

每累计2h及复杂窗口结束记录实际秒数、已交付范围、预测和偏差；若快于预测，接原375中本链相邻未完事项；若慢于预测，保当前完整闭环并推迟储备项。计划/审查/必要验证/正常交付计一次，暂停、排队、空等、两仓重叠及重复证据不重复计。以新目标timeUsedSeconds采样并核活动区间，不能用墙钟跨暂停推算。预算末尾如实盘点并完成当前窗口正常合并/清理/finish，不人为等整数小时。

## 串行开发队列

| 窗口 | 初始时段 | 原卡 | 可独立验收结果 | P50小时 | P80小时 |
|---|---|---|---|---:|---:|
| W297-01 | D1 | WP12-19 | DOC完整可信撤回与原应用互斥 | 2 | 3.5 |
| W298-01 | D1 | WP12-19 | 需求集实际撤回及显式重新授权 | 1.25 | 2.5 |
| W299-01 | D1 | WP12-19 | 版模处置Owner撤回与当前资格竞争 | 1.5 | 3 |
| W300-01 | D1 | WP12-19 | 库位规则Owner撤回与当前控制头 | 1.5 | 3 |
| W301-01 | D1 | WP12-19 | 原可信撤回卡逐项核销与真实缺口修正 | 0.5 | 1.25 |
| W302-01 | D2 | WP02-15, WP12-03 | 受控字段处置的权威Owner与最小实际闭环 | 3 | 5.5 |
| W303-01 | D2 | WP12-03 | 个人表单隐藏字段重基采用处置Ref | 2 | 3.5 |
| W304-01 | D2 | WP12-04 | 原表单提交卡合法域和原子性剩余核销 | 1.5 | 3 |
| W305-01 | D3 | WP12-10 | 历史身份准确映射的显式采用与缺证边界 | 2 | 4 |
| W306-01 | D3 | WP12-08, WP12-04, WP12-10 | 原任务信箱前置与剩余实际行为闭环 | 1 | 2 |
| W307-01 | D3 | WP05-16 | 受控技术文档独立可用性和原版本撤回 | 2 | 4 |
| W308-01 | D3 | WP05-14 | 需求集真实适用交集冲突及裁决 | 2.5 | 4.5 |
| W309-01 | ROLLING | WP05-14 | 需求集后继差分和实际消费者影响意图 | 2 | 4 |
| W310-01 | CLOSEOUT | WP12-19, WP12-03, WP05-14 | 预算末尾真实余项核对及当前任务收妥 | 1 | 2 |

## W297：DOC首个完整纵向切片

当前`ApprovalBusinessEvidenceV2.Withdrawal`仅ITER实际采用。DOC已有准确原件、当前权限、提交/决定/应用，但没有业务撤回控制头；`ApplyAsync`使用当前应用ETag与回执，不能把它或Workflow版本伪称业务撤回CAS。ITER业务阶段/责任规则不能照抄到DOC。

- [x] 在`CP6.Core/Services/Plm/DocumentReviewControlStoreV1.cs`及前向PG迁移建立准确Scope/DOC/Submission控制、不可变撤回事实/Outbox和真实opaque版本。新提交同事务登记；已接受旧提交明确采用来源，不能GET造历史或把未知当IN_REVIEW。原审批版本和内容hash保留。
- [x] `ControlledDocumentReviewOwnerV1.Withdrawal.cs`提供`WithdrawAsync(document,submission,ifMatch,ApprovalUserWithdrawV2,ct,behalf)`和`ReauthorizeWithdrawalAsync(...,previous,...)`。用户正文沿原`{commandId,reason}`封闭合同，认证实际人/业务责任/当前原Grant在Owner决定；`ApprovalUserWithdrawV2`和服务事实类型已在`ApprovalWithdrawalV2.cs`定义，禁止浏览器提供事实/Actor。500 Unicode字符原因完整保留，原命令元数据另记If-Match/代理上下文。
- [x] `ControlledDocumentReviewOwnerV1.Application.cs`及`ControlledDocumentReviewOwnerV1.cs`令apply与withdraw参与同control：应用先赢保原APPLIED且不产生撤回事实；撤回先赢后迟到批准只能STALE。新明确授权只接已BLOCKED/未终态且准确业务control，追加事实/命令和连续receipt，保原终态与来源。
- [x] DOC当前授权入口`OperationsAccessV1.ControlledDocument.cs`、资源动作目录/权限种子及`ControlledDocumentReviewController.cs`显式注册独立撤回/重新授权及必要业务授权管理；query、callback.replay或任务代理不授业务撤回。当前角色、对象、字段及SOD在负责入口和最终决定点核对，内部保证不逐层重验。
- [x] `ApprovalBusinessEvidenceV2.cs`增加明确DOC分派；`DocumentWithdrawalDeliveryV1.cs`及实际DI/worker按现可信路由采用。网络操作在数据库锁外，原bytes、真实Owner/身份和原键保持；未知先查询原结果，412只刷新Workflow header，不能换body/Actor。
- [x] `cp6.web/src/views/plm/ControlledDocumentReviewPanel.vue`及实际API模块采用业务上下文/当前token，展示业务已撤回与Workflow仍待确认；显式再授权、原结果查询及身份改变清理。恢复缓存只存原意图标识，不存文档明文；存储失败不先发送。
- [x] `CP6.Tests/Erp/ControlledDocumentWithdrawalPostgreSqlTests.cs`使用现DocumentDecision/Application夹具，验证真实PG两连接apply/withdraw互斥、原事实/Outbox/审计一起回滚、先撤回迟到批准STALE、原grant撤销BLOCKED、明确新grant续作、提交丢答原键查询/重试、跨租户/伪Actor拒绝及旧提交来源边界；复用已有完整提交/引擎证据，不重跑全仓。
- [x] 实际Main登录HTTP及页面接同一DOC Owner：可信撤回、角色/委派撤权、丢响应刷新恢复，最终PG检查一个事实/原命令结果。受影响组件行为和vue-tsc；一次完整任务审查。单一报告、四项目状态、roadmap/生成页同步；正常Main/Handoff采用、准确分支删除和官方finish。

## 后续窗口的文件与实际验收边界

- W298：`RequirementSetReviewOwnerV1*`、`RequirementSetReviewApplicationStoreV1.cs`、`RequirementSetReviewController.cs`、`RequirementSetReviewPanel.vue`，按需求集真实责任、EvidenceMode、准确修订及当前专业政策采用；共用已有wire，不新造第二权威协议。实际先应用/先撤回、BLOCKED显式授权和原结果恢复；DOC成功证据只复用未改变共同运输部分。
- W299/300：`ToolDispositionApprovalOwnerV1*`与`LocationRuleReviewOwnerV1*`实际消费域；先核原合法撤回时点和库存/用途/规则当前头，保持原财物Owner权限及金额数量精度。撤回不能逆转已经发生的库存/财务效果；只有原业务允许的状态才实施。分别用真实Owner与PG竞争，不能用ITER模拟替代。
- W301：原WP12-19三项与正反验收逐项对应实际Owner，范围无剩余才DONE；125目标/247正式验收独立，不自动关闭。
- W302：沿WP02-15原隐私/处置合同读取实际Owner、hold/保留政策和引用。明确受控处置谁有权批准/执行/失效，引用绑定Scope、准确字段/原版本、不可变处理结果；源中不存在的Owner须真正实现，不构造URI、不临时授读明文。先完整勘察再精化迁移与公开命令，不能凭此计划宣布已采用或全隐私卡完成。
- W303：`PersonalFormOwnerV2*`的显式rebase及`PersonalForm*`页面，把W302真实受控引用用于隐藏字段损失；可见数据精度/遗漏/null继续保留，准确版本/当前权限/处置失效在提交点核对，新的字段损失必须重新明确确认。相关PG并发、授权拒绝、实际页面正例。
- W304：原WP12-04按现SFS提交、文件、规则、精度、原命令与并发的有效证据核销；只补实际漏项，不能因既有组件或局部PASS关闭整卡。
- W305：`NativeTaskHistoryV2*`及原W292缺证边界，准确原principal/grant/legacy来源只由可追溯映射提供；明确隔离PG中的前向采用命令和重放/拒绝，不在既有环境运行迁移、不用当前角色补历史。若原卡只需已有实际缺证呈现，则按原AC核销，不为凑时新增映射层。
- W306：在WP12-04/10原前置完整满足后核WP12-08当前本人/代理/抄送、披露/动作隔离、分页和撤权。保原准确Owner，不重做W291已完抄送读取。
- W307：`ControlledDocumentOwnerV1*`和当前文件下载入口，按WP05-16原独立文档hold/withdraw/availability合同明确旧版可用性/保留，不把FILE hold当DOC状态；真实旧URL/current权限拒绝与准确旧版合法读取，避免与W297审批撤回混同。
- W308/309：`RequirementSetService*`与`RequirementSetReviewOwnerV1*`按WP05-14原scope交集、澄清/范围分离/ECM/DEV裁决及后继差分/消费者影响意图逐原Owner实现；批准不自动等于冲突已裁决。实际Source可模拟外部输入，内部缺失Owner不能NoOp冒成功。
- W310或预算末尾当前窗口：汇总实际有效工时、预测偏差和原卡剩余；未开始储备项保PLANNED。此项是收妥当前目标，不再开自动下一周期。

## 本地验证与任务生命周期

从已核最新Main基线创建每个独立任务分支，DEV1 Main与DEV2 Handoff固定复用，集成空闲。相关测试沿既有`run_waveNNN_main.py`的隔离PG环境/共享构建缓存执行，首次实际行为失败保原日志，修改后只做对应回归。计划和普通文档不启动产品测试。普通GitHub工作流内容/事件和受保护主线引用在push/PR前核对，未变化复用结论；不触发Actions，不部署、不迁移既有数据库。

本轮基线30DONE/2IN_PROGRESS/343PLANNED，原59卡余53；七天/W249/W267和上一周期已完成范围不重复。只读跨仓核对不修改CRM或Platform主目录，用户所有原始/未跟踪资料保留。每窗交付同时报告分支处置、工作区状态、席位释放。

W297产品实现、相关验证与集中审查完成，有效累计采样3259秒。最后一项中的HTTP/页面/组件验证已通过，交付流程最终完成以wave297-final-verification.json为准，尚不宣称席位已释放。本项初估2–3.5h低置信，最终finish后按实际耗时校准；下一W298，约24h目标继续。

## W298实际开工与首窗工时观察

W297两仓远端采用/正常finish已核对，来源`wave297-final-verification.json`。Main PR743、Handoff PR274；原分支删除、固定席位干净释放。W297有效3412秒，低于初估2–3.5h；本轮累计3553秒。首个样本不代表总体估计，累计7200秒时结合W298再校准，较快即继续原375真实余项，不能结束24h目标。

W298从`afc3cc08a081f2affc3f7033b758e312838169d6`开工，只有本对话写入。沿已核直接前置的当前授权/Scope最终fence、原命令/审计、oa.app冻结提交/可信撤回和已真实采用的RequirementSet Review。任务专属执行清单见[W298](../dispatch/20261104-wave298/README.md)；本轮原125/247和原卡状态不因此改变。

## W298交付与实测插入项

需求集实际撤回/精确grant/共同CAS、前向采用和真实工作台恢复通过，累计有效采样4849秒。最终生命周期/本窗耗时见wave298-final-verification.json。浏览器发现既有直达链接缺陷[BUG744](https://github.com/GTX537/CP6/issues/744)，在W298后、W299前独立修复，初估0.5–1.25h低置信；活动BUG状态仅以Issue为准。余下队列继续，非三天完成。
