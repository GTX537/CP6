# W323：租户导出载荷的真实受控处置

W323完成Main.Privacy.TenantExportPayload.v1当前库准确租户载荷处置：明确保留政策/独立申请批准/当前最终执行、全目标租户所有主体hold与ABA、真实字节删除/撤访问/最小原回执/墓碑同commit及原生防回填。无伪subject。新墓碑推进restore_epoch，未采用的新Owner恢复明确503并保持候选隔离，历史已提交原结果仍可恢复；不冒充备份或外部副本已清。

Main基线 `2f4eaab416eac5eff12cda327cdb9f76262ac290`，START `552cb4e95e8cf47b9ba241d5998988c492307256`。COORD单人，复用DEV1/DEV2，无子代理/新增工作树。[本窗合同](../../contracts/tenant-export-payload-disposition-v1.md)。直接采用既有事务/IAM根/平台会话/当前权限/最终会话与Main原命令协议，不加资格或重复证明层。

## 效果与边界

每计划准确ArtifactId，独立tenant scope/schema没有SubjectId；以同operator tenant原制品的准确TargetTenant、原SHA/长度/用途/可靠UTC CapturedAt和payload revision冻结。政策有明确依据、目标、版本、启停、0–36500个UTC连续最短保留天数和是否要求先关闭访问；模拟政策不充法律期限。六项tenant-payload能力分别授权，不自动授角色。申请人不能自批；当前政策/hold/批准者资格/最终会话在真实执行点重判，政策ABA不恢复旧批准。

整份租户副本采用保守规则：准确目标租户下任一主体当前有效hold都阻删除，跨operator tenant生效，复查到期仍有效。按每主体最新事件判当前，只显示最新全目标事件版本、有效/逾期计数；目标任一hold变化改变HeadVersion，解除后旧批准也失效。沿既有主体hold入口管理，没有新hold事实源或伪租户主体，其他目标hold不阻本份载荷。

最小实际执行/墓碑/原命令与真实DELETE及关闭访问同commit，原制品/Manifest/审计/历史元数据保留，回执记录真实RemovedBytes且不复制原PII。实际COMMIT答复丢失只按原UUID接回，撤execute后当前read仍可恢复；异输入冲突、关闭阻迟到。原生写者加入同IAM根，未所属删除/TRUNCATE、已墓碑回填拒绝，DELETE也判当前整租户hold。延迟约束拒孤立执行、缺原命令、未删字节或未关访问；静默跳过DELETE在实际COMMIT拒绝为已知409，完整回滚，不能冒UNKNOWN或成功。缺原生存储HTTP明确503；再次撤销已处置制品准确返回DISPOSED。

本窗没有备份恢复适配。新政策/墓碑推进现restore_epoch；有新Owner墓碑时恢复预览、正向审批及新执行在候选访问前明确PRIVACY_RESTORE_TENANT_PAYLOAD_OWNER_UNSUPPORTED/503。原候选批准失效，既有已开放候选的源证明过期而重新隔离。历史已提交结果/双库UNKNOWN责任仍可按原协议接回，不能因此开放旧源版本。没有写NoOp恢复或伪subject Unit；无此Owner墓碑时旧恢复wire/摘要/原效果保持。WAL/备份/外部已下载副本、其他业务资料仍未因此清除。

新API分面privacy-dispositions-v1/tenant-export-payloads与页面显示准确范围、真实字节/状态、聚合hold、独立审批/实际执行及未接恢复范围；仅持久化身份分区UUID，用标签锁协调，取消不写/存储失败不发/身份改变丢迟到。已知回执在原结果接回或手动计划刷新失败时仍显示且没有旧执行动作。

## 实际验证和审查

14项新增实际PostgreSQL18场景、13项受影响租户导出及主体载荷/安全字段恢复回归（含新增处置与既有租户下载Chrome）、23项Vue行为及vue-tsc通过。 逐场景来源 `D:/CP6-local-test/wave323/foundation/final-validation.json`，计数仅记录实际运行。隔离新PG18.6、真实Main登录/CSRF/当前权限/HTTP及两人Chrome；用户/租户/政策与故障输入明确模拟，内部事务和原生效果真实执行。覆盖准确字节删除/原回执、当前撤权/独立批准、政策与hold ABA/逾期/跨operator、原生竞争两顺序、审计/会话回滚、实际COMMIT丢答和已知拒绝、缺存储503及旧导出/恢复采用。

新增两人Chrome实际删除111343字节（本次生成文件的实际大小），取消零写、一次申请/一次执行，执行答复刻意丢失后重载接回原结果，计划答复丢失仍显示回执，后续实际下载410，两张截图已查看。最小权限夹具其他分面403和既有导航i18n key原样保留，不宣称全站UI无问题。真实pg_dump/pg_restore创建两个候选：无新墓碑时旧流程可实际开放；新墓碑后旧开放证明失效、新批准作废/预览执行503，候选字节仍在且隔离，没有冒充已经重施处置。

初始无所属DELETE红测、跨租户夹具缺平台身份和审查发现重复撤销状态红测均保留。错误将新墓碑与旧恢复回归放进同一源的批次已取消；仅停止准确所属测试进程，隔离PG18经pg_ctl正常停止，原日志/非零退出保留，无TRX结果不充通过。随后兼容恢复用独立源运行；相同输入成功证据按真实范围复用，不因提交或SHA重跑。一次COORD任务级SPEC/QUALITY集中自审覆盖完整分支差异：准确环境/operator tenant/制品和新独立无subject身份；明确政策/原可靠UTC捕获时刻/期限、原行修订、独立批准与当前能力；所有目标主体最新hold聚合/跨operator/逾期/ABA及原生竞争；最小实际执行/墓碑/原命令/准确字节删除/访问关闭同commit，原生无所属DELETE/TRUNCATE与回填及孤立效果拒绝；原键COMMIT未知/关闭及当前会话失败原子回滚；当前DISPOSED与历史撤访问区分、丢失存储503不静默成功；新政策/墓碑源版本与恢复前快速失败，历史UNKNOWN恢复责任保持；严格JSON/no-store、页面UUID身份分区/标签锁/取消零写/迟到清理及已知回执与计划刷新分开。审查发现已处置后再次撤销误回REVOKED，真实红测后改DISPOSED并仅定向回归；没有未解决阻断项。

## 目标与收尾

原375=36DONE/2IN_PROGRESS/337PLANNED，原59=12闭环/47剩余，125目标/247正式验收不变。WP02-15仍余租户载荷恢复实际适配、未分类安全内容/不可归属事件、授权/组织引用、OA/PLM/CRM历史、FILE及其他来源副本、整租户全部Owner导出/处置、WAL/备份/外部副本和完整环境恢复。W306/W249/七天及已闭环FORM不重复；24h继续相邻真实余项，W310预算末尾。

开工累计75502秒，交付采样77196秒，原24h仍active/incomplete。初估1.75–2.75h低置信，实际以官方finish证明核销；下次79200秒(22h)校准，较快则接相邻真实余项，不空等或重开周期。

本提交正常Main/Handoff采用、准确分支清理及finish尚待执行。分支处置结果、工作区状态和席位释放以 `C:/Users/tt/CP6-planning-reports/20261017-foundation/wave323-final-verification.json` 最终核销。无Actions/工作流或依赖变更/保护变更/生产部署/既有数据库迁移或处置；本地模拟业务验收不充生产候选。
