# W128-01 当前真人隐私导出与下载来源交付

Main新增privacy:export/download实际用途；两者均要求crm-lead:query、crm-lead:view-pii和crm-privacy:export。当前主管来自crm-lead:assign，健康管理部门取lead/privacy两个实际资源范围的交集；精确Site/Form由当前角色范围检查。无部门、撤权、角色失效或超过256线格式限额均拒绝，不截断。

前六个冻结PII字段从crm-intake完整可信来源读取，exportArtifact来自crm-privacy。MainInquiryPrivacyExportFieldsV1和MainInquiryPrivacyDownloadFieldsV1分别声明同用途映射，使用完整当前有效角色的stored-MIN后注册默认；没有先逐角色填默认，也不借用intake的Readable结果。Access2仍禁止写入，但在独立导出/下载动作、来源范围与用途条件均成立时可提供相应用途来源事实；任一隐藏字段拒绝。计算只针对来源投影，实际PII状态、Submission/job/artifact/proof和最终事务权限由CRM Owner负责，未用来源替代最终业务授权。

新版privacy目录精确为consent/exportArtifact。Consent兼容旧精确consent目录和新版精确目录，仍独立要求Access1；隐藏artifact不影响合法撤回，不接受任意额外目录或缺失/未知存储字段。Program只登记目录，没有授予实际角色动作或扩大数据范围。原认证根、前后Bearer检查、短期签名以及全部角色/用户来源保持。

CRM明确新增导出创建、导出状态只读、下载消费三种原会话捕获。创建和下载均保留CSRF（下载虽为GET仍是消费意图），只读状态不能写入。CRM验证器补齐两个精确用途，外内purpose、nonce/epoch、租户主体、站点表单、所有角色来源、当前信任和原事务绑定保持，CanAuthorize=false。没有新增业务路由。

72项唯一相关本地检查通过：Main45项当前来源/兼容/PostgreSQL/真实登录OIDC/HTTP，CRM27项保护会话/当前来源/原始Main互操作；必需0跳过。

Main成功来源main-source-20261025T054221Z37项、main-cross-export-20261025T054523Z6项、main-cross-download-20261025T054618Z1项、main-consent-compat-20261025T054757Z1项；后加的5项目录/超量范围边界在cross-export构建执行，未冒充先前快照执行。CRM来源详见[配套报告](https://github.com/GTX537/CP6.CRM/blob/c7ad3237490e03ea567897d521bef6234c739707/docs/development/evidence/W128-02/DELIVERY.md)。final-validation.json按唯一名称保留成功来源，两个用途相同互操作测试都实际运行但不重复计数。

最初一次测试缺少using未执行行为；修正后3项预期Main失败证实缺少两个用途和Consent目录兼容。CRM初始3个正常路径被原白名单拒绝，修正后10项相关用途重新通过；初批14项未受影响Consent成功按实际范围复用。所有失败日志/TRX保留，未伪造成初次通过。

跨仓采用实际Main登录/OIDC/HTTP产生的原始签名字节与原始已发布payload，没有重签、改时间或伪造成功。CRM原始来源检查的inspection actor是明确本地夹具；具体保护会话由独立TLS/HTTP/PG用例验证，两类证据不能合称真实业务导出E2E。没有Web改动/浏览器验证、既有环境迁移或生产部署。

一次任务级SPEC/QUALITY审查通过，公共线合同、依赖、迁移/模型和工作流未变。工作流当前引用与已审查W118内容一致、实际路径未命中自动事件，Actions零。自有PG55455及Main夹具正常停止，数据和缓存保留。

CRM [PR153](https://github.com/GTX537/CP6.CRM/pull/153)采用sourcec7ad3237490e03ea567897d521bef6234c739707到mainf96de22c2df0f79f695890c4efb956e2d3cbf5a4；Handoff [PR104](https://github.com/GTX537/CP6.AI-Handoff/pull/104)采用source261ce3861278bbd06aafa766fbba7c7c32963598到目标10348f2d4407d3b47621b3539bc43e7629984bea，两仓官方finish确认准确本地/远端任务分支删除和席位释放。固定DEV-2回到Handoff留作复用。Main最后合并/finish尚待，以wave128-final-verification.json核销；下个登记窗口统一更新本提交快照。

原估55–90分钟，截至准备53.07分钟，最终按Main START至最后finish计一次，跨仓不叠加。原生导出任务、加密制品/生成权限、当前单次下载与实际内容、页面和Outbox发布仍未交付；七天原计划出口先逐项审计，再决定必需下一窗口，不以来源完成冒充隐私业务完成。

2026-10-25用户问进度与估时：当前正式七阶段整阶段关闭1/7（14.3%），不是代码工作量比例；24/375整卡（6.4%）、完整目标0/125；247NOT_RUN+41REFERENCE_ONLY。累计至W127登记65.53h含等待/暂缓已超原35–55h初估。剩余30–50h只是出口逐项审计前的低置信粗估，不是按阶段率外推或新完成承诺。持续目标ACTIVE，完整范围保持。
