# W120 原生受理列表与详情独立 Owner

COORD本聊天单人；七天目标ACTIVE。W119 Main PR531、CRM PR145、Handoff PR95已远端精确核对、准确分支删除和官方finish，全席位释放；登记29.9分钟，原估70–110，先前调查/最终核对另记。本轮不重复旧交付。

## 前置与执行范围

W117完整原提交/H01图、W118公开表单/回执入口和W119实际Main人员上下文均已采用。最新AGENTS、统一工作区规则、台账、Handoff和官方status已核对。旧CrmBusinessService.Intake使用历史codec/候选DTO与硬编码期限，不能读取本轮原生格式。直接前置按实际采用范围准入；完整WP03-02/03及正式验收不因此完成。

实现原GET /api/crm/v1/intake/submissions和/{submissionId}独立读Owner，采用原保护会话、在线人员上下文、身份七锁与持久信任；保持当前query/supervisor/managed department、精确site-form对与已签字段相交，详情越权同资源404。原列表无站点筛选参数，多站点/表单不能缩为配置主表单；先仅发现候选范围、锁外取证，最终锁内完整当前范围复核，任何来源变化失败关闭。

读取W117显式PostgreSQL原图/新列保护profile，验证完整性与原资源状态，按持久deadline在解密前决定保留/匿名化视图。原冻结IntakeSubmission/SubmissionPage、piiMasked全null/空风险/空动作、字段投影、准确ETag和原始状态保留。稳定排序/页大小/status及MAC游标绑定当前可信tenant/subject/purpose/filter/sort/pageSize，期限用DB时钟，拒绝篡改/过期/跨租户/跨用户/旧键与资源位置冲突；512字符游标上限必须遵守，不改冻结合同。

接入既有明确原生开关与可信BFF配置、严格路径/查询/头/响应边界，不接受浏览器权限DTO。页面UI、放行/拒绝写Owner、隐私/保留worker和实际Outbox发布者另续；不提前显示已开放写行为。无新EF/迁移/依赖版本需求，若发现实质范围变化先回总控窗口记录。

## 任务与实际执行估计

|任务|预计实际执行|
|---|---|
|W120-02 当前身份下多表单取证与锁内权限读取|30–45分钟|
|W120-02 原生图、字段/期限裁剪与稳定分页|40–60分钟|
|W120-02 HTTP/BFF接入、定向PG/HTTP边界与集中审查|25–40分钟|
|W120-01/HANDOFF 台账同步、三仓合并、准确分支清理及席位释放|15–25分钟|

合计110–170分钟，登记起点`2026-10-24T19:57:22.293941+00:00`，各仓不叠加。固定DEV1/DEV2复用，PostgreSQL唯一新增数据库，精简相关本地验证与一次任务审查。无Actions、保护变更、既有数据库迁移/安装或生产部署。

- [x] 完整当前多表单权限范围与独立读取Owner。
- [x] 原图无损读取、字段/保留期限、冻结响应和稳定游标。
- [x] 实际HTTP/BFF链路和定向风险回归、集中审查。
- [ ] 三仓远端采用、准确分支清理和官方finish。

正式24/375DONE、2IN_PROGRESS、349PLANNED；0/125；247NOT_RUN+41REFERENCE_ONLY不变。

## 实际交付

CRM显式开启原生询盘列表/详情读取Owner，原保护会话在线取多site/form精确对证据，最终身份七锁→持久信任→资源行锁内复核当前权限、部门及字段。读取已保存PostgreSQL v4完整图与原列保护profile，完整封印/持久PII期限先于解密；稳定MAC游标绑定当前请求和资源版本，BFF严格验证冻结响应后释放，不写业务/不续期会话。

CRM [PR146](https://github.com/GTX537/CP6.CRM/pull/146)采用 `5ed99382737f476f68bdcfd3166e1260049812bb`，源 `73c6de3ca36a55f245a94d18df376497d700601d`；[单份交付](https://github.com/GTX537/CP6.CRM/blob/5ed99382737f476f68bdcfd3166e1260049812bb/docs/development/evidence/W120-02/DELIVERY.md)。64项唯一相关本地成功检查：22项游标/PostgreSQL/HTTP、42项Web；实际公开提交完整图→独立读取Owner→Node BFF，权限/租户/字段/期限/行锁变化及传输拒绝覆盖，必需范围0跳过。 本地证据 D:/CP6-local-test/wave120/foundation/final-validation.json。W119实际Main人员HTTP签发按未变输入复用，不计本轮新执行；本轮TLS身份夹具明确标注。原失败记录保留，SLA原偏移封印恢复与严格数值查询边界已有最终相关回归。

未改冻结合同、数据库模型/迁移、依赖/工作流。自有PG已停止、无新junction，缓存/失败记录/用户文件保留，无Actions、保护变更、既有环境迁移或部署。Main本轮仅协调文档。人工放行/拒绝、页面、隐私/保留worker及实际Outbox发布者后继；正式24/375、0/125、247NOT_RUN不变，七天ACTIVE。
Handoff [PR96](https://github.com/GTX537/CP6.AI-Handoff/pull/96)采用 `b28ac649bad6a0a6e1bab9bcd4301cd851519448`，准确分支已删除、DEV-2已finish释放。 Main最终PR/finish尚待；START后墙钟45.45分钟，原估110–170分钟，最终按Main finish计，不叠加各仓；不含先前只读调查。

本提交时Main最终合并和席位收尾待；以官方finish与final_proof作最终证明，下轮统一核销，不额外创建记账分支。
