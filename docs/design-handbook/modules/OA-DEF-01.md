# OA-DEF-01 — 流程与表单定义

整理状态：`core_semantics_consolidated`；当前正文与共同合同已全文补读，强制附件及跨域采用仍有待核项，不能据本文声明实现完成。本文将已接受静态规范改写为开发说明，原文与 SHA 清单见本组合同索引 `frozen_inputs[OA-DEF-01]`。

## 1. 目的、操作者与职责

流程作者设计图和表单、保存草稿；发布者核验完整依赖并发布不可变版本；流程管理员独立启停新申请。覆盖原五项 SPEC：流程设计、表单定义、草稿发布、版本、启停。定义 Owner 是 Main Workflow。订单、采购、财务、库存和 PLM 业务对象的状态及内容由各业务 Owner 维护；定义发布不会批准或应用这些对象。[D L7](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:7) [D L21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:21) [C L29](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/85/85ba15dfd2b01875__S3-COMMON-CONTRACT-R1.md:29)

入口为 `/oa/designer`、`/oa/flow-admin` 以及原流程/表单设计器。设计图预演只解释路径，不能创建实例、任务、通知或调用连接器。[D L21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:21) [D L63](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:63)

## 2. 当前组合与接受边界

当前正文是 `OA-DEF-01-DEVELOPER-SPEC-R1.md`，SHA `00435e88e979f79e18d012e6d9958a81cdb185ec766b01a2ede83bafc7f12c9c`；强制合读 S3 Common R1、FORM R1、TASK R2、OA-APP 唯一 accepted v1.1.2 R2、复用索引、矩阵、AC trace 与最终累计复核。旧索引的 TASK R1 按 S3 CURRENT R2 覆盖解析，不能任取最高文件名。[AD L157](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ec/ecf17865cff57970__UA-20261008-S3-OA-DEF-01-R1-MD02.json:157) [R L91](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f4/f47806d8b42afa57__S3-FINAL-CUMULATIVE-REVIEW-R2-20261007.md:91)

`UA-20261008-S3-OA-DEF-01-R1-MD02` 接受的是五项 Markdown 静态详设；正文历史候选标签、索引冻结时的等待接受标签不改变后续接受记录。27 项 AC 仍 NOT_RUN，IAM/Owner 实际采用 UNPROVEN；不声称用户亲签、实现、上线或原 xlsx 母版已完成。[AD L3](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ec/ecf17865cff57970__UA-20261008-S3-OA-DEF-01-R1-MD02.json:3) [AD L295](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ec/ecf17865cff57970__UA-20261008-S3-OA-DEF-01-R1-MD02.json:295) [AD L362](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ec/ecf17865cff57970__UA-20261008-S3-OA-DEF-01-R1-MD02.json:362)

## 3. 数据、身份与版本轴

|对象/字段|开发含义与约束|依据|
|---|---|---|
|`definitionId` / `versionId`|服务端 GUID；`kind=FLOW\|FORM` 不可变；head 身份与具体版本身份分开|[D L27](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:27)|
|`flowKey` / `formKey`|租户范围稳定唯一；发布后不改 key，改名身份用新定义及明确映射|[D L29](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:29)|
|名称、分类、身份码|name 1–200 随版本冻结；分类不是授权边界；FunctionId/FlowCode 空白归 null，唯一，克隆清空|[D L31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:31)|
|定义版本与 CAS|version 整数；Draft=0、Published=1；rowVersion opaque，不从数字版本推造并发令牌|[D L35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:35)|
|激活|enabled 与 activationRevision 独立于 schema 版本；发布成功不代表 enabled|[D L37](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:37) [D L92](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:92)|
|依赖|发布时精确记录表单、子流程版本及 schemaDigest；运行实例持有 pin，不能读取最新 head 替代|[D L47](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:47) [D L111](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:111)|

表单类型支持 input/textarea/number/select/radio/checkbox/date/datetime/user/dept/upload/table。子表列仅允许 input/textarea/number/select/date/datetime，不允许再嵌复杂子表；行数 min=0、默认 max=100、硬上限 200。必须完整往返 schema 的布局/UI 属性，不能保存时丢弃自己不显示但规范允许的字段。[D L51](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:51) [D L76](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:76)

流程节点保存审批人策略、会签 all/any/veto、串签档位、字段 mask、CC、超时及升级配置；StageIndex/StageRound 是运行事实，不拿设计档位冒充已发生轮次。边有稳定 ID、引用及顺序；无 priority 时保声明顺序。校验分支、合流、环及引用，审批人无可用结果转 Suspended。子流程绑定准确版本，默认深度 8、多实例 100 为 Required 参数口径。[D L43](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:43) [D L45](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:45) [D L47](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:47)

表单规则为单次顺序解释 compute/require/optional/show/hide；前端 disable/enable/setOptions 不授写权。服务端复算，不能执行任意脚本；金额精度和量纲由业务 Owner 定义，本模块不发明金融舍入规则。[D L53](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:53) [D L78](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:78)

## 4. 流程、入口条件与本地写集

|动作|前置与状态迁移|同一事务写集/结果|
|---|---|---|
|新建/克隆|当前动作权和 WFC 门；新 key 不存在；克隆显式 sourceVersionId|head + 首个 Draft + 身份元数据 + command receipt；默认 disabled；只复制定义，清身份码，不复制任务/历史，保存 clonedFromRef。[D L59](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:59)|
|读取/保存草稿|GET 无写；无草稿 404；显式 create-draft 或 save-CAS|允许未完成图，但致命结构、越权或未知可执行类型拒绝；写草稿版本与 receipt，不能静默覆盖他人的 CAS。[D L61](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:61)|
|发布预览|精确 draft token、schemaDigest、依赖和兼容诊断|只返回 previewId/digest/token、影响摘要，默认有效 10 分钟；不激活。[D L90](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:90)|
|发布|门→command→head/draft；重新核预览、依赖、图、schema、lease、子流程和表单|Draft→Published 一次；schema、依赖 pin、head 投影、publication receipt、audit、命令结果原子提交。过时预览 409 PREVIEW_STALE，不自动发布新草稿。[D L92](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:92)|
|从已发布版建新草稿|显式已发布 sourceVersionId、当前 head token；最多一个 Draft|已有 Draft 返回 DRAFT_EXISTS；回退内容也走新草稿→发布→激活，不把旧 Published 改 Draft。[D L109](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:109)|
|启停|当前 activation/binding、准确 Published 与可用依赖、CAS|同一 form 仅一条活动 flow；冲突 E-WF-008，不能暗中停另一条。启停写独立 revision 与 audit/receipt。[D L121](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:121) [D L123](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:123)|

停用仅停止新根申请；已有草稿仍可读/保存，在途实例和已准入子流程继续按原 pin。撤回在途实例另由 TASK/APP 合同处理；不能把关闭开关当取消。[D L125](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:125)

## 5. 接口与跨模块合同

所有下列 v2 路由属于待实现合同，不表示现已提供。前缀 `/api/oa/v2`；旧路由/DTO 保留并登记适配。[D L137](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:137) [D L142](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:142)

|方法与路径|请求/响应关键点|
|---|---|
|GET `/definitions/{kind}/{key}`、`.../draft`|HeadView 含 id/kind/key/headVersion/enabled/latestPublishedVersionId?/draftVersionId?；draft GET 只读。[D L142](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:142)|
|PUT `.../draft`|WFC + `{name,formKey?,functionId?,flowCode?,schemaJson}`；新 key 可 expectedVersion=null，已存在 head 不能借 null 覆盖，返回 DEFINITION_EXISTS。[D L143](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:143)|
|POST `.../drafts`|显式 sourceVersionId，准确已发布源和 head CAS；未知 key 404，无隐式 latest。[D L144](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:144)|
|POST `.../publish-preview` / `.../publish`|发布输入 `{draftVersionId,previewId,previewDigest,expectedVersion,comment}`；结果 Publication 包含精确 versionId、schemaDigest、publishedAt、publisherRef、dependencyRefs，`activationChanged:false`。[D L92](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:92) [D L145](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:145)|
|GET versions/read/compare；POST clone|diff 按稳定 field/node/edge ID；clone `{sourceVersionId,newKey,newName}`。[D L107](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:107) [D L147](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:147)|
|激活动作|`{enabled,expectedActivationRevision,publishedVersionId,reason}`；返回 activation revision 与 `inflightUnchanged:true`。[D L123](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:123) [D L148](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:148)|

APP 的 BindingRevision、PolicyRef 和不可变 BindingTimeline 不由 DEF 的 ActivationRevision 替代。FORM 消费精确 schema/activation/binding；TASK 消费实例 pin；SYS-IAM/FILE/PRIV 提供当前授权、附件和保留政策。[X L38](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bd3b9c6f0d6fee8b__OA-APP-REUSE-INDEX-R1.md:38) [D L111](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:111) [C L100](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/85/85ba15dfd2b01875__S3-COMMON-CONTRACT-R1.md:100)

## 6. 事务、幂等、并发和恢复

使用本组公共合同 `WFC-1` / `WF-AUTH-1`：命令槽是可信 tenant/environment/actual/operation/resourceIdentity + commandId，先当前 read 查原结果，新写与撤权者共享同一 durable WF_AUTH 门并按固定锁序取锁。仅 head/draft rowVersion 不能解决撤权竞态；内存锁也不能代替数据库缺失键保护。[C L43](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/85/85ba15dfd2b01875__S3-COMMON-CONTRACT-R1.md:43) [C L68](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/85/85ba15dfd2b01875__S3-COMMON-CONTRACT-R1.md:68) [C L70](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/85/85ba15dfd2b01875__S3-COMMON-CONTRACT-R1.md:70)

发布/启停失败整笔回滚。提交结果未知时沿原 command 查询，不能新生成 key 再发布并多增一个版本；同键异 body 冲突。同键已完成且仍有当前读取权时返回原结果，不把新写权限要求套到合法原结果读取。409 时保存本地编辑并展示差异，不能自动替用户接受新版本。[D L65](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:65) [D L96](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:96) [D L127](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:127) [C L52](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/85/85ba15dfd2b01875__S3-COMMON-CONTRACT-R1.md:52)

## 7. 权限、租户与敏感信息

作者使用已有 designer add/edit/form-save；管理员 enable 不自然取得发布权。新 publish/read 等 Required 动作键须 IAM 明确采用，不能把 UI 隐藏当后端授权；是否四眼发布按政策确定，不默认强制所有流程四眼。所有版本、diff、影响数、附件和选项都受当前资源/字段权限投影。[D L21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:21) [D L94](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:94) [D L107](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:107) [C L35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/85/85ba15dfd2b01875__S3-COMMON-CONTRACT-R1.md:35)

Scope 来自可信入口。请求不接收 tenant/actor 决定授权；任何缓存必须区分 Scope、actual、effective、资源和版本；撤权清显示缓存，跨租户迟到响应取消。[C L31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/85/85ba15dfd2b01875__S3-COMMON-CONTRACT-R1.md:31) [C L43](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/85/85ba15dfd2b01875__S3-COMMON-CONTRACT-R1.md:43) [C L86](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/85/85ba15dfd2b01875__S3-COMMON-CONTRACT-R1.md:86)

## 8. 页面与服务端校验

设计器保存完整图与 schema；未完成图允许草稿，但发布前必须展示精确依赖和阻断原因。预演显著显示 EXPLANATION_ONLY，不能使用户以为已经发起流程。发布按钮和启用开关分开；冲突只对当前可见定义展示信息，不用不可见对象名泄漏冲突。[D L61](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:61) [D L63](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:63) [D L90](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:90) [D L127](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:127)

表单设计、布局、条件可见性与服务端字段资格分别处理；只读和隐藏字段不能通过手写请求更新。字段删除在新草稿上进行，已发布版保持不变；影响清单只输出授权摘要。[D L53](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:53) [D L76](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:76)

## 9. 实施顺序与源码差异

建议按原 D-WP 顺序先收敛 GET/保存/发布 API 及 WF_AUTH 门，再落版本/激活模型与旧内部入口适配，最后页面与 AC。原规范对 `CP6@90c871fe571fd6b390f53e8678376d7ce60bcb60` 的静态对照指出 GET draft 可创建、内部 SaveDefAsync 可绕过发布验证、Designer 分步更新身份码非原子；这些是固定 SHA 的历史源码事实，本文未对现工作树作新代码审计。[D L9](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:9) [D L17](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:17) [D L154](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:154)

发布、保存、克隆、旧客户端和内部调用均要收敛到统一服务，不能只补一个 v2 Controller 就宣布并发闭环。旧定义迁移只能以准确证据重建并标 LEGACY_RECONSTRUCTED / UNVERIFIED_LEGACY；不为缺失旧历史补造发布人或 pin。[D L150](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:150) [C L92](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/85/85ba15dfd2b01875__S3-COMMON-CONTRACT-R1.md:92) [C L96](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/85/85ba15dfd2b01875__S3-COMMON-CONTRACT-R1.md:96)

## 10. 验收设计与未执行范围

27 项 AC 全部 NOT_RUN。重点分组：D01 创建/图/克隆/预演六项；D02 schema/规则/服务端校验五项；D03 发布 CAS、过时预览、原子性及撤权六项；D04 版本 pin/历史五项；D05 唯一活动、停新不伤在途、启停 CAS 五项。完整断言在原正文，不以本手册的归纳替代它们。[D L67](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:67) [D L82](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:82) [D L98](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:98) [D L113](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:113) [D L129](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:129)

本次只整理文本、读取身份和检查文档，不运行代码、业务测试、数据库、构建、Actions 或迁移。实际同门 writer、Recorder、Owner 适配与运行配置仍 UNPROVEN。[R L95](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f4/f47806d8b42afa57__S3-FINAL-CUMULATIVE-REVIEW-R2-20261007.md:95) [R L97](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f4/f47806d8b42afa57__S3-FINAL-CUMULATIVE-REVIEW-R2-20261007.md:97)

## 11. 缺口与待确定项

- 新动作键、权限域到 WF_AUTH 的唯一映射、所有旧入口 cutover 清单需要实际实现与 IAM 采用证据。[C L68](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/85/85ba15dfd2b01875__S3-COMMON-CONTRACT-R1.md:68) [C L96](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/85/85ba15dfd2b01875__S3-COMMON-CONTRACT-R1.md:96)
- 默认 preview TTL、深度/多实例、表单大小等是规范中的 Required 参数，部署取值和变更治理还需配置证据。[D L47](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:47) [D L90](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md:90) [C L58](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/85/85ba15dfd2b01875__S3-COMMON-CONTRACT-R1.md:58)
- OA-APP 当前正文2499行及S3来源资格的完整S3对象已补读；本组当前必要L1/L2证据已核对并按准确组合归并；跨域实际Owner采用仍需逐合同证明。

## 12. 原文与阅读覆盖

本轮全文：DEF R1 L1–160、Common R1 L1–108、FORM R1 L1–189、TASK R2 L1–294、最终累计复核 L1–126、APP 复用索引 L1–56、S3 CURRENT R2 L1–216。接受记录为结构化读取接受决策、原 SPEC 身份、AcceptedBody、ReviewQualifications、OwnerAndExecutionBoundary；不是声称每个历史字段均已语义阅读。矩阵和 AC trace 为结构化核对，旧 TASK 指针按 R2 覆盖。逐文件 SHA、区间、继承阅读和待补队列见 `evidence/platform-engineering-reading.json`。

**当前必要材料归并状态。** 本册为 `required_materials_consolidated`：选定正文、规范附件及准确接受/复核范围已按直接实读、结构化语义或精确复用归并。身份清单核对不等底层源码全文阅读；历史草稿、候选实现和Office/HTML视觉未核范围仍单列。可复查[逐件阅读与处置](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)及[本组闭包台账](D:/CP6/docs/CP6_开发设计文档_20261010/worklogs/platform-engineering-progress.json)。当前必需规范未读清单为空；具体Owner采用、参数/物理接口、真实运行和本节已有来源缺口不因整理闭包而改变。
