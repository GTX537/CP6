# W177-01 受信审批接收、准确绑定与同事务启动意图

COORD单人继续原CP6-7D-20261022。Main基线`32037b9cb577e927e209c41bcfc615a20dd7f308`，固定DEV1/DEV2复用。W176已完整收尾，本轮只归档一次并关闭WP10-48.wave176_approval_intent限定记录；原七天出口和整卡状态不缩小。

## 实际交付

新增受信服务接收`POST /internal/oa/app/v2/submissions`和本服务原命令查询`GET /internal/oa/app/v2/commands/{commandId}`。复用C02.Services的RS256/JWKS bearer验签，以当前部署注册将client映射为Tenant/Principal/Environment，认证后才解析业务DbContext；正文Scope仅用于准确比对。当前ServiceToken原记录在同一PG事务中FOR SHARE校验登记、期限、撤销和当前租户。没有服务注册时默认关闭；不以浏览器用户或伪造真人会话代替服务。DEMO仅允许显式开发配置，且必须与Owner登记模式一致。

`ApprovalIntake`部署配置包含Enabled、Version、EnvironmentId、EvidenceMode（默认REAL）及Clients（ClientId/TenantId/PrincipalId/CanSubmit）；必须另有当前有效的CrmOidc与CrmIdentity服务认证注册，以及同Scope的审批Owner登记与有效绑定。本轮未写入真实企业服务、密钥或默认政策。启用这一接收组件只允许保存待启动意图，不声明完整审批运行资格。

严格解析oa.app/2.0封闭原包：拒绝未知/重复字段、非法Unicode、非标准UUID/Base64、错误快照摘要和工程联合；保留原字符、精确BusinessRevision、摘要域和数组顺序。通过W176实际不可变Owner记录核验Subject/Policy/发起人/原授权Ref与原快照，当前提交者和政策发布人的对象/字段/来源授权均重新核验。后台来源权限由原记录中的身份取得，不依赖人类登录会话；末端实物变化使整个接收事务回滚。

三层比较分别保存完整请求、SubmissionCore和IntentCore。原命令同体返回持久原Ack，同键异体409；新命令同Submission仅允许commandId/ownerOutboxId变化；不同Submission同意图只有完整IntentCore相等才生成直指原实体的alias，reason不覆盖原文、不形成alias链。新命令均按权限门→命令→绑定头→意图→提交/alias锁序；已接收提交和别名不因后续绑定停用而重新选流程。仅查原命令不要求原送审写权或原政策仍有效。

新Submission按接收时刻的唯一有效时间区间选择绑定，准确保存BindingRevision、TimelineRevision和不可变Published版本；EXACT_PINNED使用指定版本，动态模式仅在首次接收解析一次当前Published。规则支持原数组顺序首次命中和排他命中；多命中或求值错误明确失败，不能改走fallback。数量条件使用精确有理数运算，避免double舍入误选；原有旧表达式求值器保持不变。

Command、Submission、StartOutbox与授权/选择审计同一PostgreSQL事务。复合外键绑定准确Scope/Owner/版本，双向延迟外键要求启动原包与命令同次存在；原包、命令、alias和启动意图不可改删截断。StartOutbox保存原始规范包及稳定engineStartId=submissionId。成功201/重复200均明确DISPATCH_PENDING且InstanceId=null，不调用旧引擎、不产生报废或库存效果。

## 验证与审查

证据：`D:/CP6-local-test/wave177/foundation/final-validation.json`。最终19项本地检查全部通过：18项PG18/HTTP及1项精确数值规则，0skip。

- 原Owner冻结→接收→准确版本→原子启动意图→原命令恢复；不存在引擎实例或库存副作用。
- 双连接同意图竞争、三层比较与直接alias、绑定停用后原结果、新命令复用、丢失commit回应及撤写权后的只读恢复。
- 接收时刻恰好切换到新时间线、动态Published解析、固定版本、排他冲突、求值错误与原数组优先顺序。
- 外来Scope、未知字段、伪造原授权、禁用绑定、最终服务权限变化及接收写入后的实物晚写全回滚。
- 真实Kestrel/controller/C02 bearer/RS256验签和PG当前令牌检查，覆盖缺凭据、未登记令牌、真人sub、伪Tenant、正文串租户、201、原查询、令牌撤销和新令牌恢复。信任密钥、服务映射、未来接收时钟及Owner权限目录是明确测试夹具；该组合宿主不冒充已启用完整Main部署或企业身份验收。原Main实际Program/login/冻结API回归同时通过。
- 既有8项Tool approval回归在最终运行中实际重跑通过。前端未修改，不重跑UI或前端构建；后端随本次测试实际编译。

首次14项运行12通过、2失败，原日志/TRX完整保留：测试构造未知字段时TrimEnd误删嵌套括号，以及测试复用了已有commandId却期待Submission层冲突。修正输入后验证实际预期边界。任务级集中SPEC/QUALITY发现并补齐所有新命令锁序、服务消费时的原政策发布人来源权限和末端实物重检；专项回归均通过。没有通过删除风险场景或把失败改为成功来完成验证。

## 剩余范围

本轮只完成WP12-06的实际WMS Owner接收TxS子交付。Owner Outbox的实际服务投递worker、TxE原子实例/任务/token/history启动、TxD真实决定/四轴证据/DecisionOutbox，以及Owner Inbox、业务应用与ApplicationReceipt仍必须继续。尚无从Owner worker到引擎再到业务回执的完整链路；其他业务Owner/工程对象的真实证据适配不能以本轮WMS验证代替。

正式ROU/BOM/WO/SCHED、D2补货/退货/调拨、D5/D6真实环境与外部来源、D7最终集成仍开放。跨Owner实物义务保留UNKNOWN，不能凭审批接收终结报废。无247正式运行验收、Actions、既有环境迁移或生产部署；整卡和125完整目标不提升。

## 收尾快照

Handoff正常合并：https://github.com/GTX537/CP6.AI-Handoff/pull/153；source `3195cb686c844f553bd07de84e13eaf1c896604f`，目标`227123e1f3ee300bf473bea611d5c1406b7f2e35`，精确树采用已核对，准确任务分支已删除、DEV2已finish释放。目标仍为codex/t02c-entry-handoff-20260921。

本报告提交时Main待正常PR/远端采用/finish，最终以`wave177-final-verification.json`和checkpoint为准，下轮一次归档。预计120–210分钟；截至报告登记46.4分钟，最终Main START→finish计一次，Handoff不叠加。固定目录、有用缓存及主目录用户原件保留。

下一步继续原Owner实际投递与TxE/TxD/业务回执，保持原工程与七天全部出口。
