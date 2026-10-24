# W107 原Attempt校验与PostgreSQL三维原子限流

COORD本聊天单人，沿用writing-plans / executing-plans，七天持续目标ACTIVE；不另开代理/聊天/定时任务。

## 前置与限定窗口

Main `6c52c13dca26aa37d02590c504587571dbc2a4b3`、CRM `c3e94a89e0c542ba3c5a8872ecb7bea55087cb89`、Handoff `08789af72ad034874782164f1c314aa38a6665af`已核对；W106 Main518/CRM133/Handoff82全部正常合并、准确分支删除、finish释放，实际29.49min、82项相关本地检查。最新AGENTS/POLICY、台账与真实固定席位已核对。W103 junction问题已解决，缓存保留，不重复操作。

W105实际不可变配置/激活与W106实际公开GET/持久IssuedAttempt已采用。WP03-02直接前置WP02-09仅配置Owner已采用，WP02-16的W65只完成旧Pub采番原子推进，未交付完整原命令编号回执；真实提交/H01/编号/原图/回执仍不能据此开通。Platform网关的进程内限流不是CRM外层事务内三维计数。

本窗口实施可独立交付的公开提交前置：从实际已签发Attempt读取原bundle/政策、校验原精确路由/origin/browser/CSRF/ETag/DB到期和冻结请求；NFC业务指纹保留完整bundle与政策身份。三维10/20/30按顺序锁定，以实际数据库时间刷新窗口、最大Retry-After，并在调用方同一事务原子记录三维额度。数据库租户、完整原绑定与事务身份不可由客户端推断。检查结果不是受理授权或成功回执。

冻结R3 V15正文15–27/87/218/499/505、crm-inq-target-schema.sql的RateLimitBucket与V15压缩包实际最后定义的handle_submit/_rate_refresh为语义来源；只采用PostgreSQL适用部分。原生桶与原policy三个参数不匹配409，不静默刷新计数/套新政策。429只允许限流自身的窗口/阻断事实，不写业务图；受理失败时同事务回滚三桶。没有新的默认企业参数。

## 任务与实际执行估计

|任务|范围|预计实际执行|
|---|---|---|
|W107-01 Main|前置、窗口登记、台账/四状态、合并收尾|8–12min|
|W107-02 CRM|原Attempt/请求校验、三维PG计数、定向验证和集中审查|30–50min|
|W107-HANDOFF|一次START、原分支恢复交付、正常合并finish|7–13min|

总估45–75min；Main START `2026-10-24T11:12:03.056509+00:00`，最终以Main finish墙钟计、不重复相加。持续目标CP6-7D-20261022保持ACTIVE。

## 实施与验证

- [x] 实际公开GET签发的Attempt与原不可变bundle精确读取；原Browser/CSRF/origin/ETag和DB时限校验，后继激活不改旧绑定。
- [x] 严格冻结PublicSubmissionRequest、NFC/允许字段及完整身份指纹；原输入秘密不写日志，拒绝伪造tenant和不匹配来源。
- [x] 版本化PG三桶存储，10/20/30固定锁序、同事务计数与回滚；等待后DBclock重核，原参数不一致拒绝、按实际窗口轮转、最大Retry-After。
- [x] 真实PG并发/隔离/多租户、原GET到校验、错误秘密/原键/政策/边界、失败回滚及无业务写入验证；仅相关本地检查与一次完整任务审查。
- [ ] CRM、Handoff、Main正常PR、准确远端采用、任务分支删除；自有进程停止、缓存与证据保留、官方finish释放。

固定DEV-1/DEV-2串行复用；PG55446隔离输出D:/CP6-local-test/wave107。本轮不开放HTTP提交成功、不消费Attempt、不造Submission/Lead/ReceiptProof/命令原结果；后继真实Owner须在原事务核当前消费/墓碑与H01权限，再接业务图、审计/outbox及公开页面。编号/企业政策/源发布/正式发送/既有环境迁移仍独立。WP03-02整卡PLANNED/NOT_CHECKED、24/375、0/125、247NOT_RUN不变。无Actions或部署。

## 实际交付与生命周期

CRM [PR134](https://github.com/GTX537/CP6.CRM/pull/134) 已采用 `a69098129ef64d1bd6ae0a96df8831890220ffce`，来源 `955bb8a14e105f55eefff474baec5e86c2b4ce38`；[单份交付](https://github.com/GTX537/CP6.CRM/blob/a69098129ef64d1bd6ae0a96df8831890220ffce/docs/development/evidence/W107-02/DELIVERY.md)。

公开提交前置组件读取实际GET的不可变IssuedAttempt和原BundleIdentity/Activation/content/rendered/policy，核对tenant/site/form、origin、受保护browser、CSRF、ETag和DB时限。后继激活不重选旧Attempt政策；冻结请求在trim/NFC前后严格验证，完整身份指纹以明确本地v1编码保留任意精度数字，不冒称历史夹具HMAC字节一致。没有新增HTTP提交入口，CanAcceptSubmission恒false。

原生PG以tenant/dimension10/20/30/HMAC为三桶主键，可信IP、browser与form按固定顺序锁住；原政策参数冲突409，等待后DBclock核到期，实际窗口轮转、向上取整最大Retry-After。同一调用方事务原子计数，自有savepoint失败撤回本次三桶且保留更早写入；后续业务失败需整体rollback。429只允许限流操作事实，无业务图；旧Serializable快照40001不自动改成成功。FK/主键/保留约束、不可变政策与关键触发器就绪守卫生效，已有schema失败不自动修补。

24项最终适用的唯一相关本地检查通过、0跳过：21项原Attempt/内容/PG三维限流，3项既有公开GET回归；受影响编译通过。 实际配置save/preview/activate/GET产生Attempt，再调用组件验证并发12请求、真实tenant/browser隔离、原bundle与精度、错误绑定、策略冲突、回滚/故障/40001、DB窗口与守卫缺失。300ms截止与500ms锁等待仅为限流组件边界，不声称五分钟HTTP到期验收；并发计数不等于提交幂等。初始两个夹具失败及定向修复/复用源保留在`D:/CP6-local-test/wave107/foundation/final-validation.json`。Main/Node/browser输入未变，本轮未重跑。

自有PG55446及测试HTTP停止，隔离数据/失败日志与原依赖缓存保留，没有新junction、源码树或依赖版本变更。单次集中审查完成，无Actions、保护变更、既有环境迁移或生产部署。CRM准确任务分支已删除、DEV-2 finish释放。

WP03-02只完成独立前置。原Attempt消费/重放/墓碑、真实H01服务权限、原业务Submission/Lead图、编号原操作回执、业务outbox及公开submit/receipt/page仍未完成，不新造简化业务表冒充采用。24/375、0/125、247NOT_RUN不变，七天ACTIVE。
Handoff [PR83](https://github.com/GTX537/CP6.AI-Handoff/pull/83) 已采用 `88364f6475e7ab58cf09eac933922b264f2bf8a8`，原准确分支删除、固定DEV-2 finish释放。 CRM分支删除/席位释放已核对；Main本次正常PR及finish继续，当前START后墙钟15.13min（原估45–75），最终按Main finish计，不叠加各仓。
