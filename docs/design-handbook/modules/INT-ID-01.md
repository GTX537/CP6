# INT-ID-01 Main 与 CRM 身份投影合同

## 1. 业务目的、操作者与权威边界

Main 是租户、组织、用户、角色权限及撤权事实的权威；CRM 保存投影、发布后的授权读模型和本地会话；Platform 提供运输、Inbox、checkpoint 等原语。租户 IAM 管理员在 Main 修改身份，CRM 用户在已绑定组织登录；投影服务取可信 Main 快照，应用 worker 在自己的事务消费。不能在 CRM 新建主身份、从 displayName 推导 ID、开第二身份消费者或把 Platform 当身份主库。 [规范 L9](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:9>)、[规范 L99](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:99>)、[规范 L124](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:124>)、[规范 L126](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:126>)

覆盖五条原 SPEC：租户／组织映射、用户权限版本、撤权、本地会话、重放与对账。身份健康状态和业务授权是两个轴：READY 不自动授予资源权；UNAVAILABLE 不表示用户已经撤权。 [规范 L138](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:138>)、[规范 L273](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:273>)、[规范 L305](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:305>)、[规范 L338](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:338>)、[规范 L368](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:368>)、[规范 L393](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:393>)

## 2. 当前有效组合、接受与历史边界

本册采用 S6 v0.2 的同一份完整正文，两个 Target 各保留原五条 SPEC。正式 CURRENT 是 Stage 100 的 Markdown 静态详设接受；作者包中 STOPPED／NOT_ACCEPTED 和旧 v0.1 RETURN 保持历史原样，不能用它们覆盖后继接受。接受人是用户授权的 root／COORD，记录明确 `UserPersonallySigned=false`，也不授权实现、测试或上线。[CURRENT L4](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a5/a552e6b33092ccd6__CURRENT.json:4>)、[UA L181](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/02/028deecc79c18039__UA-20261008-S6-INT-ID-MD02.json:181>)

| 材料 | SHA-256 | 作用 | 入口 |
| --- | --- | --- | --- |
| 共同正文 | d9b57a039e18a689615cfb48e181bb248dedecf957d8bdd7dd72d85a53a6dccc | 当前十条 SPEC 和共同接缝 | [原件 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:1>) |
| 定点复审 | a9515be614e35dd514b18c59f8748350a3dc679cacdf55a38c93817f8c41cbbe | B01–B04 静态关闭；真实 Owner 采用未证 | [原件 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a9/a9515be614e35dd5__S6_身份与集成异常_v0.2_定点复审报告.md:1>) |
| 本目标 CURRENT | a552e6b33092ccd6b1b2b66d5efa97d18ecd883175ba2af123010272ece445de | 冻结正文、UA、独审、接受范围 | [原件 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a5/a552e6b33092ccd6__CURRENT.json:1>) |
| 本目标 UA | 028deecc79c180398e8a09858cc7be9fabce917972ce95e19be32f890e0b3ab2 | 重建既有接受记录；不是遗失临时 UA 的同字节副本 | [原件 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/02/028deecc79c18039__UA-20261008-S6-INT-ID-MD02.json:1>) |
| 共享来源清单 | 386d6787b61a21b0c1df6be81bb9b089977232aa76687b7b2bce34a82e11c49c | S6／X1 历史及原件字节身份 | [原件 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/38/386d6787b61a21b0__SOURCE-MANIFEST.json:1>) |

原 S6 v0.2 ZIP SHA-256 `88e7050f62a7ca4b87ee140aeba0badf0659cda626790e9cf167de9965b0f80b` 共 125 个成员已只读展开到缓存；其中 99 份固定 SHA 源文本的存在不等于本轮全部语义阅读。作者原统计 38 full／26 excerpts／2 registration excerpts／33 fetched-not-reviewed 必须作为作者历史覆盖保留。[包索引 L18](<D:/CP6-archives/consolidation-20261010/commercial-cache/s6/S6/INDEX.md:18>)

70 项新 AC 全部 NOT_RUN；29 项任务仍 PLANNED_NOT_AUTHORIZED；18 项 Required 中有 16 项真实政策、物理映射、实现或 Owner 采用门，另有 native 格式边界与治理项。后继 UA 已关闭 Required-ACCEPT-01 的接受治理状态，当前 Markdown 接受不要求虚构原生 Excel，也不声称原册完成。[UA L192](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/02/028deecc79c18039__UA-20261008-S6-INT-ID-MD02.json:192>)

## 3. 数据、身份键、字段与版本轴

| 对象 | 键／字段 | 必须保持的含义 | 出处 |
| --- | --- | --- | --- |
| 租户与组织 | TenantId GUID＝organizationId；slug、issuer、region 显式注册 | slug 为小写字母／数字／短横线 1–63；仅展示与路由标识，不代替 TenantId；issuer 为准确 HTTPS origin | [规范 L279](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:279>) |
| 组织映射 | sourceTenantId、localOrganizationId、issuer、region、mappingRevision、source／projectionVersion、dependencyState、lastVerified | mappingRevision／批准生效信息是候选映射合同，不声称已有表 | [规范 L279](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:279>) [规范 L290](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:290>) |
| User | tenant／user／dept、enabled、version、roles、manager、authEpoch、mustChangePassword、validAfter、passwordExpiry、lockedUntil | 各字段来自 Main 原事实；authEpoch、aggregateVersion、会话有效期分别判断 | [规范 L311](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:311>) |
| Permission | tenant／subject、enabled、version、grants(resource,action,scope,deptIds,fields[field,access]) | actions 合并、部门范围及字段决策不可混为一项权限 | [规范 L311](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:311>) |
| Snapshot／Projection | tenant+aggregateId，单调 int version，eventType、data bytes hash、tombstone | 同版异 data／类型是冲突；原版本和删除事实不由消费者重编 | [规范 L101](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:101>) [规范 L102](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:102>) |
| 会话 | 服务端随机 cookie 的 hash；tenant+subject+issuer+jti+epoch；created／lastActivity／token expiry／Version | 浏览器不能提交 actor／role／tenant 覆盖；touch 用原存储版本防止已删除会话复活 | [规范 L103](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:103>) [规范 L374](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:374>) |
| 撤权 | issuer+jti＋准确 tenant／sub；原 epoch／expiresAt；revokedAtOrigin | RECEIVED_EVENT、SOURCE_SNAPSHOT、LEGACY_UNKNOWN 分开；不从过期时间倒推撤权发生时刻 | [规范 L116](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:116>) [规范 L344](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:344>) |
| Main 对账边界 | opaque generation boundary＋tenant-bound cursor | 只比较等值，不解释为 CRM 全局水位；snapshot version 与此轴不同 | [规范 L115](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:115>) [规范 L401](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:401>) |
| 字段决策 | resource/action、policyVersion、sourceRoleVersions、mappingVersion、expiresAt/decisionId；字段 canRead/canWrite | 决策覆盖 DTO 嵌套／集合、导出列和搜索索引，禁止未知字段扩大权限 | [规范 L134](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:134>) [规范 L323](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:323>) |

两种摘要必须保真：`dataPayloadSha256` 对原 data JSON 文本字节；`rawEnvelopeSha256` 对真实 CloudEvent UTF-8 载体。包装不同可有不同 raw 而同 data；不能重新序列化 data 后假装原字节一致。版本、摘要和来源标记共同保存，运输 messageId 不成为身份业务键。 [规范 L110](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:110>)、[规范 L111](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:111>)、[规范 L113](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:113>)

## 4. 五条正常流程、状态与写集

### 4.1 租户／组织映射

启用前核 Main OIDC 组织、identity mapping、服务 client、CRM storage 四处 tenant／issuer／region 一致。未知或矛盾为 UNCONFIGURED；登录 transaction 绑定 slug 已解析的 tenant。缺祖先部门或主体依赖禁用时拒绝授权，不用默认组织或跨 region 回退；仅无关可选角色不可用时移除该项。slug 改名保持稳定 ID，issuer／region 变更要新映射与会话兼容评估。重新启用使用新版本并重新认证，旧会话不复活。 [规范 L283](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:283>)、[规范 L284](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:284>)、[规范 L285](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:285>)、[规范 L286](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:286>)、[规范 L288](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:288>)

用户切组织先退出本地会话再走新登录。组织不存在 404；依赖不可用 503；不同 tenant 的 cursor 拒绝。诊断映射视图只读，不提供 CRM 端修改 Main 组织的后门。 [规范 L290](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:290>)、[规范 L292](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:292>)、[规范 L294](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:294>)

### 4.2 权限投影

Main writer 从同一业务事务捕获最终身份事实，写 snapshot／版本／outbox／审计；既有全 writer 覆盖仍须实证。CRM 每请求取 online actor ∩ 健康 published projection，再交对象行范围和字段策略；失效角色不能贡献 grant。消息已接收只证明事实进入，不等于已授权；依赖 503 和真实政策拒绝 403 分开。 [规范 L314](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:314>)、[规范 L317](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:317>)、[规范 L318](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:318>)、[规范 L321](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:321>)

身份状态按 UNCONFIGURED → BOOTSTRAPPING → READY 建立基线；内容冲突进 DRIFT，依赖缺失进 UNAVAILABLE，确定禁用／token／epoch 不合格才 DENIED。修复前不让旧 published 默认为允许。具体同事务冲突接入见第 6 节。 [规范 L138](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:138>)、[规范 L234](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:234>)、[规范 L239](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:239>)

### 4.3 撤权

密码、二次认证和明确外部 epoch 变更使相应 token／会话失效；普通 refresh token 替换不能擅自解释为整族终止。Main 撤权事实与 snapshot／outbox 同事务，优先投递队列独立于普通队列；CRM 一次事务登记 RevokedTokens、删准确匹配的 session、撤出 published。较低 enabled 消息或账户恢复不能清掉旧 token tombstone。 [规范 L347](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:347>)、[规范 L348](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:348>)、[规范 L349](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:349>)、[规范 L351](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:351>)、[规范 L353](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:353>)

重复真实事件可补有证据的 origin，不能误删新会话。在线故障返回 503，不能伪造“已撤权”。当前代码 expiry+60 秒保留不是批准的保留政策；源提交→消费者应用→真实最终门拒绝分别计时，尚未证明撤权 SLO。 [规范 L344](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:344>)、[规范 L350](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:350>)、[规范 L353](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:353>)

### 4.4 本地会话

Begin 创建绑定组织的登录状态；状态 10 分钟、一次性消费；交换 code 后校验 token／组织，在线身份和 projection 都通过后才索引服务端会话。每请求重新走两层判断及对象／字段权限。会话过期准确取 `min(token expiry, created+8h, lastActivity+30min)`；touch 只推进 idle 候选，不延长 token 或绝对 8 小时。projection 新鲜窗口 20 分钟是另一个条件。 [规范 L372](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:372>)、[规范 L374](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:374>)、[规范 L376](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:376>)

本地 logout 先清会话，再独立报告上游 logout；503 时仍允许退出。401 无效认证；业务 403 不等于全部会话撤销，但在线认证上游 401／403 会清会话；409 不自动登录。CSRF 绑定会话，不向 DTO 或 LocalStorage 暴露 access token。C02.Services 已注册不证明浏览器会话链已启用。 [规范 L378](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:378>)、[规范 L380](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:380>)

### 4.5 全量基线对账

coordinator lease 2 分钟、执行 deadline 90 秒、轮询 5 秒；正常间隔 15 分钟，故障退避 5–300 秒。第一次完整 versions 目录每页 ≤200、同 tenant／boundary、aggregate 唯一、cursor 不重复，最多 100 万项；读取缺项、不同版／hash或 DRIFT 对应准确 snapshot。高版本健康投影不回滚；同版冲突只由可信 Main selection 解决。 [规范 L400](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:400>)、[规范 L401](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:401>)、[规范 L402](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:402>)、[规范 L403](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:403>)

最后再取第二个完整目录，boundary 与条目集均一致才 Complete，变化则 409／重启。成功保存 LastSuccess、boundary 和 SOURCE_SNAPSHOT origin，不伪造已收消息；失败保留旧 LastSuccess，不能延长新鲜度。源遗漏进入 drift 而非直接删，失 lease 不能 Complete，新 worker 从完整目录重启；调和和消费共同锁／source CAS 防止旧 Complete 清除新冲突。手工按钮只调度既有 coordinator，无强制 READY。 [规范 L404](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:404>)、[规范 L405](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:405>)、[规范 L407](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:407>)、[规范 L409](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:409>)

## 5. 现有 API、候选接口和 Owner 合同

下表前四项为正文固定源码事实；MappingView 等诊断视图是设计对象，不能据其名称宣称已存在生产路由。

| 接口／通道 | 输入与响应规则 | 出处 |
| --- | --- | --- |
| GET /internal/crm/identity/versions | tenant 来自服务 token；cursor/pageSize 1–200；TenantId、Boundary、Items、NextCursor；400/409/503/401/403 | [规范 L154](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:154>) |
| GET /internal/crm/identity/snapshots/{aggregateId} | aggregateId 1–128 且允许字符受限；无任意 Tenant 查询；404 不能变成“空权限权威” | [规范 L155](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:155>) |
| POST /internal/crm/identity/events | sidecar 固定 topic；SUCCESS/RETRY/DROP；消费本身不生成允许 | [规范 L156](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:156>) |
| CRM auth workspace/session | CSRF、no-store；组织／token 绑定不从客户端改写 | [规范 L157](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:157>) |
| cp6.platform.events.v1 五个身份 v1 事件 | 保持旧 schema v1；service/tenant/region、contract-bundle、单 route epoch 准确注册 | [规范 L655](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:655>) |
| MappingView／FieldDecisionView／RevocationStatusView／ReconciliationStatusView | 分别显示映射、字段决策、撤权来源与对账健康；准确字段及候选边界分别见原文 | [规范 L290](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:290>) [规范 L323](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:323>) [规范 L355](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:355>) [规范 L409](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:409>) |

Platform 新 seam 的输入包含 tenant、consumer、aggregate、version/type、原 data/raw 摘要、originalMessage（topic/partition/source）、schema/bundle/region、实际注册与实际事务。其责任是 pre-checkpoint 内容门，不拥有 CRM 提交权，也不是第二消费者。 [规范 L220](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:220>)、[规范 L222](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:222>)

## 6. 事务、幂等、冲突与未知提交恢复

CRM 应用持实际数据库事务，调用候选 `ICp6InboxPreCheckpointV1`／`ProcessWithPreCheckpointV1`。Platform 所有 Duplicate／IgnoredOutOfOrder 的成功早退前都必须进入该 seam，不能只在旧业务 callback 中比较同版 hash。比较可信 Projection／Main lineage；辅助观察是证据，不成为新的身份权威，缺 anchor 返回 DependencyUnavailable。 [规范 L220](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:220>)、[规范 L224](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:224>)

共同顺序：注册 guard → ReconciliationState → Published → Projection → Inbox → checkpoint／observation；reconcile／publisher 同样参与。冲突记录、DRIFT、published 移出 allow、Inbox 分类与调和调度必须同提交。提交前不向 Dapr 返回 SUCCESS；隔离持久化后 DROP／CONFLICT_QUARANTINED；依赖或事务失败 RETRY，未知提交先查原记录。 [规范 L227](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:227>)、[规范 L234](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:234>)、[规范 L237](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:237>)

| 判定 | 效果 | 出处 |
| --- | --- | --- |
| TransportIdentityConflict | 同 message 的 raw／绑定变化优先判冲突，只影响可信 aggregate，不能使用攻击者 tenant | [规范 L229](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:229>) |
| FreshCandidate | 较高版本／来源缺口进入正常验证；无基线时不先 allow | [规范 L230](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:230>) [规范 L224](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:224>) |
| TransportDuplicate | 不重做撤权副作用；当前更高健康版不能证明所有历史内容无冲突 | [规范 L231](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:231>) |
| OlderVersionIgnored | 不回滚，但已知同版本异内容冲突仍留存 | [规范 L232](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:232>) |
| SameVersionEqual | data 相同，独立合法 transport 包装可不同；不能清 DRIFT | [规范 L233](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:233>) |
| SameVersionConflict | eventType／data／可信来源选择冲突，追加证据、DRIFT、撤出 published，禁止最后写覆盖 | [规范 L234](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:234>) |
| DependencyUnavailable | 不写成功 checkpoint／allow，查询提交真相后才返回成功 | [规范 L235](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:235>) |

恢复只能从 Main 可信完整 baseline 追加 AuthoritySelection／resolvedConflictRefs，并在共同锁／source CAS 下确认没有新冲突。缺 pre-checkpoint 能力时关闭相关新授权及依赖新写，返回 `IDENTITY_CONSISTENCY_GATE_UNAVAILABLE`；可信事实可 PendingInspection，合规历史读取可以继续。 [规范 L239](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:239>)、[规范 L241](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:241>)

最终授权逻辑 adapter 为 `OwnerFinalAuthorizationGateV1.EvaluateAndHold`。输入必须包含可信 Owner、tenant、subject／actorKind／requester、action、targetVersion、原 operation、expected epoch、scope／field policy 和实际事务；输出为 `ALLOWED_HELD`（含 fenceRef、epoch、字段证明和绑定事务）、`DENIED` 或 `UNAVAILABLE/AUTHORITY_FENCE_UNAVAILABLE`。布尔校验、TTL、二次 GET、lease 都不能替代持门到提交。 [规范 L245](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:245>)、[规范 L247](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:247>)

同库顺序为 Owner 的 ScopeAuthorizationFence → Operation 根 → 控制／队列 → 业务写集 → Receipt／Outbox → Audit；角色、委派、SoD、政策、租户禁用、撤权、worker 与管理入口所有相关写者必须参与同一门。撤权先完成则旧权限新效果拒绝；业务先持门提交则保留该历史事实。单独新增 adapter 有锁不能证明采用。 [规范 L249](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:249>)

Main 与 CRM 分库时，在线 Main GET、CRM 投影和 CRM 本地事务不是共同安全门。没有已采用等价协议时关闭相关新业务效果及 post-commit bridge 的新下游效果，保留原意图为 `BLOCKED_WAITING_AUTHORITY` 并继续原操作查询；INT-COM 有限业务用途 grant 不能替代安全门。这里消费既有 OA-APP §15.1 与 INT-COM R1 §2.3–2.5，未重新发明 IAM 政策。 [规范 L245](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:245>)、[规范 L251](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:251>)

| 操作类别 | 当前所需门 | 不能误用 |
| --- | --- | --- |
| 历史结果读 | 当前认证、tenant、对象／字段读取范围 | 不要求旧 eligibility、旧 ETag 或新写权；返回字段按当前权限 |
| 可信身份事实接收 | 当前注册／route 与 PreCheckpoint 内容一致性门 | PendingInspection 不形成 allow |
| 原已提交结果的纯技术投递 | 当前机器／操作员 delivery fence | 不重判原人类 create 权；接收者新业务效果另受其门 |
| 新业务 replay／bridge 下游效果 | 实际 Owner 最终安全门＋相应用途／目标版本批准 | 不得把历史 read 或原 grant 当续写权 |

上述四分支保持原 operation／原输入：恢复不换 ID，不因旧 token 无效创造新请求。 [规范 L256](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:256>)、[规范 L257](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:257>)、[规范 L258](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:258>)、[规范 L259](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:259>)、[规范 L261](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:261>)

## 7. 租户、行范围、字段与敏感信息

服务端从注册 client／登录组织绑定 tenant、issuer、region，不接受客户端替换。普通用户不自动拥有死信或 payload 权，投影 service 不代理用户决策，worker 不沿用旧 payload 中的 UserName 作为认证人。所有资源操作采用 online actor、健康投影、对象行范围、逐字段策略交集。 [规范 L124](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:124>)、[规范 L125](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:125>)、[规范 L126](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:126>)、[规范 L130](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:130>)、[规范 L132](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:132>)

保留 Main 当前显式字段合并 MIN：1 读写、2 只读、3 隐藏；没有配置时的默认语义由 S5 确认，不能从字典缺 key 推导允许。隐藏字段不下发；只读／隐藏字段写入返回整个受控拒绝，不静默部分成功。旧 Main StripReadOnly 的兼容行为单列；PII 总开关与字段策略取更严格交集，不允许自定义字段恢复匿名化资料。 [规范 L134](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:134>)

资源 Owner 必须登记准确 Main 字段→DTO（含嵌套／集合／导出／搜索）映射。未知字段／Access／缺政策依赖为配置未就绪，相关资源 RequiredFenced；当前历史结果也按新权限重新投影，而不改写原事实或 hash。 [规范 L134](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:134>)、[规范 L323](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:323>)、[规范 L327](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:327>)

## 8. 页面与服务端校验

映射诊断页展示源／本地组织、issuer/region、映射修订、源／投影版本及依赖；字段诊断显示可读写与政策版本；撤权页显示真实来源、源提交／消费者应用、受权范围内会话计数和待处理消费者，不显示 token／cookie。对账页显示 source boundary／lastSuccess／lease／health／safeCode，按钮仅调度。 [规范 L290](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:290>)、[规范 L323](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:323>)、[规范 L355](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:355>)、[规范 L409](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:409>)

失去字段／对象／会话权后清已加载数据、缓存和下载入口，不把隐藏值保留 DOM。Back、多 tab、迟到响应都绑定当前会话／租户及请求序列，防止旧数据回显。Loading／Empty／Forbidden／NotFound／Conflict／Unavailable／UNKNOWN 分开；焦点和 aria 状态可访问，不设置全局 Platform“强制放行”开关。 [规范 L325](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:325>)、[规范 L377](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:377>)、[规范 L709](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:709>)

## 9. 实施顺序与固定代码差异

以下是正文在固定源码版本上记录的实现事实，未在本轮刷新远端 HEAD，也未执行这些源文件：Main `90c871fe571fd6b390f53e8678376d7ce60bcb60`；CRM `c778a3052a4416b82facb07b1244fc98fb6ad8a1`；Platform `30bd23af6808d217a23878bd9437513043c52834`。[规范 L25](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:25>)

| 证据 | 固定源码位置／范围 | 已观察事实／复用 | 仍不能证明 | 原文 |
| --- | --- | --- | --- | --- |
| E01 | Main CP6.Core/Services/CrmIdentity/IdentityEventContracts.cs L12–33,52–62 | 固定topic、五种身份事件、租户/aggregate/version/相关键、原数据DTO | 生产Topic部署、所有租户启用 | [规范 L50](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:50>) |
| E02 | Main IdentityChangeCapture.cs L17–111；CP6Context.cs L2780–2853 | 用户/角色/部门/权限/会话族变更捕获，业务与身份及审计同事务/保存点 | 所有非EF或批量写入口已纳入；需S5完整writer清单 | [规范 L51](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:51>) |
| E03 | Main IdentitySnapshotWriter.cs L18–151,199–205 | 锁住原aggregate，最终事实生成快照；hash相同不加版本；禁用/token撤权进入优先队列，调用方事务 | 真实并发和故障下的运行保证 | [规范 L52](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:52>) |
| E04 | Main IdentitySnapshotReader.cs L21–79；CrmIdentityController.cs L23–79 | versions/snapshot、tenant-bound受保护cursor、10分钟、pageSize1–200、generation变返回409；服务client显式授权 | 实际服务注册、跨仓基线跑通 | [规范 L53](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:53>) |
| E05 | Main CrmIdentityTenantGenerationV1迁移 L13–32 | TenantId主键、非负Generation、seed/trigger、forward-only | 迁移在目标数据库执行或触发器覆盖所有writer | [规范 L54](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:54>) |
| E06 | Main CrmIdentityConfiguration.cs L15–77；Program.cs L86,95,527,781–795 | 条件绑定/注册，独立C02.Services认证profile，原默认scheme保留 | 运行配置Enabled=true；源码注册不等于host当前启用 | [规范 L55](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:55>) |
| E07 | Main IdentityEventDispatchWorker.cs L14–25,44–90 | 普通/优先独立循环，batch32/16、lease5分钟、max10，原CloudEvent bytes发布 | broker、运行告警及撤权延迟SLO | [规范 L56](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:56>) |
| E08 | Main CrmOidcDirectory.cs L29–35,94–154；PermissionAggregator.cs L18–29,73–113 | slug→Main tenant；在线active身份；停用角色排除；action并集、scope MAX、字段显式MIN | 部门集合任意并集语义；逐字段在线DTO交付 | [规范 L57](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:57>) |
| E09 | CRM CrmAuthenticationService.cs L37–117,120–180 | 一次性登录state、服务端会话，每请求online reconcile，再投影校验；401/403删会话，503不当撤权 | 每个业务入口均在同一请求时点完成字段级授权 | [规范 L58](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:58>) |
| E10 | CRM IdentityAuthorizationPolicy.cs L15–79,82–130；Reader.cs L27–60 | baseline20分钟、grant交集、部门收窄、依赖健康、无跨请求allow-cache、published核对 | fields逐字段执行，不是仅凭存在FieldsJson完成 | [规范 L59](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:59>) |
| E11 | CRM IdentityProjectionStore.cs L25–81,84–136；IdentityEventConsumer.cs L36–95 | Store局部具备老版本忽略、同版异hash/缺口标漂移，实际进入回调后同事务投影/会话删除/published | Platform版本checkpoint早退可能不进入Store；端到端同版冲突判定UNPROVEN | [规范 L60](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:60>) |
| E12 | CRM ReconciliationWorker.cs L20–65,84–168；Client.cs L24–116 | 租约、分页baseline、逐项一致、最终重读boundary+目录、15分钟间隔、失败退避5–300秒 | 把Main boundary解释为CRM自算数字或自造全局时钟 | [规范 L61](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:61>) |
| E13 | CRM IdentityPublishedCompiler.cs L17–101；ProjectionContext.cs；三份identity migrations | 原投影派生published图、依赖齐全、FieldsJson、nullable真实撤权origin | published表成为新权威、旧缺失origin被推断补齐 | [规范 L62](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:62>) |
| E14 | CRM CrmActorContext.cs L4–13；CrmBusinessService.Views.cs L13–68；BusinessService.cs L209–241,298–325,366 | 真实动作/行范围、view-pii掩码、原命令重放读当前可见性；Actor无字段集合 | Main逐字段readonly/hidden从传输到CRUD/导出全链执行 | [规范 L63](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:63>) |
| E15 | Main FieldPermService.cs L12–53；CurrentPermissionContext.cs L17–20,51–92 | 字段1读写/2只读/3隐藏；反射掩码及原值恢复；30分钟滑动权限缓存、invalidate API | 每一授权写入口调用失效；不把滑动30分钟称撤权硬上限 | [规范 L64](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:64>) |

执行顺序：先确定四处映射与全 writer inventory／字段政策，再完成实际共同最终门及 CRM pre-checkpoint 接缝；随后接 session 与 generation／全量对账、UI，最后用真实环境覆盖竞争和恢复。以下为原任务的准确拆分，未表示已获实施授权。

| 任务 | 代码／责任 | 具体工作 | 依赖／验收 | 出处 |
| --- | --- | --- | --- | --- |
| ID01 | Main IAM+S5；CrmOidcOptions/Directory、Identity配置 | 原tenant/slug/issuer/region/client来源矩阵；区分展示改名与身份迁移；收敛全部配置入口 | ID-01-01；Required-IAM/ID；AC-ID01-01/02/04/06 | [规范 L670](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:670>) |
| ID02 | CRM Owner；CrmIdentityProjectionExtensions/Options、登录入口 | 注册与storage组织/region一致性、mapping诊断DTO，未知映射fail-closed | ID-01-01；ID01；AC-ID01全组 | [规范 L671](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:671>) |
| ID03 | Main IAM；IdentityChangeCapture、SnapshotWriter、CP6Context | 完整writer inventory与当前同事务机制复用；补未捕获路径方案，认证失效/role变更区分 | ID-01-02/03；S5writer证据；AC-ID02-01/02、ID03-01/04 | [规范 L672](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:672>) |
| ID04 | S5主导、CRM消费；PermissionAggregator、在线Context DTO | 明确scope与field默认/合并政策，源字段→resource/action/DTO映射及policy版本合同 | ID-01-02；Required-IAM-02；AC-ID02-03/04/07 | [规范 L673](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:673>) |
| ID05 | CRM业务/FE Owner；Actor、AuthorizationReader/Policy、Views/业务mutation | 字段决策贯穿CRUD/list/export/search/历史结果；在真实业务事务消费§3.6既有Owner最终门；分库无等价门关闭新效果；清FE受保护缓存 | ID-01-02；ID04；AC-ID02-04/05/06 | [规范 L674](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:674>) |
| ID06 | Main身份+CRM会话；Priority dispatcher、ProjectionStore/Origin | 精确撤权origin/epoch/token保留；共同fence撤权入口/并发语义及缺门关闭；真实event/snapshot分列与未证SLO | ID-01-03；Required-ID-04/AUD；AC-ID03全组 | [规范 L675](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:675>) |
| ID07 | CRM Auth；AuthenticationService/AuthStore/Endpoints | 每请求online+projection、touch/删除并发、logout独立结果，核全受保护入口 | ID-01-04；Required-ID-02；AC-ID04-01/02/03/04/06 | [规范 L676](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:676>) |
| ID08 | CRM FE；CrmPageState/AppShell/认证页 | 401/403/409/503、组织切换/Back/多tab/迟到response/会话失效交互；禁止旧数据缓存回显 | ID-01-04；ID07接口；AC-ID04-05/07 | [规范 L677](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:677>) |
| ID09 | Main持久化Owner+CRM；SnapshotReader/generation migration、ReconciliationClient | provider-specific generation原理与opaque boundary兼容；cursor/最终目录条件；不改schema v1 | ID-01-05；Required-ID-03；AC-ID05-01/02/03/07 | [规范 L678](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:678>) |
| ID10 | CRM投影/FE；ReconciliationCoordinator、Published、诊断页 | 按§3.5实现CRM持事务PreCheckpointV1 seam调用、锁序/decision、冲突+DRIFT+published原子更新及§4.5恢复CAS；保source-only origin和lease诊断 | ID-01-02/05；ID09/Required-PLT-01；AC-ID02-02、AC-ID05-02/03/04/05/06 | [规范 L679](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:679>) |
| X01 | S6拟稿/S5签认 | 固定authority/tenant/field/版本合同；消费声明精确版本与拒绝/恢复映射 | ID01–05；Required-IAM；不能把讨论当采纳 | [规范 L694](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:694>) |
| X02 | Main/CRM/S7 | 消费OA-APP §15.1及INT-COM R1§2.3–2.5既有门：定义OwnerFinalAuthorizationGateV1 adapter、同库锁序/共同写者、分库缺门关闭和四类权限分支；真实采用取证计划 | ID02/03、OPS03；ID05/06/OP07–09；Required-ID-04/OPS-02；实现未授权/运行UNPROVEN | [规范 L695](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:695>) |
| X03 | Main/CRM/业务Owner | 原结果查询与identity baseline两个不同对账流程/合同窗口 | ID05/OPS05；Required-ID-03/OPS-01 | [规范 L696](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:696>) |
| X04 | 应用发布Owner/S7 | 逐route单consumer/route epoch/旧积压/停旧/回退审阅方案 | OPS03/6.3；Required-PLT-02；不启双写 | [规范 L697](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:697>) |
| X05 | 独立测试/评审Owner | 70AC test design、原测试映射、fixture/生产证据层、缺证与回归范围；独立验收 | 第8节；Required-TEST/ACCEPT；本轮NOT_RUN | [规范 L698](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:698>) |

保留旧表与旧事件 v1；新增来源字段对历史 null／unknown 保真。byte[8] 并发标记必须核目标 provider，不从文书推定所有数据库兼容。route 激活时先明确旧积压 OwnerLookup 与单 writer epoch，旧 in-flight 终态或交接后才切新；回退不撤销已提交业务回执。 [规范 L655](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:655>)、[规范 L659](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:659>)、[规范 L662](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:662>)、[规范 L702](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:702>)、[规范 L703](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:703>)、[规范 L704](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:704>)

## 10. 验收场景与真实验证状态

五组共 35 项原 AC 逐项如下，全部 **NOT_RUN**。给定／动作／结果保持原设计语义；不存在本轮执行记录。

| AC | 准确场景与预期 | 状态 | 出处 |
| --- | --- | --- | --- |
| AC-ID01-01 | 同tenant/region/issuer完整注册后，登录transaction与snapshot投影身份一致；取得三方注册与运行读证才判成立 | NOT_RUN | [规范 L297](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:297>) |
| AC-ID01-02 | 缺映射、空tenant或region不一致时，状态未就绪，无默认组织或新本地主身份 | NOT_RUN | [规范 L298](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:298>) |
| AC-ID01-03 | 源部门祖先缺失/停用时，依赖用户不获取该部门范围；不相关可选目录不扩大权限 | NOT_RUN | [规范 L299](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:299>) |
| AC-ID01-04 | 同TenantId变slug不改历史业务归属；切换组织后旧session不可续用 | NOT_RUN | [规范 L300](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:300>) |
| AC-ID01-05 | 同版本不同映射内容被隔离，保两份原摘要与来源，不覆盖权威 | NOT_RUN | [规范 L301](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:301>) |
| AC-ID01-06 | tenant暂停再恢复产生新权威版本，旧token/session不因恢复自动有效 | NOT_RUN | [规范 L302](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:302>) |
| AC-ID01-07 | 诊断读受tenant/PII范围限制，跨租户对象不暴露存在性，未知状态不显示绿色READY | NOT_RUN | [规范 L303](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:303>) |
| AC-ID02-01 | 单次角色/用户多项变更只发布最终一致事实；业务回滚时快照/outbox/审计无半成品 | NOT_RUN | [规范 L330](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:330>) |
| AC-ID02-02 | 经§3.5前置内容seam再作技术早退：低版不回退、同版同data摘要幂等、同版异摘要隔离；冲突记录/DRIFT/published不可用同commit，失败不先返回SUCCESS | NOT_RUN | [规范 L331](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:331>) |
| AC-ID02-03 | 动作与部门范围按online∩projection收窄；停用角色不贡献grant | NOT_RUN | [规范 L332](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:332>) |
| AC-ID02-04 | 显式字段1/2/3由Main语义映射到CRM DTO；只读/隐藏字段写入拒绝且无部分业务提交 | NOT_RUN | [规范 L333](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:333>) |
| AC-ID02-05 | 列表、详情、导出、搜索、排序、日志与重放返回使用同一当前字段决策；未知映射关闭相关能力 | NOT_RUN | [规范 L334](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:334>) |
| AC-ID02-06 | 同库业务写与所有撤权共同持Owner fence直到commit；撤权先生效旧epoch拒绝。Main–CRM分库无已采用等价门则关闭新效果并保合法历史只读，二次GET/TTL不算闭合 | NOT_RUN | [规范 L335](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:335>) |
| AC-ID02-07 | 无配置字段策略保留明确版本化政策，S5未确认前不推断默认全权；旧API兼容行为单列 | NOT_RUN | [规范 L336](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:336>) |
| AC-ID03-01 | 禁用和撤权事实、优先outbox与Main业务提交原子；失败不返回假成功 | NOT_RUN | [规范 L360](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:360>) |
| AC-ID03-02 | 普通queue阻塞时优先queue仍有独立调度预算；实际延迟证据另验 | NOT_RUN | [规范 L361](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:361>) |
| AC-ID03-03 | token撤权身份精确绑定issuer/jti/tenant/subject，重复真实消息不复活或误删新会话 | NOT_RUN | [规范 L362](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:362>) |
| AC-ID03-04 | 低版启用/权限不能复活高版禁用；恢复需新认证；在途新效果只能经共同最终门，缺门明确AUTHORITY_FENCE_UNAVAILABLE而非沿旧actor续写 | NOT_RUN | [规范 L363](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:363>) |
| AC-ID03-05 | 真实撤权时间只取validated origin，snapshot/历史缺证不推断时间 | NOT_RUN | [规范 L364](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:364>) |
| AC-ID03-06 | 认证服务/投影store故障返回503，不改成撤权事实；logout本地仍可用 | NOT_RUN | [规范 L365](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:365>) |
| AC-ID03-07 | 撤权保留与清理边界需覆盖未到期token/容许skew，Required未确认不执行清理 | NOT_RUN | [规范 L366](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:366>) |
| AC-ID04-01 | 同state仅消费一次；回调组织/issuer不匹配不建立session | NOT_RUN | [规范 L385](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:385>) |
| AC-ID04-02 | 每请求Main online与CRM投影均被执行；任一不可用不fallback到旧allow | NOT_RUN | [规范 L386](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:386>) |
| AC-ID04-03 | session到期严格min(token expiry,创建后8小时absolute,lastActivity+30分钟idle)；touch只推进idle候选边界，且与撤权并发不能重建已删session | NOT_RUN | [规范 L387](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:387>) |
| AC-ID04-04 | 退出在上游不可用时仍删本地session，并诚实显示上游未知 | NOT_RUN | [规范 L388](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:388>) |
| AC-ID04-05 | 组织切换/Back/迟到response不显示上一个tenant数据或权限 | NOT_RUN | [规范 L389](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:389>) |
| AC-ID04-06 | 会话/访问令牌不出现在诊断、日志、URL或FE持久缓存 | NOT_RUN | [规范 L390](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:390>) |
| AC-ID04-07 | 401/403/409/503分别展示，503不无限循环登录也不自动提交旧业务请求 | NOT_RUN | [规范 L391](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:391>) |
| AC-ID05-01 | 分页途中Main generation变更返回409，CRM舍弃旧baseline并重启，不能置Ready | NOT_RUN | [规范 L414](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:414>) |
| AC-ID05-02 | snapshot/hash/version不符时关闭相关身份；前置seam冲突恢复只沿可信Main完整baseline并追加source selection，缺内容门不先推进授权 | NOT_RUN | [规范 L415](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:415>) |
| AC-ID05-03 | final目录变化/未解冲突不记Ready；Complete与新冲突在共同锁/CAS下重判，旧baseline不清新DRIFT，较新健康投影不回退 | NOT_RUN | [规范 L416](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:416>) |
| AC-ID05-04 | 失去lease的worker无权写Ready；重复同baseline不生成新身份事实 | NOT_RUN | [规范 L417](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:417>) |
| AC-ID05-05 | snapshot-only撤权保持SOURCE_SNAPSHOT/LEGACY_UNKNOWN，不制造messageId或revokedAt | NOT_RUN | [规范 L418](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:418>) |
| AC-ID05-06 | 上游auth拒绝失效当前cached service token；技术503保有效token缓存并按有界退避，均不扩大授权 | NOT_RUN | [规范 L419](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:419>) |
| AC-ID05-07 | 新Main generation接口与现CRM opaque string消费在真实环境的兼容证据Required/UNPROVEN；静态对应不作运行PASS | NOT_RUN | [规范 L420](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:420>) |

原测试名称及断言映射只证明固定源中存在相应测试文本；不证明本轮测试成功、字段全链覆盖或真实撤权时限。部署、SQL generation 迁移和所有 writer 参与尚需真实采用证据。 [规范 L713](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:713>)、[规范 L717](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:717>)、[规范 L739](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:739>)、[规范 L742](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:742>)

## 11. 具体依赖、待确认项与退出条件

| Required | 责任 Owner | 须取得的准确能力／证据 | 缺失边界 | 出处 |
| --- | --- | --- | --- | --- |
| Required-NATIVE-01 | 主协调/原册管理 | MSBBPA010母版及本组两册native身份/格式/原内容，或明确允许本稿交付格式的记录 | 不伪造xlsx；只提交Markdown候选 | [规范 L619](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:619>) |
| Required-IAM-01 | Main IAM/S5 | SYS-IAM/TENANT正式政策、所有身份writer清单、角色/用户/部门/菜单/权限及批量路径、原权限码 | 未核writer不能宣称全变更传播 | [规范 L620](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:620>) |
| Required-IAM-02 | Main IAM/S5 | 字段默认/合并政策、Main字段→CRM字段映射、row/field/action冲突处理及cache失效证据 | 逐字段资源接入RequiredFenced | [规范 L621](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:621>) |
| Required-ID-01 | Main身份Producer | 本tenant OIDC组织/服务client/identity配置和版本、token记录/优先outbox，实际Host注册/启用证据 | 源码存在但运行UNPROVEN | [规范 L622](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:622>) |
| Required-ID-02 | CRM投影/会话Owner | 本tenant/region注册、session索引、consumer唯一性、published及online交集运行证据 | 不宣称可离线授权或全链接入 | [规范 L623](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:623>) |
| Required-ID-03 | Main持久化+CRM Owner | Main持久generation在目标provider真实迁移/触发器覆盖；CRMopaque boundary兼容及首尾目录运行证据 | 不能称新版Main与旧CRM运行兼容 | [规范 L624](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:624>) |
| Required-ID-04 | Main/CRM安全Owner | OwnerFinalAuthorizationGateV1 adapter版本、ScopeAuthorizationFence/等价门完整共同写者与锁序、撤权竞争/缺门关闭、时钟/token/保留及SLO证据 | 按§3.6共同最终门关闭缺能力的相关新业务执行；历史read/可信事实接收/结果delivery分别用其门，仍不承诺未证SLO | [规范 L625](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:625>) |

| Required | 责任 Owner | 须取得的准确能力／证据 | 缺失边界 | 出处 |
| --- | --- | --- | --- | --- |
| Required-PLT-01 | Platform/S7 | ICp6InboxPreCheckpointV1/ProcessWithPreCheckpointV1准确版本、应用持事务及§3.5共同锁序/封闭decision/Dapr映射/全早退覆盖；另核RequeueDeadLetteredAsync应用权限与原子审计 | 缺前置内容门关闭相关新授权/依赖新写，可可信PendingInspection；不直暴露Requeue原语 | [规范 L630](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:630>) |
| Required-PLT-02 | 应用部署Owner/S7 | producer/consumer/schema/topic/contract-bundle矩阵、单写route epoch、旧消费者关停/回退证明 | 不开双consumer、不能直接停旧消费 | [规范 L631](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:631>) |
| Required-AUD-01 | SYS-AUD/SYS-PRIV/S5及业务Owner | 原结果/撤权/重放/技术文档/PII保留冲突策略、访问/导出权限及legal hold依据 | 通用retention不自动启用 | [规范 L632](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:632>) |
| Required-API-01 | Main/CRM/API Owner | 3.2候选路由、DTO兼容、错误码、分页、权限码及客户端版本签认 | 新API仅设计；不冒已有 | [规范 L633](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:633>) |
| Required-UI-01 | Main/CRM FE Owner | 旧COMPENSATED文案/历史保留策略、身份失效缓存清理、UNKNOWN重试交互 | 不能声称页面已修改 | [规范 L634](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:634>) |
| Required-TEST-01 | 各Owner+独立评审 | 被授权真实测试环境、AC与原测试identity、原始run/日志、验收人结论 | 70项新AC全NOT_RUN | [规范 L635](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:635>) |
| Required-ACCEPT-01 | 主对话总负责人 | 作者STOPPED→独立review→修订→正式接受/Registry记录 | 作者不能自判PASS或更新分数 | [规范 L636](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:636>) |

Required-ACCEPT-01 已由当前 UA 的正式接受关闭；Required-NATIVE-01 为明确格式边界，不能继续误报为 Markdown 设计阻塞。其余涉及政策、全 writer、同事务门、provider、注册、保留和真实测试的门须准确采用。不能以身份 health=READY、静态源码存在或旧 PASS 替代这些退出证据。

## 12. 实际阅读覆盖与附件继续队列

共同正文 750 行及原定点复审 167 行全文语义阅读；两份 CURRENT（ID 225 行、OPS 221 行）全文；两份 UA（ID 286 行、OPS 282 行）全部对象结构阅读，OPS 同时逐行全文；共享 SOURCE-MANIFEST 全部顶层对象结构阅读。原 ZIP 的 SOURCE-READ-LIMITS 16 行、作者回应 49 行、INDEX 18 行和 ACCEPTED-DEPENDENCY-READS 43 行全文阅读。精确 SHA、模式和行段记录在[商业阅读台账](<D:/CP6/docs/CP6_开发设计文档_20261010/evidence/commercial-reading.json>)。

必要辅助已补核：18 Required、70 AC、29 Task每条ID均精确落在已全文读正文原行，0定位差异；10 SPEC与原Scope完整结构合读。v0.1首轮独审250行现已全文补读，四项B与三项N按v0.2后继关闭解释，原RETURN不改PASS。BASELINES／候选CURRENT／PRIOR／REVIEW-HISTORY／STOPPED保原历史状态，不覆盖根准确UA。INT-COM 31行、OA-APP 21行、WMS-MASTER 9行精确依赖摘录已全文合读。

99份固定源码的完整bytes/SHA及124成员清单逐项只读核对0差异；Main55、CRM35、Platform9。作者记录38全文、26窗口、2注册窗口、33仅取得未审是旧作者覆盖，本轮没有冒全99源码新审计。大metadata目录／计划／Registry是历史定位资料，本Target完整范围由ORIGINAL-SCOPE保存；其他Target未因索引而宣称已全文读。原v0.1主文仅身份保全，不替代v0.2。

相关：[另一 S6 模块](<D:/CP6/docs/CP6_开发设计文档_20261010/modules/INT-OPS-01.md>)、[INT-COM](<D:/CP6/docs/CP6_开发设计文档_20261010/modules/INT-COM-01.md>)、[商业合同](<D:/CP6/docs/CP6_开发设计文档_20261010/contracts/commercial-contracts.json>)。本册状态为 `required_materials_consolidated`；当前必要规范和辅助已归并、历史载体已按实际角色分类，真实实现／业务测试／Owner采用仍未验证。
