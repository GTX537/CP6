# W128 人工隐私导出与下载的当前权限来源

COORD本聊天单人，执行既定计划；七天目标ACTIVE。W127 Main540/CRM152/Handoff103已正常合并、准确分支删除并官方finish；登记105.78分钟含真实60分钟保留期。累计登记65.53小时含等待/暂缓，不是纯编码时间或完成比例。Main4cf39065、CRMbb5edc19、Handoffa953c39已核对；不重复W127。

目标：补齐冻结privacy:export与privacy:download的真实Main来源和CRM具体受保护会话接入。架构：Main在现认证根内读取完整当前角色/动作、独立字段用途映射和健康部门交集，并签发现有human-context；CRM仍由原会话/CSRF、在线nonce/epoch和同事务当前证明消费。该来源不能单独授权Submission/job/artifact或最终提交。技术：PostgreSQL、现有C#/EF/Npgsql和Ed25519合同，无新增依赖、模型迁移或工作流。

|任务|预计实际执行时间|
|---|---|
|Main当前动作/字段用途/部门来源与撤回目录兼容|20–30分钟|
|CRM显式导出读、导出写与下载消费身份入口|10–20分钟|
|实际PostgreSQL/HTTP/原字节互操作及一次集中审查|15–25分钟|
|三仓正常合并、准确分支删除和官方finish|10–15分钟|

合计55–90分钟，从Main START 2026-10-25T05:06:01.870333+00:00 计一次。DEV-1/Main、DEV-2/Handoff→CRM→Handoff官方bind串行复用，PG55455为本轮隔离CRM测试；Main沿用隔离PG测试宿主。普通编译/测试本地，成功输入未变复用，不启动Actions。

实施顺序和准确文件范围：

1. Main新增CP6.WebApi/Services/InquiryPrivacyPermissionSourceV1.cs与明确privacy字段目录。修改InquiryPermissionIssuerV1.cs、InquiryHumanPermissionContextV1.cs支持两个精确用途；都要求crm-lead:query、crm-lead:view-pii、crm-privacy:export。主管仍为当前crm-lead:assign，健康管理部门取lead与privacy实际范围交集，超过线格式上限拒绝。
2. 七个冻结字段为contactName/email/phone/company/need/riskCategories/exportArtifact。前六项来自crm-intake实际完整字段来源，exportArtifact来自crm-privacy；按Export和Download分别提供版本化Owner映射与用途条件，不能把已有Readable列表直接重命名为导出授权。stored-MIN后注册默认语义保持；Access2不授予写入，但导出/下载仍按各自独立门判断。对象具体PII状态、artifact/proof和最终当前门留给原生业务Owner，Main只签来源事实。
3. Program.cs注册明确包含consent与exportArtifact的新版privacy目录，不预授予动作、角色或范围。InquiryConsentPermissionSourceV1保留独立consent可写语义，并明确兼容既有仅consent目录和准确新版目录；未知字段/缺映射仍拒绝。验证artifact隐藏不会改变独立撤回授权、旧版撤回目录仍可用。
4. CRM src/CP6.CRM.Api/Authentication/CrmCommandIdentityV1.cs新增PreparePrivacyExportIdentityAsync(session,csrf)、PreparePrivacyExportReadIdentityAsync(session)、PreparePrivacyDownloadIdentityAsync(session,csrf)。下载虽为GET仍是单次消费写意图，不能绕过CSRF；三入口CanAuthorize仍false，独立purpose与ReadOnly标记，原身份/信任源完整性保留。
5. Main CP6.Tests/Platform新增IamInquiryPrivacyPermissionPostgreSqlTests.cs，关联当前producer HTTP夹具；CRM原生IdentityPostgreSqlTests新增或扩充human-context测试。覆盖缺各动作、隐藏/未知字段/缺映射、完整多角色stored-MIN、健康部门与范围交集、最终Bearer变化、读写/用途混用和当前撤权。真实Main登录OIDC/HTTP签名字节进入CRM PG验证，夹具边界明确，不拼为业务生产E2E。沿用现有run_wave126_main.py/CRM脚本参数方式改成本轮目录，先定向构建/用例，失败定位后只重跑受影响范围。
6. 两仓各一份交付及四份状态更新；Main同步roadmap/生成进度页/本计划。一次完整SPEC/QUALITY审查，正常PR、远端准确采用、条件分支删除、官方finish及最终审计。

完整WP03-04仍PLANNED/NOT_CHECKED，正式验收NOT_RUN；原生导出任务、实际加密制品/生成服务权限、单次下载与内容传输、页面及Outbox发布紧随后续采用，不以来源完成冒充文件交付。冻结FX25-S06同时描述元数据与明文传输，后续必须一致验证哈希/长度和绑定。无生产、既有环境迁移、自动授权或合同输入缩窄。正式24/375DONE、2IN_PROGRESS、349PLANNED；0/125；247NOT_RUN+41REFERENCE_ONLY保持。

## 交付准备与后续收口

72项唯一相关本地检查通过：Main45项当前来源/兼容/PostgreSQL/真实登录OIDC/HTTP，CRM27项保护会话/当前来源/原始Main互操作；必需0跳过。 [单份报告](../../evidence/W128-01/DELIVERY.md)。CRM153/Handoff104已采用并官方finish；Main正常收尾继续。原生导出任务、加密制品/生成权限、当前单次下载与实际内容、页面和Outbox发布仍未交付；七天原计划出口先逐项审计，再决定必需下一窗口，不以来源完成冒充隐私业务完成。
