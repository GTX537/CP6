# BUG #710 评审成功后的正常刷新

[Issue #710](https://github.com/GTX537/CP6/issues/710)，P2。基线`24e458914964ef0543ec890da8670857f15f279e`；COORD单人，固定DEV2，分支`codex/bug-710-review-refresh`。W278继续占用DEV1，Handoff START保存远端后按官方deferred释放，再bind复用DEV2，无额外工作树。

## 根因与修复

实际Vue/Chrome→Main Program→PostgreSQL中，应用重新授权已返回APPLIED版本2。子面板write/load完成后emit changed；父页refreshReviewIteration开始读取新版本，busy使子面板disabled=true。子面板run最后一次check把该UI状态当成身份失效，错误清空刚回读的结果并短暂显示“当前身份或评审轮次已变化”。父页随后按既有版本刷新关闭旧冻结包。单纯增加等待不能解决这个真实错误。

修复迭代评审成功后父页面刷新被误判为身份变化：临时UI禁用只阻止新动作，不清除已确认结果；真实认证、权限、租户及轮次检查保留。

只移除执行中check的props.disabled条件；run入口和按钮继续检查disabled。identityStamp、认证可读、当前query权限、scope、iterationId、submissionId、组件存活及generation均保持。没有修改后端授权、事务、原回执或父页版本刷新策略。

## 实际验证与审查

- TDD红测`C:\Users\tt\CP6-planning-reports\20261017-foundation\bug710-red.log`：真实Vue父子组件的changed事件把父级busy置true；成功结果被清空并显示alert，1失败/10通过，按预期复现。
- 相关面板及原命令恢复27项通过：`C:\Users\tt\CP6-planning-reports\20261017-foundation\bug710-green.log`，包含成功刷新保留版本2、真实身份变化丢弃私有数据、未知结果恢复、当前权限及精确轮次。显式应用vue-tsc通过：`C:\Users\tt\CP6-planning-reports\20261017-foundation\bug710-typecheck.log`。
- 原实际业务复测`D:\CP6-local-test\wave278\permission-consumption-browser-application-refresh-fixed-20261102T234141Z\iteration-application-browser`通过；页面明确重新授权，等待父页实际GET刷新，再打开同一冻结评审包看到版本2和评审批准。DOM观察同时检查整个刷新期间没有虚假身份错误。PG核对新actor/grant、连续前驱、旧BLOCKED回执不改写。模拟业务输入，内部HTTP/Owner/PG真实执行；该场景属于W278，其余窗口范围未移入BUG分支。

一次作者集中SPEC/QUALITY检查完整两文件代码/test diff及本报告/四状态：区分UI锁和权威身份条件、成功事件与父组件异步刷新、未知命令恢复及真实撤权清理。没有新增内部证明层或重跑无关全仓。

## 交付边界

全部验证本地；无Actions触发或取消、工作流调整、生产部署和既有数据库迁移。正常合并并核实远端后关闭Issue。准确分支处置、工作区及席位释放以`C:\Users\tt\CP6-planning-reports\20261017-foundation\bug710-final-verification.json`核销，随后bind恢复并resume原W278-HANDOFF。原375/56局部余项与24h目标保持，W278继续完成本窗口。
