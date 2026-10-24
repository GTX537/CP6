# W109 人工询盘受理的真实 Main 权限签发

本聊天COORD单人，七天持续目标ACTIVE。按最新AGENTS、台账、Handoff和固定席位执行；无代理或新聊天。

## 前置与交付边界

Main `b89b8eeb589f25a57c51116d842770edec953ccf`、CRM `dfeba8e44d58a668b8f721b605a425f1bc5279b7`、Handoff `e10cab1497d1341d53a8538a5ab26eb93734a353`已确认。W108 Main520/CRM135/Handoff84全部正常合并、准确任务分支删除、官方finish释放；实际29.33min、19项相关检查。原GET、配置Owner和原完整图PG存储已采用，完整H01及公开提交尚未采用。

核对R3V15正文33/65–68行和canonical PermissionProjection：人工list/detail要求crm-lead:query、实际主管和管理部门；release另需view-pii/add，reject另需view-pii/edit，并需要contactName/email/phone/company/need/riskCategories六字段读权限。现Main签发器仅配置purpose，尚未提供这些事实。现Main CrmOidcDirectory以crm-lead:assign和真实crm-lead数据范围推导主管，部门需当前完整健康树；角色/字段来源受AuthenticationCompletionGate保护。

本窗在现实际Main登录/OIDC签发入口添加四个人工intake用途，采用现权限聚合、实际角色Site/Form精确配对和主管/部门事实。登记独立crm-intake字段资源与明确一对一wire映射；它仅用于询盘源字段读能力，沿用存行MIN后注册默认策略，仍须view-pii和实际用途/范围，不能借lead编辑或隐私导出权限。未知字段、源不完整、非法access或未封存目录拒绝。新签名仍为时点事实，不是跨库最终业务fence。

CRM仅增加实际Main HTTP签名的严格合同/持久信任互通验证，拒绝错主体/用途/篡改和撤销密钥。自动受理的service身份、H01写事务、原Bundle到旧图的无损版本桥、IssuedAttempt消费/重放、编号和完整submit/receipt/page不冒充完成。不得把service:<ClientId>改造为虚构人类UUID，不把任意精度版本缩成int。

## 任务与实际耗时估计

|任务|范围|预计本对话执行|
|---|---|---|
|W109-01 Main|实际intake权限源/签发、定向PG与HTTP、台账四状态|25–40min|
|W109-02 CRM|消费真实Main签名，合同与撤权互通|10–15min|
|W109-HANDOFF及收尾|START/交付、集中审查、正常PR/精确分支/席位收尾|15–25min|

合计50–80min，Main START `2026-10-24T12:05:34.129471+00:00`，最终按finish墙钟计，不叠加各仓。复用固定DEV1/DEV2和已有构建缓存；Main测试使用自有隔离PG，CRM PG55446，输出D:/CP6-local-test/wave109。无Actions、生产、已有环境迁移或增加席位。

## 验证与收尾

- [x] 实际主管/健康部门/精确SiteForm、完整字段来源与purpose范围验证。
- [x] 无PII动作时遮蔽、隐藏字段拒绝写用途、角色/字段/部门撤权与真实锁等待。
- [x] 实际Main HTTP签名→CRM严格合同/持久信任，定向回归和一次任务级审查。
- [ ] 三仓正常合并、远端采用、准确任务分支删除、自有进程停止、官方finish释放。

正式24/375、0/125、247NOT_RUN不因限定前置提升，七天ACTIVE。

## 实际交付与生命周期

Main W109-01候选扩展实际`POST /connect/inquiry-permission`，支持`intake:list/detail/release/reject`。仍由真实Main登录/OIDC Bearer确定主体；同一ReadCommitted及AuthenticationCompletionGate内读取当前角色/action、精确Site/Form配对、健康部门与实际主管，末段重读会话并使用数据库时间，成功commit后才返回签名。配置用途行为保持。

主管采用现Main `crm-lead:assign` + crm-lead数据范围：本人不等于管理域，部门含完整健康祖先链，自定义仅有效当前租户部门，组织范围枚举真实部门。冻结wire最多256及UUIDv4限制遇到不符返回422，不截断或改写真实身份。

明确字段源`crm-intake`及`MainInquiryIntakeReadFieldsV1`：contactName/email/phone/company/need/riskCategories一对一映射为询盘读能力。启动封存目录进入现字段权限管理Owner；完整实际角色存行MIN之后才补注册默认1，未知字段/非法access/不完整来源拒绝。view-pii缺失时列表/详情fields为空；release/reject需六字段均可读（Access1或2），且分别需要query/view-pii/add或query/view-pii/edit。不伪造业务约束Allowed，不借配置/lead编辑字段/隐私导出权限。

内部字段源增加可信主体读取，仅由已认证OIDC生产者在原权限事务调用；普通HTTP读取仍执行原身份校验。签名保留原Ed25519/canonical合同与有效期，新增的字段/主管/部门均受签名覆盖；是时点事实，CRM现`CanAuthorize=false`不变，实际资源和跨库最终业务fence仍须后继实现。

CRM [PR136](https://github.com/GTX537/CP6.CRM/pull/136)已采用 `c5657a3918d589475a25e5dd4d932af9f11e7c97`，来源 `8ed860df988a1edfe70acdef874de748e7064681`；[单份互通报告](https://github.com/GTX537/CP6.CRM/blob/c5657a3918d589475a25e5dd4d932af9f11e7c97/docs/development/evidence/W109-02/DELIVERY.md)。原样读取实际Main人工放行和配置HTTP签名，现严格合同、实际PG时钟及持久信任通过；错主体/用途、字段/部门/主管篡改、密钥撤销拒绝。未修改CRM业务宿主/模型/合同。

49项最终适用的唯一相关本地检查通过、0跳过：Main47项实际权限源/PG/HTTP与局部回归，CRM2项实际签名互通；受影响项目编译通过。 Main首轮45通过/2失败，失败仅为新增原生竞争测试误用DbSet复数表名；改为真实物理表后仅重跑2项，实际观察pg_blocking_pids并在写入撤权提交后拒绝签发。两处初始编译命名空间遗漏也已修正，全部原失败保留。45项产品路径未变成功结果复用，未因提交重复测试。证据`D:/CP6-local-test/wave109/foundation/final-validation.json`；人工签名12:11:46UTC生成，CRM12:12:17UTC实际验证，后续不得改时间复用已过期凭据。

CRM准确任务分支已删除、DEV-2官方finish释放；自有PG已停止，原数据/失败日志及依赖缓存保留，无新junction或源码副本。一次任务级集中审查、相关局部验证，无Actions、保护调整、既有环境迁移或生产部署。

当前仅补人工权限生产者和互通前置。自动受理服务身份、H01实际图/事务/最终授权、任意精度原Bundle到旧图的身份桥、消费/重放、编号及完整submit/receipt/page仍待；24/375、0/125、247NOT_RUN不变，七天ACTIVE。
Handoff [PR85](https://github.com/GTX537/CP6.AI-Handoff/pull/85)已采用 `6f21e118e8b6e0c54b3d637aec430ba34168fdb9`，准确分支删除、固定DEV-2 finish释放。 CRM分支删除/释放已核对；Main实际产品PR/finish继续，当前START后墙钟13.44min（原估50–80），最终按Main finish计，不叠加各仓。
