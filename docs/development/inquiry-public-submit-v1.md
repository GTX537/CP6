# 公开询盘提交与服务上下文 v1

W117，PostgreSQL 原生询盘宿主的明确采用窗口。此版本提供实际公开 POST/H01 Owner，默认关闭。公开页面提交交互、后续线索管理/隐私 Owner 对新存储格式的采用、Outbox 发布者注册及真实环境验收仍分别推进；本说明不触发安装、迁移或部署。

## 请求、身份与授权

公开入口为 `POST /api/site/{siteKey}/forms/{formKey}/submissions`。使用原合同的 Origin、X-CSRF-Token、Idempotency-Key 和 attempt cookie；无需新增 If-Match、AttemptId 头或身份参数。可信部署路由决定租户/站点/表单，浏览器 cookie 与 CSRF 定位原始签发记录，Idempotency-Key 是独立请求键。BFF inquiry-v2 签名绑定方法、原始路径、正文、浏览器头和可信客户端摘要；禁止公开 Authorization/X-Permission-Projection。请求上限128KiB容纳全部合法 Unicode 字符，不把 BFF 摘要假装为 IP 地址。

Main新增 `/connect/inquiry-service-context`，请求 purpose/siteId/formId/32字节规范Base64Url随机nonce；未知字段拒绝。必须真实HTTPS或既有可信TLS终结。Basic凭据来自CRM服务器配置；Main沿用实际冻结注册、认证根锁、已提交服务来源、租户与当前IAM权限。同一次事务签出来源绑定上下文，提交失败/未知不得返回。

独立合同 `contracts/inquiry/service-permission-context/v1/schema.json`，schemaVersion为 `cp6-inquiry-service-context-v1`。issuer/key/catalog/client/tenant/subject/epoch/registrationRevision/sourceVersion/源数据SHA/合同bundleSHA、全部注册角色的原始事件版本与SHA、站点表单、nonce及有效期均纳入签名。intake:release包含未经扩展的冻结R3 PermissionProjection；public:receive仅含服务身份，permissionJson必须为null，不能授权H01。签名是Ed25519，字节为UTF-8版本名加换行、再加去除根signature的JSON；对象键按Ordinal递归排序，数组顺序保留，System.Text.Json默认字符转义，整数精度不经浮点。上下文最长5分钟，固定UTC七位小数时间；CRM使用数据库时钟及持久公钥信任，核对两层签名和请求nonce。

CRM在线获取在业务锁之前，拒绝重定向、Cookie、缓存、非UTF-8/非JSON、过量正文和超时。在最终原生Serializable事务内核对当前服务/全部角色来源、原七把身份锁、实际xid、注册修订/epoch及持久信任。正常提交需要intake:release的query/view-pii/add、六项字段、supervisor、管理部门、精确站点/表单对，以及当前C02动作/assign/部门范围；隔离待审使用public:receive，不伪造业务权限。Main字段策略变更即使没有同名crm-intake动作，也在同一事务推进对应角色C02版本与Outbox，回滚不发布撤权。

## 完整事务和重放

原始IssuedAttempt保持不可变；新增独立消费行记录实际consume，不添加改变冻结原图v4目录摘要的入向外键。稳定原操作键为租户、实际服务Actor、操作、资源、请求键HMAC；PayloadHash只作比较，不能把不同payload变为新请求槽。

同一数据库连接/事务写完整原Submission、PublicAttempt、历史、ConsentFacts/ReceiptProofs、可配置策略保留期和正常H01的完整Lead/历史/回链，再写消费、原结果、审计与Platform原生Outbox。LeadNo按原来源允许NULL，没有另造编号策略。提交前再次检查当前授权/时限；延迟约束在commit核对完整原图、原结果、时限、审计和实际Outbox负载/绑定/路由。429只提交限额桶/退避；已知40001/40P01/消费主键竞争有限重试，未知提交不自动重执行。

重放先检查真实浏览器证明和当前回执/提交终态，再比较规范化payload与稳定请求键。有效原结果返回相同201/body/ETag并恢复cookie，不再次请求Main、扣额或执行H01。回执撤销/过期、attempt tombstone和PII终态返回404；原结果脱敏、保护绑定失效或当前ETag不符返回409 recovery_pending。完整PublicAttempt保护绑定包含精确FormVersion，排除旧Int32访问别名与数据库rowversion；不得通过别名截断大数。结果保护失败不暴露受保护内容。

## 存储版本、显式安装与运维前置

复用完整原图v4的PostgreSQL bytea/精确数值表示。新询盘联系列使用 `CP6.CRM.Inquiry.Columns.v1` 独立DataProtection用途，绑定租户/行/表/列，保留合法UTF-16源字节，长度按原R3 Unicode标量上限验证；不改写旧CRM2信封，也不冒充旧candidate codec。新原图完整性域为 `crm-inquiry-full-graph-v1/<table>`。新列解密API为Protect/UnprotectInquiryColumnV1；旧业务Owner和清理器尚未自动采用新格式，禁止把旧codec直接接到此宿主。独立原生Program分支未暴露旧线索业务路由；此边界必须在后续管理/隐私Owner接入时显式处理。

启用之前必须已有原生身份/持久信任/原图v4、公开签发/限额和配置Owner显式安装，当前C02服务来源已发布，真实Site/PublicForm及配置采用已完成。使用新 `InquirySubmissionLedgerV1.InstallAsync` 显式安装 `crm_inquiry_submission_v1`；宿主启动只检查，绝不执行DDL。已有同名不匹配模式拒绝，不自动修复或重新封印v4目录。

运行角色增加新schema USAGE与表SELECT/INSERT；原结果脱敏Owner的Commands UPDATE另行授权，immutable/guarded触发器保留。`hold_issued(uuid,uuid)`仅提供原始签发记录行锁，SECURITY DEFINER、固定pg_catalog search_path、owner一致并撤销PUBLIC EXECUTE；运行角色只获得该函数EXECUTE，不获得IssuedAttempts UPDATE。保留既有原图、限额、配置Outbox和服务检查最小权限。就绪检查核对该函数正文、owner、ACL、触发器、稳定主键与延迟约束。

CRM配置 `CrmInquiryNative:PublicSubmissionsEnabled=true` 必须同时满足PublicReceiptsEnabled、OriginalGraphWritesEnabled、明确PublicForms和每租户唯一ServiceClients（TenantId/ClientId/ClientSecret）；Secret由部署密钥通道供应。Main沿用W116 InquiryServicePrincipals显式发布注册及InquiryPermissionSigning配置。生产配置本轮未开启，未安装既有环境、未授予生产身份。

Outbox使用现有Platform物理消息/lease表，独立owner=crm-inquiry-submission、queue=submission-owner-v1、route=submission-v1。保存时Phase=Blocked、publisher=unassigned:submission-owner-v1；实际原冻结submission.received/lead.created事件与审计因果、租户/聚合及payloadSHA同事务保留。后续发布者采用与投递恢复为独立窗口，本轮不声称已发送。

## 本地证据与边界

实际Main PostgreSQL+TLS控制器与独立CRM PostgreSQL进程已接通，使用测试自有证书、注册和数据库；服务认证/发布/签名/验签/当前来源/业务提交均为产品代码。配置准备使用明确标注的测试人类会话，未声称生产数据或完整部署宿主验收。定向测试覆盖正常/待审/最大Unicode与大数版本、冲突重放、竞争、响应丢失、撤权/错误来源、末尾事件失败、到期与取消回滚。失败日志保留，最终适用结果和分支/席位收尾见W117单份交付报告。正式125目标/247验收状态不据此改为通过。
