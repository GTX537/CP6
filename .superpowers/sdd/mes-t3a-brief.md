# M-MES T3a 任务简报：9 控制器贴 [RequirePermission](28 写端点)

## 背景与位置
M-MES 横切接线波第三任务。T1 真相源+T2 菜单锚定已过审。本任务只做控制器贴点;种子由 T3b 完成。

## 必读(按顺序)
1. `docs/00-横切接线规范.md`
2. `docs/seeds/mes-permission-keys.md`(T1 真相源——键值逐字使用: 28 真写端点×键 + 2 只读 POST 豁免)
3. 样板: M-ERP T3a 先例 commit bdbc532(git show 看贴法: attribute 位置/using/与既有 attribute 叠放)

## 需求
1. 真相源列出的 **9 个有真写端点的控制器、28 个写端点**逐方法贴 `[RequirePermission("key","action")]`,键值与真相源**逐字一致**(连字符 mes-*;高危 oee:recalculate、process-cost-rate 不得降级)。
2. **2 个只读 POST 豁免不贴**(PlanAchievement Summary/ExportCsv);MesDashboard 纯 GET 无贴点,PlanAchievement 全员豁免——两控制器合规缺席,报告里说明。
3. 纯注解叠加: 零方法体改动,类级 [Authorize] 全部保留。
4. 交付自查表: 逐控制器「端点→键」1:1 对账(28=28)写入报告。

## 验证与提交
- dotnet build 0 Error;全量测试(基线 1722 绿)不许跌。
- 单 commit 即 push。

## 报告契约
详细报告写入 `C:\CP6\.superpowers\sdd\mes-t3a-report.md`。回复只返回: 状态、commit sha、一行测试结论、concerns、报告路径(15 行内)。