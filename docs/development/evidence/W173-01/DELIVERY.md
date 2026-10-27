# W173-01 — FLOW 准确发布与不可变版本

COORD单人继续CP6-7D-20261022。Main基线 `137e8e7c711627603d728e26ef1a74c2cfedba77`，DEV1 `codex/w173-01`；Handoff基线 `64702a8887a81e1272bb210c61986cb472e3cef1`，DEV2 `codex/w173-handoff`。[准入计划](../../dispatch/20261027-wave173/README.md)。W172归档一次，仅关闭WP12-01.wave172_flow_authoring；原完整任务与七天出口保留。

## 实际实现

W172新FLOW草稿现在可以按准确draftId/token生成10分钟发布预览，并经当前发布授权确认成为不可变版本。预览固定完整schema原文摘要、身份元数据、最后实际编辑人、版本号、当前发布政策版本和依赖来源诊断。确认时重新计算等价、检查原Actor/对象/草稿/摘要和有效期，提交前再次检查截止时间；预览不是发布，不启动实例或调用连接器。

新增forward-only PostgreSQL迁移 `20261028080000_WorkflowPublicationV2`，版本、预览、发布政策和草稿状态的真实存储及数据库保护。确认发布在同一事务写入原版本快照、Draft→Published、head token、WFC原命令和当前授权审计。发布versionId保留原draft UUID，版本号服务端分配；name/schema/身份/发布人/时间/摘要及依赖清单冻结，UPDATE/DELETE/TRUNCATE被数据库拒绝。不存在native可执行投影，head继续强制disabled；发布成功不启用，不改变旧实例。

旧W172草稿最后编辑人只从匹配tenant/environment/draftId/精确row token的真实WFC回执提取；没有回执不从Owner、姓名或当前用户补造。之后保存和创建下一草稿直接记录实际Actor。schema完整原文和数字词法保持原样。

## 当前授权与命令恢复

发布动作明确采用 `oa.definition.publish → oa-designer:publish`，同时要求原edit、当前资源/字段read与作者政策。发布政策 `DIRECT_PUBLICATION_V1` 需显式配置enabled、distinctAuthor和forbiddenRoles；distinctAuthor比较当前草稿最后实际编辑人，不擅加所有企业必须四眼。`/api/iam/workflow-publication-policy` GET/POST和operations/{GUID}由当前pub-role-perm:edit管理，UUID CAS，政策/原操作/审计同事务。配置时仅登记发布能力，不自动授予任何角色；配置动作本身不是企业业务政策批准证明。

复用同一实际WF_AUTH Scope映射和当前IAM原会话。锁序native认证根→Scope→原命令→当前head/draft→回执/审计；旧已完成命令仅要求当前read。新发布政策writer在修改前取得同门并推进epoch。publication_ready核对实际trigger函数/事件/启用/条件/参数及复制模式；必要保护缺失关闭新写。最后复查Actor、当前动作/资源/字段/政策、head内容、Scope epoch和会话/预览时间；撤权、ABA、过期或版本变化整笔回滚。

新增WFC槽操作FLOW_PUBLISH和FLOW_CREATE_DRAFT，完整封闭body、expectedVersion和准确来源参与WFC-CJ-1 digest。历史成功同body返回原结果，不新分配版本；异body409。提交未知503保留原command，原槽查询NOT_OBSERVED不证明未写。当前字段遮罩重新投影原回执和历史版本，隐藏SchemaJson也隐藏摘要/依赖；撤销写权限或关闭Scope不剥夺仍获当前read的原历史。audit保留实际Actor/角色/资源部门/字段源版本、发布与作者政策、真实Scope证据、原请求/digest和发布/来源事实。

## 版本和设计器

新增GET head、分页versions和准确versionId，GET draft在发布后返回无草稿且不创建。版本分页在外层明确按唯一版本号降序。POST drafts只复制指定的本定义Published来源，使用当前head CAS分配下一版本和新draft UUID，保source_version_id；可选旧版本作为来源，不能隐式latest，也不把历史版本改回Draft。已有草稿冲突且不覆盖；当前资源可见性先于任何生命周期状态披露，来源字段受当前权限约束。

现有新草稿工作区接入发布预览/确认、发布说明、历史只读原文、指定版本创建下一草稿。未保存修改阻止预览发布；失效预览重新读取，页面不会把发布显示成启用。未知结果保留原意图和原操作类型，跨刷新恢复，NOT_OBSERVED不换号；仅匹配原command/resource/version的结果清待确认状态。身份切换/登出/403清私有缓存并丢弃迟到响应。发布期间与草稿编辑互斥，已发布内容只读。

## 依赖与未完成范围

这是一条新FLOW批次的真实发布链，尚不是完整Workflow运行链。当前FORM原件/权限来源及子流程准确激活来源未由本批正式Owner采用；带这些引用的预览明确UNPROVEN并拒绝发布，保留草稿。不会以旧native head、latest、EF层不可变意图或重建版本冒充原发布/授权证据。服务引用记录当前实际宿主登记的类型/模块身份，重算时需一致；它不证明外部调用成功，也不等于完整配置/部署资格或未来运行消费者已按pin执行。后续需交付FORM来源、子流程精确依赖/激活及真实runtime消费者，再开放对应引用与启用。

本轮没有完整FORM/APP/TASK/TxD、旧实例迁移、clone/比较/业务预演全套采用、真实企业政策批准或保留清理。原D2 Transfer/REP/RMA、D3工程批准/RootWO/Snapshot/Occurrence/ROU/BOM/SCHED、D5企业宿主/broker、D6政策/日历/登记/提交及D7集成出口全部保留。不能以本轮关闭完整WP12-01、125目标或247真实运行验收。

## 验证与集中审查

26项后端结果（25隔离PostgreSQL18/actualHTTP、1canonical unit）与20项Vue/恢复检查通过，0skip；前端类型检查和构建通过。后端覆盖准确发布一次、原命令冲突/丢ACK恢复、不可变历史、指定旧版本复制、政策不授角色、distinct author、当前字段遮罩、实际撤权等待、最后权限失效/预览过期整笔回滚、保护缺失、未证依赖及真实宿主登录/CSRF/HTTP。页面覆盖未保存阻止、失效预览、原意图恢复/匹配、旧来源选择和身份清理。

初始23与最终25后端检查均通过。一次作者SPEC/QUALITY发现并修正：提交时预览截止检查、版本外层排序、误扩大的草稿DTO服务端字段、可见性先于生命周期状态和Scope先于新命令head读取。修正后定向13项（11发布、2当前Owner/实际HTTP）通过；覆盖的旧结果被替换，其余13项未受影响作者行为保留final25原来源复用。没有把计数相加或旧结果写成新执行。证据 `D:/CP6-local-test/wave173/foundation/final-validation.json` 关联真实日志与源码blob；所有进程结束后才修改相关代码。

没有浏览器到PostgreSQL完整E2E、真实247业务验收、既有环境迁移或部署，没有Actions或保护变更。迁移只在隔离数据库运行；合并不等于生产启用。

## 台账与生命周期

七天目标ACTIVE，D1/D4完成，其余原出口开放。375卡仍24DONE/2IN_PROGRESS/349PLANNED，125完整目标0交付、247真实运行NOT_RUN；只增加WP12-01.wave173_flow_publication限定交付。

预计90–150分钟；本报告时官方Main START经过25.1分钟，最终实测以Main finish为准。Handoff [PR](https://github.com/GTX537/CP6.AI-Handoff/pull/149)已合并，源`aeeb27b04c0e0b03831f6698a322d7a121930d8d`、目标`11ea3bb7234a0f69bab2bb03652a58f9d494aa23`；准确任务分支删除，DEV2释放。Main本提交是正常PR/finish前快照，未提前写已合并/释放；最终采用、分支清理、工作区及席位由 `C:/Users/tt/CP6-planning-reports/20261017-foundation/wave173-final-verification.json` 核对。固定目录、缓存和用户原始资料保留。

下次准入只归档W173一次，关闭WP12-01.wave173_flow_publication限定窗口，再推进真正依赖与原七天未完成出口。
