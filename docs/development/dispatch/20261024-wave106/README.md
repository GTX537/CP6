# W106 公开表单读取与持久浏览器绑定 Attempt

本聊天COORD单人沿用writing-plans / executing-plans，七天持续目标ACTIVE；不另开代理、聊天或定时任务。

## 前置与范围

最新Main `da0ca5bf2ba11fe22fda170336f222d7235ceadb`、CRM `14ca04883e5aae7b856b13674d00b6777d1567d5`、Handoff `53cddda6f0bbc6dd0db3d5481613b9f937772f41`已核对。W105 Main517/CRM132/Handoff81全部正常合并、准确分支删除、finish释放；实际43.07min，85项相关本地检查按原范围保留。AGENTS、统一POLICY和实际固定席位已复核；没有重复W103 junction操作，缓存保留。

冻结R3 V15 SPEC01/217/499/505及已采用DTO要求：GET /api/site/{siteKey}/forms/{formKey} 在DB时钟选择唯一生效bundle，事务内持久IssuedAttempt后返回严格PublicFormView和attempt ETag；秘密浏览器绑定只在HttpOnly/Secure/SameSite=Strict的__Host-cookie，数据库保存带版本摘要，不能用旧成功后PublicAttempt或仅cookie代替。W105实际配置Owner/lifecycle提供保存内容、批准激活和精确有效区间；原Site/Form登记还不等于公开入口授权。

本窗口显式可信部署映射精确tenant/site/form与唯一公开路由，拒绝歧义/缺失，当前已批准bundle提供organizationName/notice/allowedFields及TTL。匿名请求不提供tenant/actor，不使用CRM登录或管理权限。沿用已有签名BFF传输和独立native开关，保持旧宿主范围。

## 任务与实际执行估计

|任务|范围|实际执行估计|
|---|---|---|
|W106-01 Main|前置、窗口登记、台账与四状态及收尾|10–15min|
|W106-02 CRM|可信公开映射、PG IssuedAttempt、GET及BFF、相关验证/审查|50–85min|
|W106-HANDOFF|一次START、原分支恢复交付、合并finish|15–20min|

总估75–120min，Main START `2026-10-24T10:39:19.975741+00:00`；最终按Main finish墙钟计，各仓不重复相加。持续目标CP6-7D-20261022保持ACTIVE。

## 实施与验证

- [x] 显式可信映射精确tenant/site/form；无注册/未激活/未来/禁用同形404，不从URL/body/header推导tenant，不自动公开现有管理配置。
- [x] 独立版本化PostgreSQL IssuedAttempt存储，精确tenant/bundle/form/版本/内容和渲染身份，DB签发/到期、policy TTL及版本、不可改写签发事实；同一Form行串行激活与GET，提交前复验有效窗口和时限。安装仅本轮隔离数据库。
- [x] GET返回冻结PublicFormView和attempt ETag；随机256-bit绑定仅Secure HttpOnly Strict __Host-cookie，CSRF随机且只存HMAC，既有浏览器绑定安全复用/轮换不延长旧Attempt，响应提交后才写cookie。不创建Submission/Lead/replay/outbox业务事实。
- [x] 原Node BFF转发新公开入口，保留无缓存及全部公开安全头，拒绝伪造租户/身份和异常上游响应。查询/方法/body等按合同拒绝。提交和回执未接入时明确拒绝，不落回旧实现。
- [x] 实际PG/HTTP及Node BFF验证跨租户/错误绑定、未激活和边界、并发及中断回滚、严格响应/秘密不泄漏、缓存/头/Cookie、schema保护与实际权限；一次任务级完整diff审查，复用无关成功证据。
- [ ] 三仓正常PR、远端采用核对、准确分支清理，停止自有进程并官方finish。单份CRM交付报告保留失败与未验证范围。

DEV-1+DEV-2串行复用，跨仓官方bind，无新目录连接/源码副本。隔离PG55446与输出D:/CP6-local-test/wave106。生产TLS、企业实际政策/部署映射、源登记发布、公开页面/submit/receipt/H01、正式发送及常驻C2仍对应后继，不以夹具冒充真实采用。WP03-01整卡PLANNED/NOT_CHECKED；24/375、0/125、247NOT_RUN不提升。无Actions、保护改动、既有数据库迁移或部署。

## 实际交付与生命周期

CRM [PR133](https://github.com/GTX537/CP6.CRM/pull/133) 已采用 `c3e94a89e0c542ba3c5a8872ecb7bea55087cb89`，来源 `d2e6263fbb5ecf943ece027f1125a50205cce027`；[单份交付](https://github.com/GTX537/CP6.CRM/blob/c3e94a89e0c542ba3c5a8872ecb7bea55087cb89/docs/development/evidence/W106-02/DELIVERY.md)。

冻结公开GET现由独立native Owner处理。可信部署显式登记精确tenant/site/form/公开路由，拒绝重复或不属于已登记租户的映射；匿名请求不提供租户/actor，不使用管理登录。事务内读取实际已批准配置与DB时钟唯一生效bundle，使用原Form guard与save/preview/activate串行；提交前重核有效区间。未登记、未发布、未来及禁用均同形404，不自动公开管理配置。

独立版本化PostgreSQL IssuedAttempts保存不可变签发事实、精确BundleIdentity原字节、版本/内容/渲染摘要、DB签发与到期及初始ETag。随机浏览器绑定与CSRF只存用途/租户/origin/版本隔离HMAC；约束与延迟触发器校验真实配置、激活、租户和期限。提交成功后才返回冻结PublicFormView及Secure/HttpOnly/Strict的__Host Cookie。旧绑定在轮换后保留到原到期，不延长旧Attempt。并发首次无cookie请求仍可能各自建立绑定，后续消费必须核对实际cookie，不宣称浏览器能合并首批cookie。

现有Node BFF接通冻结公开GET，精确保留编码路径和无损DTO，严格检查响应、无缓存/安全头及唯一允许Cookie；拒绝查询、未实现方法和旧入口回退。真实Node→HTTP→PG互通覆盖保留字符与Unicode。此窗口不写Submission、消费结果、Lead、回放或业务outbox，未来消费/撤销须以独立事实引用原签发。

82项唯一相关本地检查通过、0跳过：26公开Owner/PG/HTTP/cookie/配置绑定（含实际Node BFF互通）、28相关配置/BFF回归、28Node，另含受影响编译与类型验证。 实际DB时钟跨激活边界时回滚旧签发并重试新bundle，跨租户/身份/期限与cookie秘密边界已验证。PG到期负例是直接边界夹具、cookie轮换用受控组件时间，不声称等待五分钟。实际Node互通不等于Chromium真实Secure Cookie投递或完整Next SSR/build；Main代码未变，本轮没有重新执行Main互通。证据 `D:/CP6-local-test/wave106/foundation/final-validation.json` 保留失败、修复和复用来源。

自有PG55446与HTTP/Node测试进程停止，隔离数据和失败日志保留，依赖缓存原位复用，未新增junction或源码树。新schema仅显式安装于隔离库，运行身份不需要新表UPDATE/DELETE/DDL；既有环境不迁移。一项集中审查完成，无Actions、保护变更或生产部署。

WP03-01只完成公开读取/持久签发限定窗口。公开页面、submit/receipt/H01、企业实际源登记发布、正式outbox发送、常驻C2及跨仓即时撤权仍待后继。24/375、0/125、247NOT_RUN不提升，七天ACTIVE。
Handoff [PR82](https://github.com/GTX537/CP6.AI-Handoff/pull/82) 已采用 `08789af72ad034874782164f1c314aa38a6665af`，原准确分支删除、固定DEV-2 finish释放。 CRM分支删除/席位释放已核对；Main本次正常PR及finish继续，当前START后墙钟26.93min（原估75–120），最终按Main finish计，不叠加各仓。
