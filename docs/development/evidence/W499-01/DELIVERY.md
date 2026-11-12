# W499-01 询盘原接口与采用边界

在主仓 `a9140849a354baa2c8081873d263313ed50a3a13` 核原 V15 主文、`SOURCE-MAP.json` 和 `ACCEPTANCE.json`。三个归档文件的实算 SHA-256 分别为 `fa7d0f3388911b30992162f693b8f3340739171d17ae6256c4e72fc569f7890d`、`f21c37e552787f306178fa37bf7a802f1c79d5fcccf3be482bd4c89fc956d728` 和 `d9300d117b5d119e6ebcebe53163509eb60f016934275700cd59d69b112619dd`。现行 CRM 对照提交为 `42fea57e387aa0b4dfc99ae4fc85afb8b29f786d`。原发布件和历史执行字段保持原值。

## AC-INQ-005 接口映射

原 `NEXT-B-CRM-INQ.md` §2 固定 DG01、ADJUST、P01、H01、SC01、DP08、DP10、三个 CUR 切片及四个接口。R-005 对应 FX01、FX03、FX16、FX26。映射指向已验证的实际入口与 Owner。

| 原引用 | 对应入口、效果和已有证据 |
| --- | --- |
| DG01、ADJUST、P01、CUR-CRM-001 | 公开表单签发 Attempt，提交走当前 site/form 和策略，接收方得到浏览器绑定回执。实际 Next、BFF、CRM Owner 与 PostgreSQL 贯通。自动受理经 H01 在同一事务形成询盘、Lead 关联及原结果。FX01、FX03。 [W247](../W247-01/DELIVERY.md) · [W485](../W485-01/DELIVERY.md) |
| H01、SC01、CUR-CRM-002 | 自动受理和主管放行调用询盘的 H01，状态、Lead 关联、审计、Outbox 和可恢复原结果同一事务。拒绝无 Lead。FX16 实测原决策、同键及并发唯一赢家。SC01 的后继 H02 资格及 Account/Contact/Opportunity 转换由 CRM-LEAD 按后继合同负责。 [W121](../W121-01/DELIVERY.md) · [W310](../W310-01/DELIVERY.md) |
| CUR-CRM-009、FX26 | CRM 配置 Owner 保存、预览、生效并维护后继和历史绑定。已签发 Attempt 使用旧 Bundle。原配置冲突、当前授权和原结果分别由配置事务处理。 [W255](../W255-01/DELIVERY.md) · [W247](../W247-01/DELIVERY.md) |
| DP08 | 原 H01 产出为绑定 Submission 的 Lead，`Won` 和 Main 商务结果属于后继 Owner。AC-INQ-031 保留后继零写的实测范围。 [W310](../W310-01/DELIVERY.md) |
| DP10 | Main 签发的用途、租户、权限投影经 CRM 当前授权检查，隐私 Owner 实际执行撤回、导出、期限和文件凭据。 [W482](../W482-01/DELIVERY.md) · [W496](../W496-01/DELIVERY.md) |
| SYS-IAM-01、SYS-PRIV-01 | IAM 提供当前身份、用途及签名事实，CRM 保有业务授权和事务决定。CRM 原生隐私当前处理同意、保留及制品；正式共同服务的采用按 WP03-04 原范围核验。 [W482](../W482-01/DELIVERY.md) · [WP03-04](../../task-plans/WP03.md#wp03-04) |
| CRM-LEAD-01、CRM-ACT-01 | CRM 原生 H01 接收原赢家并形成 Lead。询盘审计由询盘 Owner 保存。ACT 的正式采用以已接受的活动锚点及显式接口为界。 [W135](../W135-01/DELIVERY.md) · [CRM-ACT-01](../../../design-handbook/modules/CRM-ACT-01.md) |

原 15 条方法及路径按同一现行宿主对照。公开为表单 GET、提交 POST 和回执 GET。内部为队列和详情 GET、放行和拒绝 POST、配置 GET 和保存／预览／生效 POST，以及导出请求、Job 读取、下载、撤回四条。浏览器使用实际 Next/BFF 入口，原公开和内部 route 均由当前 Owner 挂载。 [公开和配置入口](https://github.com/GTX537/CP6.CRM/blob/42fea57e387aa0b4dfc99ae4fc85afb8b29f786d/src/CP6.CRM.Api/Inquiry/CrmInquiryNativeHostV2.cs) · [审核入口](https://github.com/GTX537/CP6.CRM/blob/42fea57e387aa0b4dfc99ae4fc85afb8b29f786d/src/CP6.CRM.Api/Inquiry/CrmInquiryNativeHostV2.Intake.cs) · [导出入口](https://github.com/GTX537/CP6.CRM/blob/42fea57e387aa0b4dfc99ae4fc85afb8b29f786d/src/CP6.CRM.Api/Inquiry/CrmInquiryNativeHostV2.PrivacyExport.cs)

## AC-INQ-033 活动采用范围

R-033 的 `fixtureSteps` 为空，验收为文档边界。ACT v1.1.1 原件实算 SHA-256 为 `67dc3667fb852c5672f73710b80a4af17810cd09fe03ed69cbb2c82dff8dfa91`，与 `UA-20260930-A-CRM-ACT-MD01` 接受对象一致。其 CRM 原生主锚为 Account/Contact，Lead 原生事实由 Lead Owner 提供。询盘当前使用 CRM 本地 AuditFact 与 Outbox。将来接入活动时按已接受的锚点和接口另行采用。V15 当时的原件可用性及未运行状态照原记录保留。 [已接受的活动范围](../../../design-handbook/modules/CRM-ACT-01.md) · [W135](../W135-01/DELIVERY.md)

## AC-INQ-036 生产采用证据

R-036 同样为文档边界。已在隔离环境运行真实的 Main OIDC 签发、当前权限、CRM 配置保存和预览、计划生效、当前撤权、日历与原 Attempt 冻结及隐私服务执行。W103 原 `interop-20261024T075814Z/crm.trx` 的 `Configuration_actual_Main_OIDC_and_permission_to_Web_BFF_to_native_Owner_then_current_revoke` 通过，W247 `acceptance-corrected-20261101T191859Z.json` 两种已发布模拟日历通过。W482 与 W496 保留模拟业务和签名身份驱动的真实隐私 Owner 证据。当前生产真实值、批准人、正式调度、IAM 映射和生产运行采用在交付记录中为 `UNPROVEN`。这些字段记录环境事实，开发与验收仍可使用合法模拟输入。 [W103](https://github.com/GTX537/CP6.CRM/blob/42fea57e387aa0b4dfc99ae4fc85afb8b29f786d/docs/development/evidence/W103-02/DELIVERY.md) · [W247](../W247-01/DELIVERY.md) · [W482](../W482-01/DELIVERY.md) · [W496](../W496-01/DELIVERY.md)

## 台账采用

一次集中审查核对原条款、SHA、当前路由、原 TRX 和采用范围。AC-INQ-005、033、036 采纳，原询盘 48 PASS / 12 NOT_RUN。本任务核对文档与已有运行证据，Actions 运行与取消均为 0，部署 0。DEV-1 的本任务分支与席位处置记录在主仓 PR。并行窗口 W500 独占 DEV-2 继续 AC-INQ-029。
