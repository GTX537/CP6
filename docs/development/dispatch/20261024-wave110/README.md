# W110 原CRM图的无损表单版本与原Bundle绑定

COORD本聊天单人；七天目标ACTIVE，PostgreSQL唯一新增数据库，固定席位复用。

## 前置及本轮范围

Main `d639c3b1130413e9b94502a0a042d04d94ff520a`、CRM `c5657a3918d589475a25e5dd4d932af9f11e7c97`、Handoff `6f21e118e8b6e0c54b3d637aec430ba34168fdb9` 已核对。W109 Main521/CRM136/Handoff85全部远端采用、精确分支删除、官方finish释放；实际15.16min，49项本地检查，无Actions。正式24/375、0/125、247NOT_RUN不变。

新BundleIdentity允许任意精度JSON整数，旧完整crm_v1图PublicForm/PublicAttempt的FormVersion为int。不能截断、置1、伪造另一表单或创建简化Submission。W108完整图原PGv1保持；新增显式选择的PGv2模型/安装，仅新库，拒绝把现v1当v2使用。原32组织表、codec、租户FK、精度及完整图继续复用。

表单版本采用原JSON数字UTF8，数据库验证任意精度正整数并按不展开指数的规范数字键比较；同值不同词法表达能匹配，原词法仍保留。既有int访问保持兼容可表达值，对超范围值明确失败。PublicAttempt同样无损存储；不以此声称新版receipt消费已接通。

同一调用方事务中，将真实原Bundle身份与原CRM Site/PublicForm核对并保存绑定；原JSON与实际组织/站点/表单/版本必须一致，冲突及跨租户拒绝，回滚不遗留部分关系。绑定仍须原来源并复核当前原图；不把元数据绑定当服务身份、H01授权或业务成功。

本轮只打通此实际存储和身份前置。原日历/Intake完整采用、ConsentFact/ReceiptProof/消费重放、服务主体与最终授权、H01以及完整submit/receipt/page继续后续实现；不虚构完整WP03-02完成。

## 任务与实际耗时估计

|任务|实际工作|预计执行|
|---|---|---|
|W110-02 CRM|显式PGv2原图、无损版本、原Bundle表单绑定及真实PG故障/边界|45–75分钟|
|W110-01 Main|台账前置登记、真实结果和四状态同步|10–15分钟|
|W110-HANDOFF与收尾|START/交付、一次审查、三仓PR/精确分支/席位|15–20分钟|

合计70–110分钟，Main START `2026-10-24T12:24:47.612460+00:00`，最终按finish墙钟计，不叠加三仓。复用DEV1/DEV2、原构建缓存，自有PG55446及D:/CP6-local-test/wave110；无Actions、部署或已有数据库迁移。

- [x] 原完整图无损版本及明确PGv2准入，PGv1历史保持。
- [x] 实际原Bundle身份绑定、故障/精度/事务/租户和局部回归。
- [ ] 一次完整任务审查、台账同步与三仓正常合并。
- [ ] 远端采用核对、精确分支清理、自有进程停止、官方finish释放。

## 实际交付与生命周期

CRM [PR137](https://github.com/GTX537/CP6.CRM/pull/137)已采用 `6c181d249745a9fb1b77aaef3d19da931ab7f4ab`，来源 `5a8550ad91b0d878be26c8bc0dc211aa6cad4e85`；[单份交付报告](https://github.com/GTX537/CP6.CRM/blob/6c181d249745a9fb1b77aaef3d19da931ab7f4ab/docs/development/evidence/W110-02/DELIVERY.md)。

新增显式PGv2原完整图，原32组织表和Lead62/PublicSubmission56保持，PublicForm/PublicAttempt的FormVersion采用原数字UTF8 bytea；原词法保留、任意精度正整数按不展开指数的规范数字键比较。旧int访问保留可表达值，超范围明确失败，绝不截断或置1；可空历史attempt事实保持。独立上下文类型隔离EF模型，v1入口保持；v2元数据Version=2，明确仅新库安装，不升级既有v1或切换宿主。

调用方ReadCommitted事务内，真实原Bundle、配置Owner和原Site/Form注册与现crm_v1行逐一核对组织/ID/键/版本；只绑定既有原图，缺失拒绝，不创建伪造配置或替代Submission。原身份JSON保存原字节，重复绑定一致，调用方回滚不遗留关系。复合租户FK、SQL完整性与不可变触发器拒绝伪造或事后漂移。真实原表FOR SHARE行锁覆盖非键版本修改；两个竞争方向均观察pg_blocking_pids后验证，绑定先提交则更新拒绝、更新先提交则绑定读到新值后拒绝。NUL/组合字符/补充平面键、12000位系数及超出PG numeric范围的指数均保留。

75项唯一相关本地检查通过、0跳过：25项真实PostgreSQL原图/绑定与50项旧模型和attempt codec回归；生成器检查及受影响测试项目构建通过。 其中11项新增真实PG检查、13项原完整图回归及1项既有Bundle检查。首次辅助连接方法名编译错误和13项旧图共用安装夹具失败均保留；根因是条件表达式target-typed new误选派生v2上下文，明确两种构造类型后联合25项PG全部通过。证据`D:/CP6-local-test/wave110/foundation/final-validation.json`；不因后续文档/提交/SHA重复产品测试。

CRM准确任务分支已删除、DEV-2官方finish释放；自有PG55446已停止，原数据库/失败证据/依赖缓存保留。一次任务级集中审查，未触发Actions，无既有环境迁移、保护调整或部署。

本轮只完成无损存储和原表单身份绑定前置，不能授予业务权限、消费attempt或签发receipt。完整原日历/Intake采用及隔离级别兼容、ConsentFact/ReceiptProof/GraphBindingStatus版本化扩展、消费/重放、真实service主体和H01最终授权、编号及完整submit/receipt/page仍待。24/375、0/125、247NOT_RUN不变，七天ACTIVE。
Handoff [PR86](https://github.com/GTX537/CP6.AI-Handoff/pull/86)已采用 `c0af8315db75e64024919b15b43ba56f9b43154e`，准确分支删除、固定DEV-2 finish释放。 CRM分支删除/释放已核对；Main文档PR/finish继续，当前START后墙钟18.58min（原估70–110），最终按Main finish计，不叠加各仓。

本文件提交时Main最终合并/席位收尾仍待；最终证明以官方finish与上述final_proof为准，下轮真实开工时统一核销，不另建记账分支。
