# W269-01：FORM草稿Owner与当前授权/原命令

> W270验证口径更正：本报告当时执行根配置`vue-tsc --noEmit`退出0，但该配置`files=[]`只含项目引用，并未检查应用源码，不构成应用类型通过。本轮实际应用检查见[W270报告](../W270-01/DELIVERY.md)，历史日志原样保留，不将后来结果改称当时已执行。

W269：FORM独立草稿Owner/显式政策、当前会话/资源/字段权限、WFC原命令及CAS、旧身份互斥、完整表单设计器工作区已实现验证。24小时持续目标第二窗口，下一步W270。

[24小时连续计划](../../planning/20261102-codex-24h-oa.md)，最新确认基线`feed7f6a4374a50233c8377e72b3972ec66982d9`，COORD单人、DEV1 Main / DEV2 Handoff固定席位。

## 实际产品变化

FORM使用独立resource `workflow-form-definition`及显式`DIRECT_FORM_AUTHORING_V1`政策；既有FLOW政策不授予FORM能力。当前实际登录会话、oa-designer:form-save、本人/部门/下级/自定义/全部范围和Name/SchemaJson/Category/SubCategory字段权限在同一IAM根/Scope事务内检查，最终提交点核当前状态。沿用内部已保证的合同，不新造资格回执。生产政策、菜单、角色不自动预置。

新增cp6_form_authoring_v2政策/head/draft/commands/audit；保存头、完整schema/分类、原命令和强审计同事务。新草稿disabled且尚未发布，不写native可执行表；GET entry/list/draft/original不隐式创建草稿。expectedVersion必需，新建明确null；数据库每次更新生成opaque GUID token，调用者不能恢复旧token，不将GUID声称数值单调版本。WFC-CJ-1按租户/环境/实际actor/操作/key/commandId确定原件，保原请求和原schema词法，原件披露按当前字段/资源权限，写权撤销不抹去仍可读原事实。

新旧FormKey在共同事务根按原native bpchar及CI排序语义双向唯一；包含大小写/尾空格别名。缺少相关写门Fail-Fast。已有native定义和数据不改，不把FORM新头伪装成已供运行时采用；后续W270–276分别实现发布、启停、依赖及实际运行。新增迁移只在本轮隔离PG18执行，不迁移既有环境。

/api/oa/v2/definitions/FORM只读与保存入口、通用commands原件分派和/api/iam/form-authoring-policy已在实际Main注册，NoStore、当前Cookie会话、CSRF、严格信封。现有表单设计器可进入新工作区，复用完整可视化编辑器，原字段、布局、数字精度和分类null/空串/空格保持。受控编辑器不调用旧保存/发布；字段隐藏不构造空schema覆盖；并发冲突保留本地稿。未知保存先缓存精确命令，存储失败不发请求；NOT_OBSERVED、未知后的拒绝和查询失败都不冒充未保存；匹配原件后读取当前稿。身份epoch隔离旧响应和缓存，无关浏览器设置事件不丢稿。

## 实际验证与一次集中审查

本地16项PG18行为通过、0跳过（15项新增FORM，1项既有FLOW实际HTTP回归）；20项Vue组件行为通过（最后13项FORM复查，7项未变FLOW复用同任务成功结果）；最终vue-tsc退出0。实际Main登录会话/CSRF/HTTP和真实隔离PG；业务政策/IAM/数据为明确模拟。无Actions、工作流调整、既有环境迁移或生产部署。

证据D:/CP6-local-test/wave269/foundation/final-validation.json；review-20261102T140500Z覆盖真正双连接CAS锁竞争、政策撤销串行、同事务政策ABA、过期会话、跨租户/当前部门树、只读/隐藏字段、native别名、严格HTTP/CSRF、审计故障全回滚及commit丢ACK恢复。前端采用组件挂载和模拟API，不声称已运行浏览器到PG串联；该跨窗口验收在W278。此前成功W268完整schema边界直接采用。

作者合并SPEC/QUALITY集中检查完整分支和直接前置：边界/最终授权、命令身份、PG锁/原子性、不可变原件、当前披露、合法输入精度、UI恢复和375范围。审查收窄了PG约束异常分类，避免审计失败误报身份冲突；补充审计失败与两个真实连接争抢同一CAS的回归。修正页面存储失败提示、无关storage事件清稿；只复查影响部分。首轮测试辅助方法名错误导致编译失败，首轮Vue一个替身名称错误；保留失败日志，未当作成功，无未处理运行错误或未解决必需门禁。

## 剩余、时间与生命周期

WP12-01仍余FORM不可变发布/独立启停与固定sourceVersion克隆、完整原AC；WP12-02余FORM/子流程精确依赖、唯一绑定及实际运行/旧入口采用；表单个人草稿/提交字段规则授权、文件/选项Owner资格、跨窗口浏览器到PG全链按后续窗口执行。原59局部仍3闭环/56余项，不以本切片关闭整卡。

总目标持续IN_PROGRESS；375仍27DONE/2IN_PROGRESS/346PLANNED，125目标和247正式验收状态不变。W268最终核销1841秒；本轮累计时间及W269完整窗口工时由wave269-final-verification.json记录，每2小时按吞吐修正，不用本窗口冒充三天完成。

本提交为合并前快照：主分支采用、准确本地/远端任务分支删除、进程停止、官方finish和席位释放均以wave269-final-verification.json核销。固定目录保留复用，用户根目录未跟踪资料不动。
