# W58 / 七天一期 D5：当前身份核查与原字段来源消费

COORD本聊天单人，2026-10-23T07:18:55.213312+00:00。Main `2070c0b44fef912256fc2141d87763fc544aa6a2`，CRM `6ff205740ac5290154588acd7840ad9f70cb7c96`，Handoff `caccd556bcc3f7346990156f803355476f3319af`。W57 CRM102/Handoff33/Main426全部合并采用，77项相关本地验证及API/Worker编译通过，登记实际78.17min；准确分支删除、固定树干净detached、全部席位finish，三仓任务Actions均0。七天目标ACTIVE，W37–W57不重复。

## 前置与本窗边界

最新AGENTS/POLICY哈希未变，已核实际工作树/来源/容量及W57最终证明，W58三仓无重复PR。WP02-07正式Inbox与S01-08合同采用DONE；WP02-05/06/09/29及WP02-16仍有未验收范围，不能把已交付局部当整卡完成。已读S6 §3.6和字段消费边界、现有Main/CRM在线路径与字段来源实现。

明确现状：Main原`/connect/crm-context`经真实签名/token/family及在线主体校验后返回动作、范围、目录，但未传输完整字段policy/registry/mapping。Main已有内部当前字段来源读取和MIN算法，尚无CRM字段注册/映射与跨仓发布合同。CRM历史业务工厂仍走SQL Server。Main在线GET与CRM事务分库，不能充当共同最终门；W57派生发布保持PendingInspection/CanAuthorize=false。

本窗完成CRM端每次真实具体OIDC客户端Reconcile（原签名/issuer/audience/token/在线主体）与PG当前发布事实的同次核查服务。拒绝外部自报verified/actor作为顶层入口；核session、tenant/subject/issuer/region及token元数据，再读实际PG注册/发布/源、用户认证epoch、禁用/过期/密码/锁定、原不可变token撤销。PG读使用新的Serializable事务与既有readiness；不创建SQL Server投影假行、假rowversion或另一身份权威，不跨请求缓存。

动作/部门/主管/成员与原完整grant交集语义一致；原字段事实保留真实permission来源、角色/版本、resource/action/scope/DeptIds及显式Fields，不把不同grant的动作/范围/字段拼成权限，不把缺项物化为跨仓默认。未知字段注册/Owner映射/在线字段策略仍明确不可用。结果为不可外部构造的本次核查快照，CanAuthorize=false；不实现IIdentityAuthorizationReader、不注入旧业务授权链、不返回业务ALLOWED_HELD。真实业务读/PII字段展示仍需对应Owner读权与映射，新写需S6 §3.6共同门。

本窗不改八份既有身份SQL、不新增迁移，复用原PG发布/会话/接收基础和原scope算法；实际边界问题按受影响代码定向修复。Program/既有consumer/旧数据库工厂不切换，不改变公共C02合同合法输入。HTTP测试明确本地可信来源夹具，不冒充真实IdP或完整宿主验收。后继须继续补Main字段来源合同/CRM映射、真实Owner共同门与宿主采用，不以本窗核查代替这些任务。

## 时间、验证和收尾

预计实际100–160min：40–65min具体OIDC客户端与PG当前事实核查；30–50min动作/范围及原字段来源交叉核查；30–45min相关验证、一次作者SPEC/QUALITY和三仓收尾。沿用七天目标，不按日历等待；登记Main start到最终finish计实际，不重复累计子任务。

定向覆盖真实JWT校验/在线拒绝与503/取消、tenant/subject/source/region不匹配、撤销与epoch变更、失效/过期发布、禁用/缺依赖、scope收窄及原字段分支保真、无权限扩大和旧路径兼容。仅相关本地用例及API/Worker受影响编译；W57已通过且输入不变的90秒超时等证据引用复用，不重跑。无Actions、部署、既有DB迁移或新工作树/代理。

DEV-1 `codex/w58-coord`维护总台账/四状态/计划；DEV-2 `codex/w58-handoff`可靠保存START并finish deferred后bind CRM `codex/w58-01`。CRM正常PR、远端采用、准确分支清理/finish后复用同席位完成Handoff，最后Main PR/finish。owned PG55437停止留证。24/375整卡、125目标0完整交付、247 NOT_RUN不提升。W51准确data删除曾被自动审批拒绝，理由仅“blocked by policy”；停止保留、不重试或绕过。

## W58实际交付

CRM [PR103](https://github.com/GTX537/CP6.CRM/pull/103)已采用，source `80af97ec22060c1dd32586db4ebb045e71762611`，远端main `aaed1098b35cf74fd286099f2bc31b1b9b071f80`，13任务路径exact blob核对通过。[单份产品报告](https://github.com/GTX537/CP6.CRM/blob/aaed1098b35cf74fd286099f2bc31b1b9b071f80/docs/development/evidence/W58-01/DELIVERY.md)。130项相关本地用例通过（17项本次PG/HTTPS、1项PG认证、112项旧身份/OIDC回归）；API/Worker相关构建；真实Owner/宿主验收NOT_RUN，无Actions。

具体OIDC客户端每次执行签名/token/在线context校验，新分支要求原URI、JSON与no-store；随后创建PG Serializable READ ONLY事务，核当前注册/完整发布、原来源、用户限制、token撤销与原epoch。原身份/范围计算通过内部事实接口共用，旧路径验证未放宽，不制造SQL Server投影行或rowversion。

原字段逐grant保留role/source/version、action/scope/DeptIds及显式Fields；不同grant不合成新许可、缺字段不推默认。结果为本次核查快照，CanAuthorize=false，字段政策和业务门明确不可用；不实现旧授权Reader、不注册新业务路由、不新建/续期session。真实HTTPS/签名JWT串联与只读角色拒绝写入已实际验证，夹具不冒充真实Main或IdP。

八份原SQL不变、无迁移，专用读取角色验证不改变原writer能力。一次作者完整SPEC/QUALITY通过；当前130项结果和失败记录留证，未改W57发布/90秒采集及W54/55正式包证据仅复用。owned PG停止保留，CRM准确本地/远端分支已删除、固定树干净detached且finish释放。

后继继续Main在线字段政策/CRM字段映射、真实业务Owner共同最终门与宿主采用；旧业务工厂仍为历史SQL Server，Program/consumer未切换。无Actions、既有库迁移或部署；24/375整卡、125目标0完整交付、247 NOT_RUN不提升，七天目标ACTIVE。W51准确data删除曾被自动审批拒绝，原因仅“blocked by policy”；停止保留，无重试或绕过。

Handoff [PR34](https://github.com/GTX537/CP6.AI-Handoff/pull/34)已采用 `3aa2fac8dd49aca5bb0e2a08cd0b7034a864fcea`，准确本地/远端分支删除、工作区干净detached、finish释放。Main仅剩当前协调分支正常PR及finish，不提前声明释放。登记开工至此约59.82min，原预算100–160min，实际最终时长以finish为准，不重复累计子任务；此前只读核对约3min。
