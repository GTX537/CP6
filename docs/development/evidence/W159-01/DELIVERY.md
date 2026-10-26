# W159-01 工具技术规格草稿独立 Owner 与原控制版本

COORD单人；Main基线465c0e70b1e4357fa5d797bb0ac3f41977575427；复用固定DEV-1/DEV-2。预计90–150分钟，Main官方START至本登记18.73分钟；实际结束以finish/proof计时，Handoff不叠加。[原计划](../../dispatch/20261026-wave159/README.md)。原七天目标ACTIVE。

## 实际交付

依据SUP-ACT-006-01与ENG-TOOL-COMPATIBILITY §3–8，增加真实ToolSpecificationOwnerV1，实际HTTP `/api/tool-specifications/owner-v1` 和独立页面 `/plm/tool-specifications`。plm-tool-specification定义query/add/edit，不授予真实角色；旧erp-plate-mold编辑权不能写专业草稿，也不等于专业批准。

以准确原生版模UUID/原业务修订为草稿身份，不猜测或压缩原40位编号/20位BaseCd。专业资源与原版模查询使用各自当前授权，可信部门映射复用实际plate-mold UUID；工程产品必须其真实Owner当前引用和查询范围。写入还要求专业query，恢复只需原actor及当前专业/版模/产品query，无需保留写权限。所有对象/字段注册、当前租户、作用域和来源权限在同Main事务最终复查。

创建/修订必带原操作UUID、理由、准确当前native令牌、原观察Ref、当前技术控制Ref、准确工程产品Ref及明确产品适用、工艺要求、配置适用、技术规格和后续DOC要求。缺失/陈旧/越权均拒绝，不替调用者读取latest填预期。工程产品为真实当前启用原件；工艺/配置/DOC要求目前是工程师声明的草稿要求文本，未伪装正式受控来源或资格。当前技术草稿无生产执行资格。

新PostgreSQL迁移建立cp6_tool_specification_v1 heads/versions，tenant_id参与主外键；不可变UTF8原件及SHA-256包含准确来源引用、完整草稿、原actor、理由和记录时间。触发器约束原身份、原版本、源/产品原件摘要、单步头推进及原件禁止改删。无旧历史回填、无真实角色授予。迁移仅在隔离PG18本地库应用，未触及既有环境。

草稿CREATE/REVISE、头推进、Main原操作结果/审计共同提交或回滚。相同原键/原输入回放，异体冲突；两个实际DbContext竞争同一前驱只允许一个成功；丢失ACK查COMMITTED，UNKNOWN可查询或结束，CLOSED拒绝晚写。阶段状态明确DRAFT、Approval/Freeze NOT_REQUESTED、PhysicalQualification UNKNOWN。业务Rev、native令牌、原观察版本与独立专业草稿工作版本保持区分。

PlateMoldWriteOwnerV1实际编辑读取/旧create-revise-update-delete采用真实草稿控制Ref：没有专业草稿时仍是TECHNICAL_CONTROL_UNPROVEN原观察；存在后是Main.Plm.ToolSpecification.v1/TECHNICAL_SPECIFICATION_DRAFT及独立版本/摘要。专业草稿变化但native不变，原页面旧控制前置也会412。旧原生内容修改不改草稿原件；技术页显示准确来源已变化，编制下一草稿必须明确载入并提交新前置。商业专有变化只改变native令牌，不假装改变技术草稿；原订单请求处理与PE行为未扩展。旧成功原结果仍按原事实恢复，不被当前控制状态改写。

独立页面从当前服务端授权列表选取工具，明确选择当前工程产品，显示各版本、草稿与批准/实物边界；准确读取历史不替换输入。保存冻结输入，冲突保留原输入与预期版本，不自动刷新或重发；成功后需明确重新载入。仅在租户/actor命名空间存原operationId/objectId，Web Locks排他、当前scope复查、UNKNOWN查询/close和结果对象/类型核对；身份变化或401/403清除私有资料，晚到响应丢弃。旧编辑页显示专业草稿控制，恢复校验接受准确专业草稿Ref而不误判为原观察。

## 验证与集中审查

12项新PG18/实际HTTP加13项旧写者回归，共25项通过：专业权限隔离、实际原件/精度/未批准状态、相同key回放与异体、数据库不可变/前驱约束、跨租户、独立技术控制使旧写者冲突、旧修改不覆盖草稿历史、原native/观察/产品各自冲突、不支持APPROVE、当前产品停用、丢ACK/撤写后恢复/关闭晚写、最终授权失败全回滚、部门2/3/4与产品原Owner映射/撤销、创建者范围、缺原版模query、错DbContext、两个真实DbContext并发创建/修订、实际HTTP命令/历史/恢复/CSRF/no-store/撤权。完整旧写者13项回归保持通过。

47项UI/router/auth通过，包含9项新规格页、13项旧编辑页（新增专业控制恢复1项）、25项路由/认证（新增独立路由授权2项）。vue-tsc与Vite通过，无跳过。初始未实现路由404保留；首次后端22过/3部门夹具缺产品映射失败，源Owner正确拒绝。修正夹具：先断言产品未绑定时拒绝，再通过实际IAM绑定工程产品；仅重跑这3项并通过，产品代码未改，未变22项复用。UI新加一项旧控制恢复后仅重跑该文件13项，其他34项复用。构建后的变动仅测试/文档，不重复产品构建。证据D:/CP6-local-test/wave159/foundation/final-validation.json，失败记录全部保留。

一次执行者SPEC/QUALITY集中审查整个任务diff，核对实际Owner、当前权限与同事务、锁顺序、原件/控制版本、精度保留、恢复和页面身份边界、原规格阶段要求及未完成范围；无待修必需项。没有运行GitHub Actions，未修改保护或部署，未把本地验证称为真实企业验收或浏览器E2E。

## 剩余范围与收尾

实际专业草稿已交付，技术审查/批准/冻结工作流未实现；不能用新草稿权限直接批准，不能将DRAFT称为已受控发布。正式工艺/配置适用Owner原件、受控DOC双Role、物理实例身份/资格和HoldExact仍待各原Owner。完整WP05-04/06、D3尚未完成；原订单CHANGE/CANCEL请求应用、正式Sales稳定来源也仍未完成。Continue actual professional tool control/approval prerequisites and WP05-06 physical instance dual-Role provider WP10-48, DOC and exact source qualification; WP05-05 original Order request processing and WP04-32 formal Sales remain. Preserve all original ROU/BOM/WO/SCHED and D2/D5/D6/D7 exits.

Handoff正常PR https://github.com/GTX537/CP6.AI-Handoff/pull/135，source 499fee491a7426935b7e02a8ebb2cac6a7fa1955，目标443ff1616eceeefc6de899b52dbaa863f3881083，准确任务分支删除、DEV-2官方finish释放。Main本记录是正常PR/远端采用/分支删除/finish之前的真实快照；最终proof记录完整生命周期。固定目录及Main依赖连接/CRM缓存保留。

W158只归档一次并关闭WP05-04/05.wave158_legacy_writers；W159仅WP05-04/06新增wave159_technical_drafts限定记录。375整卡24DONE/2IN_PROGRESS/349PLANNED、125完整目标0、247运行NOT_RUN、41历史静态不变。只有D1/D4整阶段完成，2/7=28.6%是阶段比例，不是工作量完成百分比。
