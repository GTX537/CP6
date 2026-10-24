# W119 人工审核权限上下文与当前源版本绑定

COORD本聊天单人；七天持续目标ACTIVE。W118三仓PR530/144/94已远端核对、准确分支清理和官方finish，27.45登记分钟；调查/最终核对另记。全席位空闲后复用，旧任务不重复。

## 前置、范围与完成条件

原R3V15人工intake:list/detail必须当前主管/部门/字段授权。Main已有人工权限签发，CRM当前身份锁已落地，但旧人工签名没有用户/全部角色源版本，当前inspection明确FieldPolicyGate unavailable；不能直接把旧签名用于新详情明文读取。W117服务上下文不能冒充人工身份。

本轮先补齐独立人工上下文：Main真实Bearer、认证根锁、已发布C02用户及全部角色源、随机数/用途/实际site-form和bundle绑定；保持内层冻结PermissionProjection原字节，独立版本/域隔离外签名。CRM原保护会话在线捕获后实际HTTP取证，锁内验证持久信任、用户/角色完整集合与版本/哈希、epoch及同事务当前性，任何错配失败关闭。不得从grant/pair反推全部角色导致遗漏零grant角色。只输出证据、不自行授权业务。

用途限定人工intake:list/detail/release/reject；不启用业务读取/写入路由、不借用服务身份。W120才接审核队列/详情新格式Owner；原90–140分钟读Owner预估被前置审计调整，本轮人工权限基础70–110分钟。完整WP03-02/03、125目标/247正式验收均不提前关闭。

## 任务与实际执行估计

|任务|预计实际执行|
|---|---|
|W119-01 Main签发、真实身份/源读取、协议及定向PG测试|25–40分钟|
|W119-02 CRM严格验签、会话HTTP获取、完整角色源锁内比对|25–40分钟|
|W119-01/02/HANDOFF 定向交叉验证、一次集中审查、三仓正常合并与清理释放|20–30分钟|

合计70–110分钟；正式起点`2026-10-24T19:24:09.844668+00:00`，仓库时间不重复相加。PostgreSQL、固定DEV1/DEV2、精简本地验证；不触发Actions、不更改保护/既有数据库/生产环境。

- [x] Main原Bearer路径和新版本签名上下文。
- [x] CRM严格验证与保护会话、当前用户/全部角色源绑定。
- [x] 相关PG/HTTP/契约故障边界与集中审查。
- [ ] 三仓远端采用、准确分支处置、官方finish释放。

正式24/375DONE、2IN_PROGRESS、349PLANNED；0/125；247NOT_RUN+41REFERENCE_ONLY不变。

## 实际交付

Main原Bearer和认证根锁下新增独立人员上下文签发，绑定已提交C02用户及全部已分配角色（含禁用/零授权）、nonce/epoch/bundle/实际site-form/用途；内层冻结R3V15签名原字节不改。CRM使用原保护会话在线HTTP取证，原身份七锁→持久信任→完整源版本/哈希比对，保留当前会话/期限/原xid检查；所有证据CanAuthorize=false，未开放人工受理业务入口。

CRM [PR145](https://github.com/GTX537/CP6.CRM/pull/145)采用 `c3cd69801a13feb9127528e97fdba7020cda50c6`，源 `1346a30431f3d7cabfe64ac3de2547c6400bcb7f`；[单份交付](https://github.com/GTX537/CP6.CRM/blob/c3cd69801a13feb9127528e97fdba7020cda50c6/docs/development/evidence/W119-02/DELIVERY.md)。54项唯一相关本地成功检查：Main12、CRM42，实际Main login/OIDC/HTTP原签名→CRM PostgreSQL持久信任互通；身份/完整角色源/epoch/nonce/用途/期限/撤权/会话CAS及传输拒绝覆盖，必需范围0跳过。 本地证据 D:/CP6-local-test/wave119/foundation/final-validation.json；实际Main HTTP正文 main-human-context-actual-http-193700.json。原失败保留；epoch非空canonical Guid兼容修正与线性全角色比对的最终相关回归通过，未重复实际互通证据。

未改冻结合同、数据库模型/迁移、依赖/工作流。IAM/TLS夹具与隔离PG范围明确，不冒充生产/业务Owner验收。自有PG停止，缓存和失败记录保留，无新junction；无Actions、保护变更、既有环境迁移或部署。后续Owner须当前action/supervisor/dept/字段/精确site-form相交及资源锁后读取PII。W120继续队列/详情，放行拒绝、隐私及发布后继；正式24/375、0/125、247NOT_RUN不变，七天ACTIVE。
Handoff [PR95](https://github.com/GTX537/CP6.AI-Handoff/pull/95)采用 `13bfc6a61e3da7a6cf6c5d4b632a4e12cde3b609`，准确分支已删除、DEV-2已finish释放。 Main最终PR/finish尚待；START后墙钟27.31分钟，原估70–110分钟，最终按Main finish计，不叠加各仓；不含先前只读调查。

本提交时Main最终合并和席位收尾待；以官方finish与final_proof作最终证明，下轮统一核销，不额外创建记账分支。
