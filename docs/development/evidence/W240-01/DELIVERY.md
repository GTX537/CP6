# W240-01 完整 Root 操作改期交付

W240完整Root TIME_ONLY改期：准确Snapshot/Occurrence与五域、保留扩展根包络、WO/SCHED共同事务、单写范围、不可变计划及原结果恢复、独立权限与页面。

用户明确允许生产验收使用模拟数据，不等待企业真实资料。本轮实际环境是隔离本地 PostgreSQL 18、Main Owner/API 与 Vue 组件：工程 Snapshot/Occurrence/Assignment、资源 Claim/窗口、来源用途、执行状态及日历输入明确模拟；Root/操作计划、版本效果、共同事务、原命令和审计实际执行。业务验收结果为该 TIME_ONLY 范围通过；部署状态为未部署，不能据此声称生产环境已运行。

## 交付行为

- 显式模拟期初准确映射尚未执行的原生工单与所有操作；采用范围由数据库触发器关闭旧工单/操作写入，未采用旧计划仍参考只读，不自动迁移在制数据。
- 完整 Root 捕获、不可变计划和整组前后差异；保留或外扩根包络，null根只在全成员时间已知时初始化。精确微秒/offset、数量单位依据和未知null保持。
- 最终五域、操作集合、队列前驱和当前Claim/来源/执行/技术前驱检查。Root加一次版本，改变的操作加一次排程版本，未改操作和其余WO域不变。
- WO/SCHED/application/audit/command实际共同事务；中途失败全部回滚。相同命令、同计划另一命令均采用原application；当前写权限撤回后按原键及当前读权限恢复。
- 独立权限点对已有和新租户登记，不自动授予角色。跨租户与无WO写权限不产生效果。
- 页面完成读取、准备、全Root确认、提交和原结果恢复；未知不当作无效果，原命令不因随后重试403而丢失，身份变化忽略旧响应。

合同及端点见[完整Root改期v1](../../scheduling-time-v1.md)。新采用根没有队列成员，数组明确为空；本轮仅比较冻结的队列前驱，不冒充已实现队列管理。

## 实际验证与复用

19 distinct local backend scenarios PASS:11 scheduling PostgreSQL18/actual HTTP,2 existing planning PG/HTTP,6 existing permission-seed behaviors;15 Vue scenarios PASS (7 scheduling,8 existing planning); app vue-tsc PASS. Explicit simulated upstream inputs, actual WO/SCHED PostgreSQL transaction and HTTP. Unchanged successful results reused, failures retained.

11项排程场景覆盖：根缓冲保留/完整未改清单/版本/原结果，外扩与来源/技术前驱拒绝，准备后开始或Claim撤回，全部五域及未改成员/队列前驱，中途失败回滚，并发计划一个胜出/旧raw写拒绝，失去响应后撤权恢复，独立WO权限及异租户，未排根与DST微秒，以及实际HTTP上下文/准备/提交/原结果/额外字段和无offset拒绝。普通文本看似时间但内容不同仍拒绝同命令冲突。

实际日志/TRX在 `D:/CP6-local-test/wave240/foundation/`，每个场景取最后实际结果，汇总 `C:/Users/tt/CP6-planning-reports/20261017-foundation/wave240-validation.json`。PG修复轮11项与前轮未受影响的2项旧计划、6项种子行为合并为19个不同场景，不累计重复运行。页面7项排程加8项旧计划，终态 `D:/CP6-local-test/wave240-web-review.log`；类型检查 `wave240-web-review-types.json` exit0。组件API响应模拟；后端HTTP使用实际宿主/登录/PostgreSQL。

失败保留：首轮测试引用Json命名碰撞，修复后执行；事务故障测试初用仅Reader钩子未拦到实际写入，改既有写入钩子后原步骤回滚通过；新增测试三处括号语法修复后通过。集中审查发现理由文本被误作时间规范化、未知重试拒绝时清除原命令，两项已修复并行为回归。没有吞错、伪造通过或减少权限边界。

未运行全仓回归、浏览器布局、正式发布构建、R2制品/真实环境推广或生产部署；相同输入成功证据按来源复用，提交SHA变化不重跑。

## 集中审查和剩余范围

One author task SPEC/QUALITY review: exact Root/Snapshot/Occurrence and five-domain identity, full unchanged manifest and root envelope, current permission/tenant/Claim/EXEC checks, shared WO/SCHED transaction rollback and original replay, raw legacy writer fence, precision/offset time, immutable audit/results, independent role catalog, UI unknown recovery/current identity. Corrected ordinary text timestamp aliasing and preservation of original commands after denied uncertain retry; no extra proof wrappers or repeated internal serialization validation.

当前只交付TIME_ONLY子集。完整自动排列/整Root批次、资源变更六阶段、资源队列、BoardCapture筛选KPI分页、真实工程资源执行宿主衔接及其他原D3范围仍未完成，不关闭完整WP08-10/WP08-11或D3。模拟上游并不冒充这些产品功能已实现。未部署生产、未迁移既有数据库。

W202工程基线及W203资源目录既有交付继续存在，本轮不重复建设、不把未实现业务改称数据缺失。原七天目标ACTIVE，D1/D4闭环，D2/D3/D5/D6/D7部分完成；2/7是阶段数而非工程量百分比。375正式任务、125目标、247验收不因本子集自动关闭。下一步：继续原D3自动排列的完整Root批次、资源变更与队列及实际看板接线；D2通用Transfer/RSV/ActualFact和退货商业财务、D5/D6/D7原剩余范围保留。缺失输入直接模拟，不等待企业数据或新增资格证明链。

## 生命周期快照

COORD本聊天单人，Main基线 `976fa6db7eadbf81e6f94f6d084471c1dfe77964`，复用DEV-1/DEV-2。原估实际90–180分钟；11:15登记后用户暂停，15:21:52Z恢复。暂停不计工作耗时，最终记录分别列注册经过和恢复后经过时间。此报告为合并前快照；正常合并、准确分支删除、进程停止和官方finish由同轮最终核对证明。固定目录/缓存保留，无Actions触发/取消、工作流或保护变更。

Handoff [PR](https://github.com/GTX537/CP6.AI-Handoff/pull/216)已完整采用并finish；Main待最终核对。本快照距恢复27.58分钟，不含此前用户暂停。
