# W172-01 — FLOW 草稿首个 Scope 消费 Owner

COORD 单人继续 CP6-7D-20261022。Main基线 `175aaf0852451e38c6392384515fb9660bc42dc8`，DEV-1 `codex/w172-01`；Handoff基线 `4c55189e83c7ac20b6f3c6d529f6dd7167ca2c00`，DEV-2 `codex/w172-handoff`。W171只归档一次，只关闭 WP02-10.wave171_scope_authorization。[准入计划](../../dispatch/20261027-wave172/README.md)。

## 实际交付与采用边界

首次把新 FLOW 草稿的新建、读取、保存与实际设计器接入 W171 的 WF_AUTH Scope 门。新增 PostgreSQL forward-only `20261028070000_WorkflowAuthoringV2`，authoring schema 内 head/draft、身份元数据、WFC命令原件/结果、授权审计同一实际事务。head 默认且强制 disabled，不生成任何 native 可执行投影。新草稿拥有稳定 head/draft UUID，元数据与完整 schema 共用精确 CAS token；完整原始 schema 字符串保存，未知扩展不会被窄 DTO 重写。未连线等未完成图返回诊断；非法形状、重复键、未知节点/服务执行种类、未登记服务引用、非法租约及子流程引用关闭准入。

这批新草稿没有发布/启用能力。完整 R1 的发布预演、准确 Published 来源复制/clone、冻结依赖、激活、旧定义迁移、FORM/TASK/OA-APP TxD仍待后续任务。现有 W170 native 下一草稿继续原链。当前不把独立 authoring UUID 冒充 native Published version，也不把新页面或本地夹具计为完整 WP12-01/真实企业验收完成。

## 权限与事务

可信 `ScopeAuthorization:EnvironmentId`、实际租户和当前原 access session/expiry/authentication receipt由宿主提供；请求不能指定 Actor、Tenant、Environment或权限域。拒绝 impersonation，不借用委派人的权限。固定锁序 native authentication root → 同一已登记 WF_AUTH fence → command/head/draft → receipt/audit；native root仅为外层根，不替代 Scope 门。新策略 writer 的 BEFORE STATEMENT 触发器先持同门再改变授权事实并推进 epoch。开始及提交前核对 native 和 authoring writer/保护覆盖，真实同事务复查当前 Actor、权限、版本及截止时间，任一失败全部回滚。

显式策略 `DIRECT_AUTHORING_V1` 由当前已登记 `pub-role-perm:edit` 管理，通过 `/api/iam/workflow-authoring-policy` 读取/保存，严格 operationId/expectedVersion/enabled/forbiddenRoles/reason；配置与原操作结果/审计同事务，原操作 GET `operations/{guid}`。禁止角色列表与当前有效角色求交，只针对直接设计期编写，不冒充发布四眼/SOD或审批委派的完整政策。无自动企业策略、环境登记或角色授予。

资源 `workflow-definition` 显式采用当前启用的 `oa-designer` 菜单访问作为 read 门；新增必须已登记 `oa-designer:add`，编辑必须 `oa-designer:edit`。资源范围保持原 stored MAX；SELF用服务器记录的创建人UUID，部门范围使用创建时真实当前部门及现在健康部门树，custom4沿原集合，ALL仍受租户和字段等条件约束。查询先资源过滤后分页，不泄露不可见对象计数。

Name/SchemaJson/FormKey/FunctionId/FlowCode完整注册字段域复用当前有效角色与 stored MIN 后 default1；未知/无效来源关闭。SchemaJson是一个整体受控字段，不宣称实现 schema内任意业务数据的细粒度隐私策略。只读字段同值回传明确允许，任何禁止字段变化整笔拒绝；hidden字段不输出，依赖SchemaJson的诊断也不输出。原结果不可变，返回时重新应用当前资源/字段权限；写权限撤销不自动遮蔽仍有权读取的原历史。审计保存真实 Actor/effective Actor、动作、当前角色/部门/范围、字段来源行及版本/有效矩阵、策略、Scope域/epoch、原请求与canonical digest、真实决策记录引用。

## 命令与入口

- `GET /api/oa/v2/definitions/FLOW/entry`、`GET /api/oa/v2/definitions/FLOW?page=1`、`GET .../FLOW/{key}/draft`：只读入口/分页/草稿，不创建版本。
- `PUT .../FLOW/{key}/draft`：封闭 WFC-1 `{commandId, expectedVersion, body, clientCorrelationId?}`；body `{name,schemaJson,formKey?,functionId?,flowCode?}`。commandId 为1..100字母数字及`-_.`，不是强制GUID。新建必须明确null；已有草稿必须原token。空白身份码归null，唯一约束同时覆盖旧身份语义。
- `GET /api/oa/v2/commands/{commandId}?operation=FLOW_SAVE_DRAFT&resource={原key}`：原槽严格含 tenant/environment/actualActor/operation/resource/commandId。精确资源引用使用返回的原key；其他Actor或租户不会借相同命令号得到结果。NOT_OBSERVED不证明没有提交；503/未知提交继续查原槽或原意图重试。

WFC-CJ-1复用严格JSON，任意层重复键和非法Unicode拒绝；键按UTF-16 Ordinal排序，数组保持顺序，字符串唯一JSON转义，合法JSON数字保原词法，不用浮点改写；版本前缀并入digest。普通请求/schema保1MiB、深度8、顶层500字段限制。raw请求/hash与canonical/hash分域保留；诊断correlation不决定幂等身份。全体成功事实不可变，未制定保留政策前无自动删除。

## 旧入口与页面

双向数据库身份检查与共同根锁覆盖 authoring/native 的 FlowKey、FunctionId、FlowCode。显式使用现有 PostgreSQL native bpchar/CI排序语义防大小写或尾空格身份别名绕过，同时原文本保持不变。旧 Designer save/clone、Definition save/publish、FlowAdmin启用、internal FlowDefService及所有当前 native head写者不能覆盖本批身份；EF触发冲突映射为503 AUTHORITY_FENCE_UNAVAILABLE。旧读取、引擎、Inbox、Forecast、触发和子流程来源仍只读native表，无法把新authoring草稿当Published执行。旧 FORM/TASK 仍未整体切换，不能据本轮宣称全Workflow旧入口已收敛。

OA设计器新增“新草稿工作区”，实际采用上述列表、新建、加载、字段投影、CAS保存与原命令恢复。复用原画布/节点/边面板，保存时保留未改动原JSON片段、未知扩展和原数字精度。待确认意图保存在当前身份的标签页缓存；网络/503不换命令号，NOT_OBSERVED不清意图；401/403、身份变化清页面私有数据与缓存，过期响应丢弃。隐藏结构不能变成空结构覆盖，冲突要求重新读取。只显示草稿已保存，不显示已发布/启用。

## 验证、失败与一次审查

42项后端结果（41 PG18/actualHTTP、1canonical unit）和14项Vue/身份恢复检查通过，0skip；前端类型检查及构建通过。最终15项新authoring检查，另对审查修正的未连接服务节点和授权证据分别定向复查；27项未受影响的旧Workflow/Scope结果保留原运行来源复用。实际HTTP包含原宿主登录/CSRF、策略配置及原操作、草稿保存、原槽查询、封闭body和旧入口拒绝。覆盖 CAS/未知ACK、跨Actor/租户、SELF/部门2/3/custom4/失效祖先、字段隐藏/只读、policy撤权竞争、同事务ABA/过期整笔回滚、writer缺失和原结果不可变。

早期失败原日志全部保留：目标框架无ToHexStringLower、固定测试菜单ID冲突、AsyncLocal夹具跨helper返回后丢失、native/text排序冲突，以及部门夹具在EF生成UUID前构建路径。错误的宽过滤曾命中无关Space artifact检查，诊断后改为实际方法过滤。集成42次运行的41pass/1失败不被改称全通过；部门修正后新专项15全通过。一次完整作者 SPEC/QUALITY，后续只复查上述实际修改：未连接服务图保诊断但拒绝非法执行配置；提交前再次检查覆盖及时间；审计补齐实际授权输入。无未解决必需门禁。

本地证据 `D:/CP6-local-test/wave172/foundation/final-validation.json` 逐项关联原日志、代码blob、UI/typecheck/build。真实企业247运行验收、浏览器到PG完整E2E、环境迁移/部署未执行。既有全局native根和全部Scope保守失效继续存在，不声称性能验收。

## 台账、耗时与生命周期

七天目标ACTIVE，D1/D4完成，D2/D3/D5/D6/D7原出口全部保留。375卡仍24DONE/2IN_PROGRESS/349PLANNED，125完整目标、247真实运行验收不变；只在WP12-01增加 wave172_flow_authoring 限定交付。没有SQL Server新增实现、Actions运行、保护变更或既有环境迁移/部署。

预计实际150–240分钟；当前官方Main START至本报告 64.09 分钟，完整实测以Main finish为准，Handoff重叠不重复计时。Handoff [PR](https://github.com/GTX537/CP6.AI-Handoff/pull/148) 已合并，源 `02975678a92f0314d94c2d7cf799f5cdd8207f63`、目标 `64702a8887a81e1272bb210c61986cb472e3cef1`，准确任务分支删除并释放DEV-2；Main本提交仍为正常PR/finish前快照，不能提前写已合并/释放。Main最终采用、分支删除、无进程/干净固定工作区和席位释放由 `C:/Users/tt/CP6-planning-reports/20261017-foundation/wave172-final-verification.json` 核对。

固定目录和缓存保留，根用户文件保留。下一准入只归档W172一次并关闭 WP12-01.wave172_flow_authoring，继续准确发布/启用与原七天剩余出口。
