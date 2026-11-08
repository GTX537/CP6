# W322：租户已分类来源受控导出

W322完成Main.Privacy.TenantExport.v1：准确target tenant下已分类identity/security/roles及白名单租户元数据，明确用途/接收人、独立tenant能力、不可变制品/原操作/TTL；当前披露重查收件人/来源权限/绑定/期限/撤销和最终会话，逐源真实Manifest。没有伪subject，旧直接租户HTTP散流退役410；其他Owner全量导出和新租户载荷物理保留/处置及恢复仍为真实余项，整体PARTIAL。

Main基线 `48420d71829daeb955198351696cc29aec33545a`，START `090b37afdff8bd60b8d99139e9b3e5c20e4678e5`，COORD单人，固定DEV1/DEV2，无子代理或新增工作树。[范围合同](../../contracts/privacy-tenant-export-v1.md)。直接采用现有Main事务、当前平台会话、IAM共同根和最终会话，不增资格包装或逐层重复校验。

## 实际行为与边界

新入口 `/api/platform/privacy-tenant-exports-v1` 不以TenantId/Guid.Empty充主体。独立tenant请求、回执和私有schema，准确目标租户（含停用/过期目标）下全部已请求identity/security/roles行直接按TenantId筛选；不按用户名推关系。三个Owner字段沿已接受白名单，租户登记仅Id/TenantCode/TenantName/Enable/ExpireDate/TimeZoneId。密码、2FA secret、未知自由ContextJson/设备内容与其他未分类字段不带出。来源失败立即失败回滚，不以空数组冒成功。

每源实际数量、开始/完成读取窗、selector、字段包含/排除原因；真实0与未请求/未知分别表达。共享目录枚举保留旧主体wire/顺序/语义，租户Manifest无subject，现EF未接实体和明确非EF Owner以及未登记余项列出，始终PARTIAL/complete=false；不宣称跨Owner一致快照、全租户已覆盖或副本已清除。

tenant-read/export/download/revoke及三个tenant-scope能力分别授权，迁移不自动授角色。用途、requestRef、准确内部收件人及最长1440分钟访问窗显式确认；申请人/实际执行人来自会话。生成时收件人须为operator tenant中启用的平台用户。实际收件人下载时独立检查当前身份/权限、全部原来源能力、准确目标绑定、政策、TTL/撤销及最终会话；平台身份本身不代替业务能力。TTL只限访问窗口，不充法律保留期限。

真实载荷bytes、一次生成SHA/长度、Manifest、原命令及最小审计同commit；载荷与历史元数据分开。相同UUID返回原结果，异输入冲突，当前read可在撤export后接回原操作，NOT_FOUND不等无效果，显式关闭阻迟到生成。实际COMMIT答复丢失后只查原键，不再抓取或生成。事务内共用IAM根使原生撤权、撤销和生成/下载按真实提交顺序判定。内部下载信任本Owner已安装的不可变载荷，不重复重哈希；实际HTTP与Chrome下载从外部核对SHA/长度。

新schema没有W320主体载荷处置/恢复合同；物理删除、原生防回填及备份恢复适配仍未实现。撤销只关闭访问，已下载外部副本不会自动收回，丢失载荷明确410。旧整租户HTTP直接散流改410/no-store并指向新入口，旧服务内部实现和历史记录保留。主体导出、处置/恢复原入口与合同保留，新旧制品不互认。

页面单独显示准确租户/用途/来源/收件人/TTL、真实回执与Manifest；仅存身份分区UUID并用navigator.locks协调标签。取消零请求，不能写存储不先发，身份变化清理迟到数据。生成回包丢失重载接回原结果，已知回执在当前状态读取失败时仍显示且没有旧下载/撤销动作；撤销回包丢失同样保留原事实，再读准确当前状态。原申请人/执行人/捕获时刻直接显示回执字段。

## 实际验证和审查

11项新增实际PostgreSQL18场景、5项受影响主体导出回归（含新增两人Chrome实际下载与既有主体Chrome）、18项Vue行为及vue-tsc通过。 逐场景来源 `D:/CP6-local-test/wave322/foundation/final-validation.json`，计数仅为实际记录，不作为目标。隔离新PG18.6，业务用户/租户/政策数据明确模拟；真实Main Program登录/CSRF/当前权限/HTTP、原生数据库并发及Chrome双登录实际执行。覆盖跨租户准确范围、秘密及未知内容排除、停用空租户实际0、未请求与未知、失败来源、准确收件人/当前撤权/TTL/最终会话、审计回滚、实际COMMIT丢答、原UUID/关闭、跨主体模型不互认以及原生竞争两顺序。

两人Chrome实际下载JSON后核对文件SHA/长度；申请人无收件下载资格，收件人确实取得文件，随后实际撤销后的服务器GET410。生成/撤销答复均刻意丢失，原结果按准确键恢复，取消无写，三张截图已查看。最小权限夹具其他分面403及既有导航i18n key原样保留，不宣称全站UI无问题。初始旧HTTP200红测保留；准备脚本首次在写入前失败，提前启动的同一旧行为测试仍200也保留；修正准备后真实迁移/接口和定向复测通过，没有将失败改成通过。相同输入成功证据按适用范围复用；最后只增原回执三项显示文字，集中检查diff和现有类型定义，不为提交/SHA变化重复行为测试。

COORD一次任务级SPEC/QUALITY集中自审覆盖完整分支差异：准确TenantId与operator tenant边界、实际平台会话、显式独立能力且无自动授予、准确启用内部收件人；三个Owner字段投影及未知嵌套/秘密排除、真实0/未查询/未知来源区别、逐源读窗和非全局快照；命令/制品/最小审计同事务、原UUID冲突/关闭/COMMIT未知；当前收件人/所选来源/TTL/撤销和最终会话，原生撤权/撤销与披露两顺序；私有独立schema无伪subject且不互认主体制品；新载荷没有物理处置/恢复Owner的边界明确，不套用旧主体Owner；严格JSON/旧入口410/no-store；页面UUID身份分区/标签锁、取消零发送、身份变化丢弃迟到结果、已知原回执与当前访问状态分开。无未解决阻断项。

## 目标和生命周期

原375=36DONE/2IN_PROGRESS/337PLANNED，原59=12闭环/47剩余，125目标/247正式验收不变。WP02-15仍余未分类安全内容/不可归属事件、授权/组织引用、OA/PLM/CRM业务历史、FILE及其他来源副本、整租户全部Owner导出、新租户制品受控物理保留/处置及恢复、WAL/备份/外部副本和完整环境恢复。W306、W249及已闭环FORM卡不重复；24h继续相邻真实余项，W310仅预算末尾。

开工累计72040秒，交付采样74946秒，约定24h仍active/incomplete。低置信初估1.75–3h，最终实测以官方finish证明为准；20h已校准，下一79200秒校准。连续OS monotonic保留先前恢复段，四日暂停不计，不加原生面板计数，不空等凑时。

本提交时正常Main/Handoff采用、准确任务分支删除和官方finish待执行；分支处置、工作区状态和席位释放以 `C:/Users/tt/CP6-planning-reports/20261017-foundation/wave322-final-verification.json` 最终核销。无Actions、工作流/依赖/保护变更、生产部署或既有数据库迁移/处置/恢复。本地模拟业务验收不充生产候选或环境推广。
