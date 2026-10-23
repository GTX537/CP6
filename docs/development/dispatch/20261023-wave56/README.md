# W56 / 七天一期 D5：可信完整基线AuthoritySelection与原来源依赖图

COORD本聊天单人，2026-10-23T04:32:10.398085+00:00。Main `6cf666b86655b46f1a761260466f79aa7c98c809`，CRM `8d8dd082232c6f74204c3e30be71c9922b8725ee`，Handoff `57b2d7c4288bcf1e36074679025b73ec0c206400`。W55 CRM100/Handoff31/Main424全部采用，102项相关本地验证通过，实际47.64min、只读前置约3min；准确分支删除、固定树干净detached、全部席位finish。七天目标ACTIVE，W37–W55不重复。

## 前置核对与限定准入

最新AGENTS/POLICY哈希未变；W55最终三仓采用/Actions0证明、最新台账WP02-06/07/09/29、S6 v0.2 §3.5与§4.5及当前PG源码已核。WP02-07正式0.10.3同事务Inbox已DONE；WP02-06/09/29完整任务仍PLANNED，W51–55只是已采用的限定基础，不能越过剩余身份/业务/宿主前置。Main原始完整首尾基线/HTTP合同使用W52已采用来源，不改Main API或伪造生产验收。无重复W56 PR，固定席位容量无告警，主目录用户内容保留。

W55已经能记录冲突并保持Drift，但没有可信恢复选择；本窗追加不可变AuthoritySelection与resolvedConflictRefs，只接受注册原来源的VerifiedIdentityBaseline完整首尾目录/精确snapshot。支持操作员不能上传替代数据、指定任意hash或用“已校验”布尔解除Drift。selection保留baseline原边界/原来源、原current选择、原冲突引用及版本/CAS，不覆盖原事件或原冲突。新事件/冲突与Complete竞争同W54七锁和行fence；过期lease、较早Serializable快照、新增未解冲突、缺应有来源或基线遗漏已知事实均不能解除。opaque boundary不作字典序新旧比较，保留更高版本健康current，不把低版本基线覆盖它。

原生版本化附加层精确约束State/Current的恢复写入，只有同实际事务内、原来源外键和完整已解决冲突证明齐全的选择可以进行等版替换/解除Drift；未知/伪造/旧transaction选择无效。W51–55冻结SQL原件保留，新增显式安装脚本及强制/可选完整性检查，writer清单更新但七锁键/顺序不变。现有带状态库升级是独立迁移任务；本窗安装器拒绝暗中升级已有current/State，仅全新owned隔离库验证完整安装链，不自动切换环境。

正式内容门读取有效source selection：匹配已选权威的内容不被已解决的旧候选再次当作未解决冲突；不匹配内容仍在Duplicate/Older前识别。重复的已解决冲突保留DROP及原证据，不伪称历史所有版本一致，不借重试创造新的当前权威；新的未解冲突继续同事务关闭和调度。必须区分原内容冲突与原transport身份冲突，后者仍由原绑定决定目标。

补齐V2 Current两类真实来源的只读依赖图，复用既有依赖规则与合法C02输入范围，保留SourceKey/Origin而不把EVENT raw hash装成完整baseline hash。缺tenant/部门祖先/角色依赖和Drift保持关闭；不会新增Ready、业务Allow或Main新鲜度声明。Published持久授权、真实Owner共同最终门和实际sidecar/OIDC/broker采用仍是后继，Program/旧消费者未切换。

## 实际预算与验证

预计实际105–170min：45–75min完成可信完整基线选择、不可变解决引用和原生CAS；40–60min接入正式内容门恢复语义与真实来源依赖图；20–35min完成相关本地验证、一次作者SPEC/QUALITY和三仓收尾。纳入既有七天目标，不创建重复目标，不按日历等待。实际耗时以登记开工到最终finish记录，不重复累计子任务。

定向验证合法同版修复/保留高版健康current、遗漏/过时/伪造恢复拒绝、历史原证据不变、恢复后Duplicate/Older/旧冲突与新冲突、实际竞争40001/lease、新版缺依赖图、故障rollback与调用方commit边界；共享会话/撤权仍取原七锁，新增schema/guard损坏失败关闭。复用未变输入的成功结果，不做全仓、Actions或旧双库执行。单份产品报告引用实际TRX及失败，不以用例数作交付目标。

DEV-1 Main `codex/w56-coord`同步计划/台账/四状态；DEV-2 Handoff `codex/w56-handoff` START可靠保存后finish deferred，再bindCRM `codex/w56-01`。CRM正常PR/远端采用/精确分支删除/finish后，bindHandoff resume原分支交付，再Main最后PR与finish。固定路径/缓存复用，不新开物理树或代理，PG独立55437。允许CRM身份PG附加层、当前内容门/相关依赖图和必要readiness接入/测试/报告/四状态；Main/Handoff只协调文档。无既有库迁移或部署；24/375整卡、125目标0交付和247运行NOT_RUN保持真实。W51被自动审批拒绝的准确data删除保持停止留存，无重试绕过。

## W56实际交付

CRM [PR101](https://github.com/GTX537/CP6.CRM/pull/101)已采用，source `b220c9298e3ec2f1ad477619c0ae001289a6cd64`，远端main `6bd4dc30c962c86fe6ba3661fc52e8bbbfc4b8b0`，23任务路径exact blob核对通过。[单份产品报告](https://github.com/GTX537/CP6.CRM/blob/6bd4dc30c962c86fe6ba3661fc52e8bbbfc4b8b0/docs/development/evidence/W56-01/DELIVERY.md)。64项不同相关本地用例最终通过；新恢复/原生CAS/完整提交/跨租户/来源依赖图及旧接收/会话/基线/认证回归；API/Worker编译；真实Owner与宿主验收NOT_RUN，无Actions。

可信完整首尾基线追加不可变AuthoritySelection、逐aggregate原来源选择和resolvedConflictRefs。等版修复受原七锁、实际Serializable事务、Revision/lease/current CAS及完整提交约束；更高版健康current保留，遗漏事实/旧快照40001/过期或伪造ticket/跨租户引用/新未解冲突关闭。原生deferred Seal阻止部分选择提交，故障回滚覆盖选择、Current和会话效果。原事件、冲突及原journal不重写。

正式内容门读取已完成选择，已解决的坏内容重试仍DROP并保留原证据，新未知冲突继续Drift；transport身份冲突仍由原绑定确定目标。V2只读图保留真实EVENT或SOURCE_SNAPSHOT引用，复用C02依赖算法，缺部门/角色扣留依赖对象。没有把事件摘要冒充完整基线摘要，状态仍PendingInspection/CanAuthorize=false。

新增显式空状态安装附加层，拒绝既有状态隐式升级；六份旧SQL原件未改。守卫核函数绑定、触发时机和deferred属性。一次作者SPEC/QUALITY及相关本地验证通过，真实失败日志保留；owned PG均停止并留证。CRM准确本地/远端分支删除，固定工作区干净detached，manage finish后DEV-2复用Handoff。

后继D5继续持久Published/当前授权、真实业务Owner共同最终门与sidecar/OIDC/broker宿主采用；Program/旧消费者未切换，Main–CRM分库不冒充共同门。无Actions、既有库迁移或部署，24/375整卡、125目标0交付和247运行NOT_RUN不提升，七天目标ACTIVE。W51准确data删除曾被自动审批拒绝，原因仅“blocked by policy”；停止保留，无重试绕过。

Handoff [PR32](https://github.com/GTX537/CP6.AI-Handoff/pull/32)已采用 `87399631c8f9b25a8779047f90e729f0b5440454`，准确本地/远端分支删除、工作区干净detached、finish释放。Main仅剩当前协调分支正常PR及finish，不提前声明释放。登记开工至此约73.14min，原预算105–170min，实际最终时长以finish为准，不重复累计子任务；此前只读核对约3min。
