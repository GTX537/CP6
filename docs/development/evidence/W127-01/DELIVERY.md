# W127-01 原生人工撤回同意交付

W126提供当前真人来源后，本轮在CRM新增独立撤回Owner与页面。严格冻结ConsentWithdrawalRequest/View、If-Match和幂等槽；主管且管理部门，query与consent-withdraw逐动作准确范围，精确site/form/ConsentFact及可写consent字段。不借PII查看、放行或拒绝权限。

原PostgreSQL Serializable事务保持身份/信任→槽→Submission根锁顺序，提交前重查真实当前证明和数据库时钟。首次WithdrawnAtUtc、原ReceiptProof撤销、根history/seal/ETag、不可变原结果、审计、目标本地事件及正式Outbox共同提交。重放仍需当前授权；失效ETag/改变参数/后段失败不新增效果。原Granted与Lead/Submission历史同意保持，不修改处置、原决定时间和保留期限，不提前删除PII。

新crm_inquiry_consent_v1只存非PII原结果、HMAC请求/原因指纹与必要不可变证据，显式Installer与普通宿主readiness分开，默认ConsentWithdrawalsEnabled=false。延迟数据库门禁验证同根/事实/凭据/事件完整性；正式Outbox新路由Blocked，未宣称外部已投递。事件版本排除自动转Lead的重复历史事实，避免后续撤回与匿名化版本冲突。

独立consent-context只用于显示，写入另取当前证明。React→BFF使用原受保护会话/CSRF；未知结果保留同body/key/ETag/身份，阻止同时审核/刷新，412刷新后明确重提，CSRF/访问失效/期限到期清屏并隔离晚到响应。390px页面无横向溢出。原IntakeSubmission.allowedActions仍只含release/reject。

79项唯一相关本地检查通过：27项PostgreSQL/HTTP/实际浏览器、52项Web及相关页面类型/浏览器包构建；必需0跳过。包括真实原60分钟保留期，不改业务期限或时钟。

汇总D:/CP6-local-test/wave127/foundation/final-validation.json记录唯一用例成功来源。实际PG/HTTP覆盖并发、回滚、期限/取消、当前会话变化、禁用门禁、无PII权限独立撤回、撤权后拒绝原结果重放、拒绝后保留原历史、共享根CAS；浏览器覆盖正常、未知结果、旧标签页、真实CSRF拒绝、晚到及显示到期，原放行/拒绝回归通过。CSRF浏览器场景与Owner实际撤权场景分别表述。

真实长测按原deadline 2026-10-25T04:34:20.029221+00:00，实际完成2026-10-25T04:34:27.7843136+00:00，撤回后回执404、期限前不删除；自然到期后原生匿名化、ConsentFact撤回历史与凭据墓碑、非PII撤回原结果保留、事件1→2→3及重复扫描无新增。使用03:34独立编译制品快照；最终源码随后排除自动转换重复历史，该NeedsReview路径无转换记录且语义相同，另由最终原生HTTP覆盖自动转换/拒绝序列。不把快照称最终SHA新执行，也不把W124原提交PII结果清理复用记作本例新断言。

W126实际Main HTTP签名/原字节来源互操作按原适用范围引用，不增加本轮计数或拼接为生产端到端；本轮页面与IAM服务为明确本地夹具。保留所有初始失败及修正日志：编译变量名、非主管错误码、测试历史排序、夹具复制、可访问名称、StrictMode夹具等；最终只重查受影响部分，没有重复全仓验证。

一次集中SPEC/QUALITY审查通过。仅两项明确csproj资源嵌入/夹具复制，无包、构建目标或工作流改变。推送和PR前复用内容未变的工作流审查并核对准确差异，本轮Actions零，无生产、既有环境迁移或保护绕过。

CRM [152](https://github.com/GTX537/CP6.CRM/pull/152)采用source 105df93427bf0504ce077dee2c85092e433c7340 至main bb5edc19798eb7d03b02d4e528b099b5ce594213；Handoff [103](https://github.com/GTX537/CP6.AI-Handoff/pull/103)采用source c8168a77ca35aa4ad195332024a299b185679568 至目标 a953c39a8bfcfd4ab33cc6547906298588053325。两任务官方finish已核对准确本地/远端分支删除和席位释放，DEV-2停在Handoff已确认目标留作复用。自有PG55453/55454按身份及连接核对正常停止，数据、失败日志、可复用依赖缓存和用户文件保留。Main正常PR与finish待本轮完成，最终以wave127-final-verification.json核销；下轮准入统一更新本快照。

原估90–150分钟，截至交付准备99.02分钟，最终以Main START至三仓最后finish计一次，包含真实60分钟期限等待，不将跨仓时间相加。

下一窗口落实privacy:export/download的独立当前真人来源与明确字段用途映射，再实施原生导出任务/加密制品/单次下载及页面。原生Outbox发布、真实授权配置/既有环境迁移/Lead月跨度到期和正式业务验收仍未完成。

正式24/375 DONE、2 IN_PROGRESS、349 PLANNED；0/125；247 NOT_RUN+41 REFERENCE_ONLY保持。D6 PARTIALLY_ADOPTED，七天ACTIVE，不把限定Owner采用等同完整WP03-04。
