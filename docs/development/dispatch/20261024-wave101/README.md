# W101 Main真实询盘配置权限签发

COORD本聊天单人，七天持续目标ACTIVE。Main `20bb1b7bce926a218fce5bb9c59aba7ccd5f17f0`、CRM `cab16a031c93d52da674f20984a2d503d35fc2a8`、Handoff `7ec07d382c98942481cea90f4a80aa6f4222d558`已核远端；最新AGENTS/POLICY哈希未变，官方status/capacity通过，三席位空闲，用户原有七项未跟踪资料保留。W100三仓已完整合并finish，173项相关检查、原事件重放通过，实际25.96min＋前置约10min；本轮只登记其最终CLOSED证据，不重复实现。

## 直接前置与实施范围

W100提供真实Main当前角色精确Site/Form来源与CRM当前消费；W99提供受保护CRM原会话，W87/W59提供持久信任与冻结签名校验。现在补实际Main签发入口，不能再用测试自签投影作为真实生产者。

Main新增显式启用的 `/connect/inquiry-permission` POST，由现有CRM联邦Bearer（CP6.Web at+jwt）绑定实际主体/tenant，不读取请求自报身份、动作或密钥。PostgreSQL authentication根内重新读取当前会话/用户/角色动作与精确pair；原grant行锁保护本次签发读取，await后重新核有效期，输出只在外层事务成功后返回。只接受配置read/write/preview/activate用途及一个Site/Form，按原授权矩阵要求query或query+configure；fields=[]、不表达线索部门/PII/监督授权；activate仍不等Owner批准。

冻结CRM-INQ RECOVERY-R3V15 PermissionProjection v1使用分离siteIds/formIds，本轮每份仅签一个已授权精确pair，不能把多pair展平为笛卡尔积。按原规范化JSON键序、纯Ed25519、issuer/key/catalog/tenant/subject/purpose和最多5min时效产生真实字节。原v1的UUID表示边界明确遵守，不改写任何Main持久ID、不缩窄W100/C2合法存储；无法无损表示时明确拒绝签发，后续合同版本扩展另行处理。

使用明确宿主配置的私钥/公钥匹配、issuer与实际OIDC来源绑定、keyId/生效窗口/撤销状态；默认关闭，不生成生产密钥或部署信任。沿用CRM已经验证的精确NSec25.4.0/libsodium1.0.20.1；Main仅添加需要的引用。CRM以现有严格schema/真实Ed25519及持久PG信任验证Main实际输出，当前源码和冻结输入不被重造。目标是实际生产者的代码与跨仓接收，不宣称完成配置Owner或跨仓即时撤权；这些仍是七天后续直接任务。

## 任务和时间

- W101-01 Main，DEV-1 codex/w101-01：实际配置签发HTTP/当前权限读取/密钥配置、相关PG/HTTP验证及总台账四状态。
- W101-02 CRM，DEV-2后续官方bind：实际Main输出的冻结合同/真实验签消费检查；按需要保存最小相关测试，不改生产信任配置。
- W101-HANDOFF，同一原分支保存一次START、稍后交付并正常合并finish。

实际预计65–100min：Main35–50、CRM/相关验证20–35、审查/三仓收尾10–15；独立前置约5min。Main START `2026-10-24T05:32:28.557106+00:00`，最终按Main finish计，CRM/Handoff不重复相加。继续现有七天目标，不另建较小目标。

定向验证真实签发/篡改/用途和主体错绑、当前动作/精确pair/停用撤销、原token/会话到期、锁等待后的重核、失败不返回投影、密钥缺失/错配/未生效/撤销及未知请求字段；既有签名与OIDC行为按实际影响复用或局部回归。一次集中SPEC/QUALITY，保留真实失败。正常PR、远端采用、准确分支处置和官方finish；固定目录复用，PostgreSQL唯一新增目标，不运行Actions/既有环境迁移/生产部署。24/375、0/125、247正式NOT_RUN不变。

## W101实际交付与收尾

Main新增实际 `/connect/inquiry-permission` POST，沿用真实CP6.Web at+jwt、已消费/未撤销/未过期grant、来源登录及refresh family；主体/tenant只取服务器真实记录。PostgreSQL authentication根内重读当前启用角色/动作和W100精确Site/Form，原grant行FOR SHARE，await后再次核验同一会话与到期；成功commit后才返回。单份只含一个已授权精确pair，配置read要求query，write/preview/activate要求query+configure；fields=[]、managedDepartmentIds=[]、isSupervisor=false，不扩大为线索/PII/部门授权或Owner批准。

显式配置启动的纯Ed25519签名绑定原OIDC issuer、keyId、catalog1和最多5分钟/原token到期较早值；规范化字节按冻结合同排序。默认关闭；缺失、错配、未生效、已撤销或已退役密钥拒绝。冻结v1只表达UUIDv4，其他Main合法持久ID保留，签发时返回422，不改写身份、不缩窄W100/C2存储。本轮无新DB迁移。精确NSec25.4.0/libsodium1.0.20.1及两份锁文件采用CRM已验证版本；本地恢复保留Main原signatureValidationMode=require、trustedSigners和sourceMapping。

CRM [PR128](https://github.com/GTX537/CP6.CRM/pull/128)已采用 `dc09f558d6070a807c59316adce50b35641d8ba6`，source `fad3bf02776db82fc5eccdd9320ea3bf30f88be3`，[单份交付](https://github.com/GTX537/CP6.CRM/blob/dc09f558d6070a807c59316adce50b35641d8ba6/docs/development/evidence/W101-02/DELIVERY.md)。实际Main登录、OIDC两段授权、PKCE换票及签发返回原JSON，CRM直接用冻结schema、真实Ed25519、持久公钥信任和真实PG时钟验证；无私钥、无重签、无改时间。跨主体/tenant/purpose、篡改form和公钥撤销后原文均拒绝；CanAuthorize=false。未修改CRM生产合同/宿主/信任或环境source注册。

152项唯一相关本地检查通过：Main134（23项新签发/当前PG/HTTP行为＋111项既有OIDC相关回归），CRM18（17项持久信任＋1项实际Main原HTTP输出）；0跳过。 证据 `D:/CP6-local-test/wave101/foundation/final-validation.json`、所列原JSON/TRX/log；Main首次22项21通过/1失败因测试未跟随原OIDC桥接跳转，第二次2项1通过/1失败因测试把原CSRF403预期为401。修正测试后实际HTTP通过；没有改认证或CSRF保护。原失败保留；已通过且输入未变结果复用，未全仓重复验证。Main真实role撤销在原PG根前阻塞由pg_blocking_pids观察，等待后拒绝；提交确认丢失不返回投影。集中SPEC/QUALITY核对源码与合同、真实权限/会话/范围/键窗、失败和依赖，最终diff/状态/链接检查通过。CRM自有PG55446已校验身份后smart stop，Main临时PG和HTTP进程也已退出，保留数据日志。

**边界**：这是Main当前时点的已签权限证据，不是跨库业务commit锁。CRM仍须从当前身份/撤权来源及其真实Site/Form实体取交集并完成Owner最终事务；五分钟TTL不能代替跨仓即时撤权。配置activate签名也不等于Owner审批。真实配置保存/公开提交尚未执行；没有部署生产密钥、发布生产公钥、升级现有Source或迁移环境。24/375整卡、0/125完整目标、247正式NOT_RUN不变，七天持续目标ACTIVE，接续配置Owner和最终授权边界。

Handoff [PR](https://github.com/GTX537/CP6.AI-Handoff/pull/77)已采用 `b997999715477656dfa94fb36e4a7b5e744f8a02`，准确分支删除、固定席位干净detached并finish释放。Main本次PR/finish继续；当前实际22.45min，原估65–100min＋独立前置约5min；最终按Main START到finish计，不叠加CRM/Handoff。

## 宿主配置与使用边界

`InquiryPermissionSigning`默认Enabled=false。显式启用需要Issuer（与已启用CrmOidc.Issuer相同且满足冻结https://iam.*）、KeyId（8–80安全字符）、PrivateKeyBase64（32字节Ed25519原始私钥的规范Base64）、PublicKeyBase64（匹配32字节公开密钥）、ActiveFromUtc（UTC）、可选ActiveToUtc及Revoked。密钥由宿主秘密配置供给；不提交到仓库，不在请求中提供。本版本配置在启动捕获；变更需受控重启，接收方另以持久信任publication执行公钥轮换/撤销。没有自动生成密钥、自动信任请求内公钥或生产配置变更。

可信HTTPS（或现有明确登记的TLS代理）POST JSON只含 `purpose`、`siteId`、`formId`；使用原CP6.Web Bearer。用途仅configuration:read/write/preview/activate。未知字段400，缺少/失效Bearer401（无显式Bearer的unsafe请求仍先受原CSRF403保护），缺少当前动作/精确pair403，冻结v1不能表达原ID时422，权威/键窗不可用503，未启用404。响应no-store。查询到的范围不证明CRM对象存在或属于指定站点；这由真实Owner核对。

复现用Main测试 `Inquiry_producer_actual_HTTP_login_OIDC_signing_rejects_spoofing_and_current_revocation` 输出 `CP6_INQUIRY_PROJECTION_OUTPUT`，接收端按CRM单份交付在原有效期内消费；全部是自有隔离测试数据，非生产业务验收。
