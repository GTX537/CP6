# W99 CRM受保护会话到同事务身份的宿主入口

COORD本聊天单人；Main `790a19c88265be8bc4039556f4e5d54be41bd903`，CRM `59845f28539d89582884db442e25e95cd29ce328`，Handoff `84897920632c79f32c2ad95695c30c1b7b45b049`。最新AGENTS/POLICY、官方status/capacity与台账、实际源码已核对；没有容量告警或重复任务。W98 Main509/510、Handoff74已远端采用，30相关验证和类型检查通过，实际13.99min＋前置约2min；准确分支删除、三个固定席位释放。W89/W97也已完整收尾，不重复。

## 前置、范围和出口

W89持有式检查需要可信宿主提供已解保护的原PG会话版本和真实在线身份；当前ReadAsync会Touch改变版本，detached reader不提供可直接用于业务事务的原会话。正式CRM-INQ配置矩阵需要独立crm-site query/configure与精确Site/Form，fields=[]；Main已有动作目录，但现有crm-context仅有crm-lead部门范围，不能转为Site/Form。真实范围/签名生产者及业务Owner仍是后续必需范围。

本窗在实际CrmAuthenticationService增加明确PG/真实CrmOidcClient入口：浏览器只能给opaque cookie/CSRF；从真实受保护存储读取、解保护、核对索引身份和session版本，执行当前在线JWT/context验证，冻结Actor及token/epoch/原版本/原有效期。既有登录/ReadAsync不改变；新入口不touch、不续期、不吞注销/替换竞态、不暴露token或protected payload。错误CSRF必须在联网前拒绝。

返回不可由浏览器构造的宿主捕获；直接调用W89 HoldCurrentIdentityAsync与最终重核，绑定原身份/会话、真实连接/Serializable事务及pg_current_xact_id。所有网络在持锁前完成；同事务期间不联网，超时/取消/版本变化整笔拒绝，无自动重试。事务结束、换连接、丢锁、注销/续期/替换、撤权、过期不能重用。CanAuthorize=false；此入口解决真实会话接线，不冒充Site/Form授权或配置完整业务采用，不自动启用PG Program/路由/安装。

预计实际35–50min：宿主入口12–18，真实TLS/JWT/PG及会话竞态12–18，集中审查和三仓交付10–14；独立前置约5min。START `2026-10-24T04:36:50.144904+00:00`，最终按finish计，不叠加子任务/Handoff。原生PG复用55446；定向验证原版本不变、真实受保护输入、跨主体/索引/CSRF/失效JWT、在线撤权和持锁竞态、最终校验及外层回滚。一次完整作者SPEC/QUALITY，复用W89/BUG123未变验证。

DEV-1 Main协调；DEV-2 Handoff START可靠保存后finish deferred，再官方bind CRM，产品合并finish后bind回原Handoff任务。正常PR/远端采用/准确分支清理/finish释放。PostgreSQL唯一新增数据库；无Actions、既有环境迁移或生产部署。24/375整卡、125目标0完整、247正式运行NOT_RUN不提升，七天全目标ACTIVE。

## W99实际采用与最后收尾

CRM [PR126](https://github.com/GTX537/CP6.CRM/pull/126) source `c5ddbe13accfdfd5e3b3a7ced52088e8c1edcbbf`已采用远端main `e86ca195470f8e4fad29aab474cfbef696dde132`，7任务路径逐blob一致。[单份交付](https://github.com/GTX537/CP6.CRM/blob/e86ca195470f8e4fad29aab474cfbef696dde132/docs/development/evidence/W99-01/DELIVERY.md)。

实际CrmAuthenticationService新增明确PostgreSQL/具体在线客户端命令准备入口：只用浏览器opaque cookie/CSRF，从受保护原会话读取固定身份、epoch、版本和有效期，每次执行真实在线JWT与no-store context验证。无需浏览器提供Actor/Token/版本；不Touch/续期/改变会话，不公开token/cookie/密文/hash。有效期取原会话、原行和实际JWT的最小值，并在await后重查。一次性捕获直接进入W89同事务原身份/会话持锁，失败后不得重用，提交前原事务最终重核；锁内不联网，无隐式重试。

54项相关本地检查通过：21项真实受保护会话/TLS/JWT/PG宿主行为＋33项相关身份与认证回归，0跳过；原失败保留，无Actions，正式业务NOT_RUN。API与必要依赖同次编译通过；一次集中SPEC/QUALITY。首轮18通过/3失败为不合法存储准备和错误异常类型预期，现有守卫没有放宽；改为真实epoch变化并校正断言，原日志/TRX保留。自有PG55446已smart stop并保留数据，测试HTTP宿主正常释放；无schema/依赖变更。

CRM准确任务分支已删除、固定席位finish后官方bind回Handoff。CanAuthorize=false；真实Site/Form当前范围、Main签名权限生产者、实际配置Owner/Program路由和保存/Audit/Outbox采用仍需后继，配置矩阵fields=[]，不能用线索部门或PII替代。24/375整卡、125目标0完整、247正式运行NOT_RUN不变，七天ACTIVE。没有Actions、既有环境迁移或生产部署。

Handoff [PR](https://github.com/GTX537/CP6.AI-Handoff/pull/75)已采用 `ed4e49d9f68fd599d2c9dc29f0c5e77a6b538fd6`，准确分支删除、固定树干净detached、finish释放。Main本次协调PR/finish仍待完成；当前约12.77min，原估35–50min＋独立前置约5min，最终以wave99-final-verification.json实际finish为准，不叠加CRM/Handoff。
