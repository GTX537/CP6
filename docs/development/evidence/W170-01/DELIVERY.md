# W170-01 — 流程读取无副作用、显式版本草稿

COORD 单人，修复 [Issue #591](https://github.com/GTX537/CP6/issues/591)。这是 WP12-01 的限定实际前置，完整卡仍开放。W169 已归档一次，仅关闭 WP10-48.wave169_disposition_intent 生命周期。

## 实际行为

FlowDefService.GetDraftAsync 使用不跟踪查询，保留旧参数的源码兼容但任何读取都不创建版本。没有草稿的设计器加载返回发布原版；已有head保存不得隐式生成下一版，需显式创建，已有草稿编辑和发布必须携带实际row token。拒绝前先做检查，不把失败编辑残留为待写head变化。首次新定义仍沿现有创建链，未冒称其已采用完整OA-DEF。

Main.Workflow.Draft.v1 显式动作携带 operationId、准确 Published SourceVersionId 和完整head/version摘要。PostgreSQL内先取得既有原生身份权限门，再锁定义及版本表，分配下一序号；同定义只有一个草稿。可选择较早发布版本，新版本号递增，头不回退；原 schema 字符串含未知布局属性、Unicode和数值词法原样复制，来源ID/摘要保存在原操作及审计。创建不发布、不启停、不启动审批或改写已pin实例。

实际Wf_FlowDefVersion、Main原操作结果和审计在同一事务。末尾再次核对原头/版本集、实际新草稿、当前原操作者和edit权限；同事务来源变动或撤权整体回滚。另一连接并发创建只有一方成功；原始输入不同不能复用同操作键。提交回执丢失可查或重放原操作；已失edit但仍具现有租户读取资格的原操作者可以恢复。NOT_OBSERVED不被解释为确定未写。原结果保持创建当时事实，即使草稿后来发布也不改写原回执。

采用当前既有租户内读取规则和oa-designer:edit，检查当前用户/租户启用、锁定/过期与已登记动作；没有新增角色授权。**这不是WFC-1或accepted OA-APP共用WF_AUTH映射的采用**，不把原生认证门重新命名为该协议，也未宣称本轮完成正式资源/字段细分权限。

## 接口、页面与兼容清单

- GET `/api/oa/flow-defs/{key}/draft` 与 `/api/oa/designer/load/{key}`：只读，未有草稿分别404和原发布视图。
- 新增同前缀 `draft-context`、POST `drafts`、GET `draft-operations/{operation}`；封闭请求体、当前权限、CSRF、原结果身份及no-store由实际HTTP验证。
- 已有Designer.save、Definition.SaveFlowDraft、旧`/api/wf/flow/def`统一经过严格SaveDraftAsync；旧WF路由增加可选rowVersion并返回实际token，缺失或错误不能覆盖已有草稿。
- `/oa/designer`和`/wf/flow-designer`均显示发布原件，显式选择来源后创建。未确认不显示成功；原意图重试与结果查询、已有草稿冲突、身份切换迟到响应均有覆盖。OA使用草稿自己的名称；SVG加载、历史快照和保存保留顶层扩展schema字段。未运行浏览器E2E。
- 内部SaveDefAsync兼容helper保留历史语义，不作为本轮公开创建/发布Owner；源码检索未见Core/WebApi生产调用者。FormService和表单读取链本轮未切换。正式publication preview/dependency pins/activation及旧helper收敛仍属WP12-02后续范围。

## 实际验证与审查

27项相关后端检查：12项最终源码上的隔离PostgreSQL18／真实HTTP；15项定义、pinning、不可变及设计器既有单元结果按未变代码复用。12项Vue组件/实际页面接入检查、最终typecheck/Vite通过，无跳过。

修复前PG回归确实失败：GET写入第2版。首次测试编译因await lambda缺async失败；保留日志。随后22/23通过，失败来自race注入器只监听Reader却挂在NonQuery日志写入，改为实际草稿存在后的读取注入。之后24/25通过，失败是跨租户测试未恢复AsyncLocal请求上下文；已纠正并检查实际权限错误映射，4项针对性检查通过。最终12项PG全部通过，包括准确历史版复制、原回执重放、旧token拒绝、真实并发、最终来源变动/撤权回滚、跨租户、锁定、HTTP和发布后读取无副作用。失败运行不整体改称PASS，只复用其中明确独立成功且代码未变的15项。

一次COORD完整SPEC/QUALITY核对上述行为、范围与完整diff。审查修正历史草稿名称读取及SVG顶层schema属性保留，3项页面定向复核和最终type/Vite通过。证据 `D:/CP6-local-test/wave170/foundation/final-validation.json` 逐项绑定运行及最终源码；本地证据不计入247真实业务验收。

## 生命周期与时间

预计45–75分钟，官方Main START至报告 30.33 分钟；最终实测记录在外部wave170-final-verification.json，Handoff重叠不累计。Handoff https://github.com/GTX537/CP6.AI-Handoff/pull/146，源 884212f47800672ca3b225c327c64100a1d774cd，目标 4842d97717584cf52eeb0bedfcd48f8acc2da515，已完成准确任务分支清理与DEV-2释放。Main在本提交时仍待正常PR合并、远端采用核对、分支清理与官方finish；此处是提交前真实快照。

复用固定目录，未创建工作树；根用户未跟踪文件和依赖缓存保留。工作流/依赖沿用已核对手动触发配置，无任务Actions、既有环境迁移或生产部署。Issue只在远端main核对后关闭。

## 保持开放

WF_AUTH精确映射和同门writer、WFC-1、正式发布/启停、表单、OA-APP绑定/真实票决/Owner应用仍未完成。工具正常使用ENG/DOC/VER/REL资格、跨Owner义务闭合、调拨正式技术适用性/货权/可信上下文，以及原D2/D3/D5/D6/D7全部出口保留。375卡24DONE/2IN_PROGRESS/349PLANNED，125完整目标、247真实验收不因本轮改变；2/7仅是完成阶段数，不能当作实际工作量百分比。
