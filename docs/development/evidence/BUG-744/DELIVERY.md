# BUG #744 — 需求集审批详情直达恢复

[Issue #744](https://github.com/GTX537/CP6/issues/744)，基线`3a80fceb31dc2864dd47a825891dcf9f2a367fa2`，固定DEV1、`codex/bug-744-requirement-set-detail`，COORD单人。

需求集审批详情URL已注册并独立加载准确对象，submission传给既有评审面板；页面直接刷新保持轮次，切换对象丢弃旧响应，当前权限拒绝清理私有内容。UUID大小写保持同一身份。

根因：既有审批Binding配置`/plm/requirement-set/{bizId}`，路由仅注册目录，页面也未接收对象/送审props；直接打开出现空白。W298真实Chrome首次失败记录保留在`D:/CP6-local-test/wave298/permission-consumption-set-withdraw-behaviors-20261104T013231Z/requirement-set-withdrawal-browser/`。新增回归修复前27通过/5失败，其中冷启动首项触及测试默认5秒上限并伴随清理错误，另有实际路由权限与3项对象读取行为失败；冷启动沿既有15秒测试上限。首次修复32通过，集中审查发现UUID大小写遗漏，单项先失败，再修正后33通过。

33项相关Vue组件及实际应用路由启动行为通过，vue-tsc通过；真实Chrome直接打开需求集/准确送审URL，创建本轮授权、实际提交后丢响应、直接刷新URL并恢复原结果，实际Main API/PostgreSQL18核对原事实未改写。

实际浏览器结果/截图：`D:/CP6-local-test/bug744/permission-consumption-deep-link-browser-20261104T015115Z/requirement-set-withdrawal-browser/`，已查看恢复截图。复测使用真实前端路由/登录/API/PG，仅模拟业务输入并故意丢弃已提交响应，未模拟内部Owner。业务仍为两条相连事实，旧BLOCKED原件不变，一次新提交后用原键恢复。最后UUID大小写归一由新增定向回归和最终类型检查覆盖，已成功的小写URL场景行为不变，复用其真实浏览器结果。

一次任务级完整diff审查：静态详情子路由沿既有query权限守卫；详情不依赖目录当前分页；原submission只传给相同对象，UUID大小写等价；路由换对象使旧generation失效，拒绝清理私有状态。复用既有评审/Owner/命令恢复，不改变数据库或业务权限。

唯一来源索引`D:/CP6-local-test/bug744/foundation/final-validation.json`。本窗未改服务端/数据库，未重复W298所有Owner测试；未执行生产部署、既有环境数据库迁移或Actions。正常push/PR前核对工作流和依赖内容不变，复用已审查事件结论。

原375保持30DONE/2IN_PROGRESS/343PLANNED，原59余53，125/247不变。Issue待远端main准确采用后关闭；本窗分支处置、工作区干净、官方finish释放按`C:/Users/tt/CP6-planning-reports/20261017-foundation/bug744-final-verification.json`核销。Handoff无需新占席位，W298两仓已有交付；下一W299总控窗口同步该实际结果及W298生命周期。24h有效实际执行目标继续。
