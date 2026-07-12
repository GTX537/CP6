# M-MES T6 任务简报：测试补网(报工状态机 / PlanningBoard 排产改期)

## 背景与位置
M-MES 横切接线波末任务(计划原文: 「T5 测试补网(审计 T5/#7 零→有): 报工状态机(开始/中断/完工/全工序完工触发入库)、PlanningBoard 排产/改期核心用例。(反冲的测试在 F1 包。)」)。先例=M-WMS T7(653a7fc)/M-ERP T6(a5551de): 真值锚定,期望值先手算再验,服务算错会红,零生产改动。

## 需求
1. 先盘点既有覆盖: grep CP6.Tests 中 ProductionResult/WorkOrder/PlanningBoard/反冲(F1 波 C.1/C.2 已有 justCompleted/反冲/CostCollect 链测试)相关既有用例,新用例不得重复——**反冲/成本归集链明确排除**(F1 已覆盖),本任务聚焦状态机与排产本身。
2. **报工状态机**: ProductionResultService 的 开始(Start)/中断(Suspend)/再開(Resume)/完工(Complete) 状态流转核心用例——非法流转被拒(如未开始直接完工/重复开始)、合法流转落库状态与时间戳、**全工序完工触发入库**(Complete 最后工序→InboundService 生产入库路径,断言到入库单/库存侧真值,若该链在 F1 测试已锚定则只补状态机侧缺口并在报告说明)。
3. **PlanningBoard 排产/改期**: Reschedule/AutoArrange 核心用例——改期落库(计划日期/机台变更真值)、边界(改到过去日期/冲突机台的行为,以服务实际语义为准,手算期望)。
4. 断言=手算期望值,禁套套逻辑;发现真实缺陷不修,记 concerns。
5. 零生产改动;用例量级照 M-WMS T7(5 用例级,以覆盖质量为准)。

## 验证与提交
- 焦点 filter 全绿;全量(基线 1749)不许跌。
- 单 commit 即 push。

## 报告契约
详细报告写入 `C:\CP6\.superpowers\sdd\mes-t6-report.md`(既有覆盖盘点+逐用例输入→手算→期望表)。回复只返回: 状态、commit sha、一行测试结论、concerns、报告路径(15 行内)。