# W276-01 当前授权任务信箱和真实在途影响

W276：原生APP/普通FORM本人当前授权信箱、准确根子pin、独立已读原事实及实际定义在途影响已实现。

[24小时持续计划](../../planning/20261102-codex-24h-oa.md)。基线`ee236f554ebde05203bed63b1baaebab41834c35`，COORD单人复用DEV1/DEV2，无新增工作树。

## 实际交付

新信箱查询APP和普通FORM的真实运行归属，当前本人参与关系、oa-inbox能力及各业务Owner/FORM当前字段权限共同决定可读范围。过滤发生在搜索、计数和分页之前；合并/展开保留准确TaskId/CCId集合。在途含当前有权查看的参与实例，已办保留真实办理事实。根/子图使用真实FLOW/FORM pin，普通表单内容明确来自根提交，APP使用冻结绑定的业务详情路由；未取得业务Owner应用回执时显示UNPROVEN，不从流程终态推断业务已应用。只读模型不能替代办理最终授权。

显式mark-read建立独立不可变首次阅读事实，记录actual/principal/UTC/原命令，不改审批任务RV、不投票也不完成任务。GET没有隐式写入；同意图/不同意图重复不改首次时间，跨连接竞争只有一个首次事实。原结果查询仍核当前读取权限，NOT_OBSERVED不能解释为无效果，显式关闭同锁下形成永久无效果槽位。当前写能力和原结果读取分开；数据库guard、真实目标归属、同事务writer及延迟完整性保护阻止绕过。

历史信箱/待办中心/旧详情不再以历史参与关系披露新原生记录；旧mark-read和批量转交明确拒绝原生目标。旧InboxView整体保留为LegacyInboxView，新默认页面采用当前读面，历史和当前草稿可显式进入。身份切换、选择切换迟到回复及403清除旧内容；本人普通任务复用W275办理组件，APP进入实际业务页面。未知已读命令只持久保存安全标识，存储失败不发送新命令。

FLOW/FORM启停和FORM发布预览读取实际运行/挂起根子实例、Active草稿、个人入口绑定、当前或计划APP绑定、递归发布父版本。只返回当前定义授权范围的聚合计数，不披露其他业务身份和内容。后续停新或新版发布不修改已接受运行pin；缺少读数不伪装为零。

## 验证与集中审查

本地31项唯一后端行为通过（11项隔离PostgreSQL18、20项既有读面/内核），0跳过；25项Vue组件行为通过，应用vue-tsc通过。实际Main登录/CSRF/HTTP、两连接已读竞争、当前权限和租户隔离均有定向证据。模拟业务数据，内部Owner/PG事务/原生内核真实执行。

逐行为最后成功来源在`D:/CP6-local-test/wave276/foundation/final-validation.json`。原有分页行为测试从不具备实际collation的SQLite fixture改为隔离PostgreSQL，仍验证服务端LIMIT/COUNT和同一分页行为，未新增SQL Server实现。保留最初迁移CASE语法、关闭命令nullable映射、旧fixture及组件mock失败日志；修复后只做相应回归，不把失败记录算作成功。所有隔离PG/API已停止，没有浏览器到真实PG全链结果。

作者一次任务级SPEC/QUALITY集中审查覆盖全部产品diff、新文件、原TASK/DEF范围、当前读权限与字段投影、旧入口防绕过、实际pin/历史、独立已读原子性/原命令、UI身份与迟到回复。审查修正了在途参与集合、APP应用状态推断、关闭槽位null映射和父页失权清理；相关定向验证通过。没有Actions运行/取消、工作流/依赖变更、既有环境迁移或生产部署。

## 真实剩余与生命周期

本窗口本人读面已完成；完整TASK-R2封闭代理授权尚未采用。显式代理读取当前返回DELEGATED_READ_OWNER_NOT_ADOPTED，不能退回旧参与式接口或把旧空scope授权称为新合同。此直接依赖并入W277：TASK_READ前置、actionKinds与flowKeys/bizTypes封闭scope、维度AND、UTC半开区间、稳定grant身份/修订撤销及当前实际/有效双身份，然后接代理撤回/BLOCKED新授权。既有APP/普通办理协议保留真实历史范围。全量高负载读性能、完整SFS可编辑patch/三版本和全通道通知不在本窗冒称完成。

原375仍27DONE/2IN_PROGRESS/346PLANNED，59局部仍3闭环/56余项，125/247正式状态保持。W277接续封闭代理授权/当前代理读面、代理撤回及BLOCKED新授权；完整SFS patch/三版本、历史下载/目录、浏览器到真实PG、退回等操作和定时Worker仍在后续窗口。

24h总目标持续IN_PROGRESS，本窗不是三天完成。最终远端采用、准确本地/远端任务分支删除、工作区干净和官方finish见`C:/Users/tt/CP6-planning-reports/20261017-foundation/wave276-final-verification.json`。固定席位/复用缓存保留，主目录用户资料不动。
