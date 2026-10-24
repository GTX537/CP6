# W118 公开询盘 POST BFF、表单与回执页面采用

COORD本聊天单人，七天持续目标ACTIVE；PostgreSQL唯一新增数据库。W117 Main PR529 `7bf2e54083400b0cea6fba228426932215feb595`、CRM PR143 `24f5931b51d7c1f8fbc42cc8b05e24fd7575c974`、Handoff PR93 `eb641466f17e818b009f37e1fbd18b0ab83d9fb1` 均已核对远端采用、准确分支清理及官方finish，全部席位释放；登记101.09分钟，前期只读调查/最终核对另记。不重复W117。

## 范围和直接前置

实际原生公开提交/H01 Owner、持久原始签发和回执、Main真实签名服务上下文已由W117采用。当前源码仍只有GET BFF；PublicInquiry使用旧DTO，提交夹带attemptId/csrfToken并读取旧reference，最大长度也未遵守原Unicode合同。本轮只补齐真实浏览器入口：精确公开POST路径、inquiry-v2签名、原始正文和Cookie传输，严格201/PublicReceipt与公共Problem、回执cookie/ETag/header边界；不把浏览器身份作为权限来源。

表单和回执页面采用冻结R3V15的PublicFormView/PublicSubmissionRequest/PublicReceipt与严格JSON解码。七字段提交，不新增If-Match或身份头；字段按allowedFields及Unicode标量约束处理。未知结果/响应丢失保留同一正文和幂等键，按服务端时间/Retry-After等待后由用户重试；过期/表单更新显式重新打开、保留安全草稿并重新同意，禁止自动提交。访问丢失清敏感状态；回执使用displayReference和不含秘密的opaque URL。公开数据不存localStorage/sessionStorage/URL/log。

只修改CRM受影响页面/BFF和定向测试及必要文档；Main负责本轮唯一台账/四状态同步与W117生命周期核销，Handoff一次START和交付。无DB/schema/依赖版本变更，无旧管理/隐私Owner或真实Outbox发布者冒充采用；公开端到端本地夹具不代替生产/125目标/247正式验收。

## 任务与实际执行时间估计

|任务|预计实际执行|
|---|---|
|W118-02 公开POST BFF签名、响应/cookie与错误边界|35–50分钟|
|W118-02 冻结合同表单/回执、草稿恢复、稳定重试与限流|65–100分钟|
|W118-01/HANDOFF 定向浏览器/实际原生PG链路、集中审查、三仓合并清理|45–70分钟|

合计145–220分钟；登记起点`2026-10-24T18:51:07.248923+00:00`，不叠加仓库时间。固定DEV-1/DEV-2串行复用；未授权Actions/既有环境安装迁移/部署/保护修改。

- [x] 公开POST BFF完整传输与失败边界。
- [x] 真实表单和回执页面、未知结果稳定重试和显式恢复。
- [x] 相关本地验证、一次完整SPEC/QUALITY审查。
- [ ] 三仓远端采用、准确分支清理和官方finish释放。

正式24/375 DONE、2IN_PROGRESS、349PLANNED；0/125；247NOT_RUN+41REFERENCE_ONLY不变，七天ACTIVE。

## 实际交付

既有原生开关下公开POST BFF与冻结合同表单/回执已采用；七字段请求保持原合同，Unicode标量上限与精确版本保留。成功检查原body/ETag及删除attempt、受保护receipt cookie；身份头、额外字段/错误期限/弱cookie拒绝。未知结果与429保留原正文/键并显式重试；更新/过期显式重新打开与重新同意，访问丢失清空，旧签发请求取消、迟到响应不覆盖。页面no-referrer且无敏感浏览器存储。

CRM [PR144](https://github.com/GTX537/CP6.CRM/pull/144)已采用 `b0690c662b443b5d327eb9163627a9883ac4ee62`，源 `390fae2ad2238c3f14e7c142f6f0352eb5e3e7d1`；[单份交付](https://github.com/GTX537/CP6.CRM/blob/b0690c662b443b5d327eb9163627a9883ac4ee62/docs/development/evidence/W118-02/DELIVERY.md)。48项唯一相关本地成功检查：38 Web、10真实浏览器/原生HTTP与PG场景；最大Unicode/精确版本、原请求重放/坏响应、限流、迟到清理、回执及显式恢复覆盖，无必需跳过。UI过期/更新/拒绝注入范围明确，不冒充数据库过期验收。 证据 D:/CP6-local-test/wave118/foundation/final-validation.json。失败日志和修正保留；最终拒绝正文/倒计时修正的相关Web回归已通过，其余真实浏览器证据复用。未改Main业务、后端/schema/合同/依赖版本，原生事务与实际Main签名证据复用W117。

页面配送和IAM为明确夹具，不称Next完整SSR/生产TLS/部署验收；后续管理/隐私Owner新格式采用、实际Outbox发布者和正式验收继续。自有PG已停止，数据/缓存/失败截图保留，无新junction。未运行Actions/修改保护/迁移既有环境/部署。正式24/375、0/125、247NOT_RUN不变，七天ACTIVE。
Handoff [PR94](https://github.com/GTX537/CP6.AI-Handoff/pull/94)已采用 `c7fc5bdfb99bf8b13f7d8b89dd40861a784fde3a`，准确分支删除、DEV-2 finish释放。 Main最终合并/finish尚待；当前START后墙钟24.63分钟，原估145–220分钟，最终按Main finish计，不叠加各仓。前期只读调查不包含在此登记计时内。

本提交时Main最终合并/席位收尾仍待，官方finish和final_proof为最终证明；下轮真实开工统一核销，不单独建记账分支。
