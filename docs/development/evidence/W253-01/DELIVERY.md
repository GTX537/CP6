# W253 / D2：CRM切组织与审核期限实际验收

D2实际CRM登录/切组织/旧会话失效通过；七天审核期限前、整点、之后及写入越界回滚通过，修复CRM Issue164。读取到期禁用动作；Expired持久终态与对应保留策略仍未实现，不关闭整卡。继续D3编号原命令提供者。

## 实际行为与修复

真实HTTP登录A经过CRM组织发现、授权请求、callback、RSA签名JWT验证、当前身份读取和PostgreSQL持久会话；callback消耗原种子会话并创建新会话。切组织清除Cookie并Take旧会话，缺少或错误CSRF均403且原会话保留。旧Cookie的session/询盘读取和放行写入均401，重试不重建会话、不新增决策。实际登录B后context仅为B，A的询盘404。

外部IdP组织/授权码响应与业务身份为明确本地TLS模拟服务；没有用替身替换CRM身份客户端、认证服务或业务Owner。两个隔离宿主代表组织A/B，依次发布测试信任，未把该场景称为生产IdP注册、浏览器跨页UI或既有环境部署。

[CRM Issue164](https://github.com/GTX537/CP6.CRM/issues/164)复现：放行/拒绝开始于审核截止前，完成图写入时跨过截止，旧代码仍200提交。两条RED确实失败。修复在当前权限最终核对之后、同一事务commit之前用PG clock_timestamp再次判定新决策期限；到期409并回滚Lead/Submission/历史/原命令/审计/outbox。已提交原命令仍按原结果恢复，不因后来审核到期生成新效果或变成UNKNOWN；原当前权限、同意、PII保留期继续适用。

实际公开提交创建完整政策/Attempt/Consent/提交/原结果图，固定七天reviewWindow未变。受控内部审核时钟驱动截止前1tick、等于、后1tick；读取能力时钟同样在原图上验证动作：之前release/reject可见，到期无动作。期限前合法放行/拒绝成功，期限后原key返回同一结果；写入中跨界整组回滚。身份、会话、持久保留与响应披露仍走实际时间；没有直接UPDATE不可变原件、缩短七天或等待七天的声明。生产未设置这些内部测试时钟，仍取PG时间。

## 范围判定

R3V15冻结Spec第10/11节要求NeedsReview到ConvertedToLead/Rejected/Expired单调转换，拒绝/到期采用各自持久行期限，不能默默将NeedsReview当Expired。当前读取Owner保留真实持久NeedsReview与原ETag，仅移除已到期动作；查询不负责状态写入。现有RetentionOwner处理持久PII截止，不能替代审核到期Owner。本轮**没有实现或声称通过Expired持久转移及其独立保留策略/worker**。这些余项保留WP03-03，本轮补齐的是原计划限定的实际期限拒绝/竞争与准确读取结论，不将纯predicate或静态断言冒充完整到期业务。

WP02-06保留其原整卡余项；该两组织会话场景只核销明确验收补项。WP03-03仍含完整到期终态及全原scope余项。原375卡仍26DONE/2IN_PROGRESS/347PLANNED，125完整目标和247正式验收不变。D1两张完整提供者已核销，WP02-10租户/连接采用关系待后续准确核对；七天/W249保持完成。

## 验证与采用

11 unique relevant PostgreSQL/HTTP scenarios passed,0 skipped: final8 review boundary/rollback cases,final1 browser login/switch case,and2 unchanged original decision cases from acceptance batch. Signed mock IdP/policy/business inputs;actual CRM OIDC/cookie/session/Owner/PG graph executed. Review/capability clocks controlled;IAM/retention/disclosure use real clocks. One author full SPEC/QUALITY review;no UI changes,Actions or deployment.

最终证据`D:/CP6-local-test/wave253/foundation/final-validation.json`逐项标来源：8项final-boundaries-20261102T014433Z、1项switch-final-20261102T014610Z；原图/原回执2项复用本轮acceptance-20261102T013343Z成功子集，该批另外的夹具失败没有改写为通过。保留所有RED和夹具失败；修正了测试信任发布顺序、旧session请求的no-store假设及替换具体身份客户端的错误夹具。新最终用例未skip。Web源码与合同未变，W122页面输入相同部分复用；不重跑全仓/UI/镜像。

CRM https://github.com/GTX537/CP6.CRM/pull/165 已准确采用源`8bd54612d85d2bb684b810e9c347b00247051cd0`到`6741882cfa4b500536fb3dd7749291e7a24f782a`。Main本窗仅协调台账/计划和四状态，随后正常PR与官方finish。没有Actions触发/取消、保护变更、既有环境迁移或生产部署。自有PG停止后保留数据和失败日志；固定席位复用，不新增工作树。

当前提交前快照：CRM分支/席位按官方finish核销，Handoff恢复原已保存分支补交付，Main待采用。最终三任务分支处置、工作区和席位释放见同轮PR及`C:/Users/tt/CP6-planning-reports/20261017-foundation/wave253-final-verification.json`；不提前声称完成。

CRM与Handoff https://github.com/GTX537/CP6.AI-Handoff/pull/229 均正常采用，准确任务分支已清理、DEV-2已释放；Main等待当前PR正常采用和finish。
