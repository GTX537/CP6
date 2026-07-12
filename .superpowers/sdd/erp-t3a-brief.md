# M-ERP T3a 任务简报：15 控制器贴 [RequirePermission]

## 背景与位置
M-ERP 横切接线波第三任务(branch=feat/m-erp-crosscutting)。T1 已产出权限键真相源,T2 已完成菜单 MenuKey 锚定。本任务只做控制器贴点;权限种子由 T3b 单独任务完成。

## 必读(按顺序)
1. `docs/00-横切接线规范.md`(P1 横切规范,本任务是其机械执行)
2. `docs/seeds/erp-permission-keys.md`(T1 真相源——键值以此为准逐字使用,46 行:35 真写端点×键 + 11 只读 POST 豁免)
3. 样板: M-WMS T3a 先例 commit 8aecc71(git show 8aecc71 看典型贴法)

## 需求
1. 对真相源列出的 **15 个 ERP 控制器、35 个真写端点**逐方法贴 `[RequirePermission("key:action")]`,键值与真相源**逐字一致**(连字符 erp-*,禁下划线;高危键如 order:cancel、sheet-unit-price:correct 不得降级为 edit)。
2. **11 个只读 POST 豁免不贴**(真相源已逐条豁免,勿自行加贴)。
3. **裁决点(主控已裁)**: `EstimateCalcController.Calculate` 挂 `[AllowAnonymous]`(EstimateCalcController.cs:136 附近)——**保留不动**,本任务不贴不删;T4 反射测试将显式豁免;已记终审票待用户裁处。在报告中确认其现状未被触碰。
4. 纯注解叠加: **零方法体改动**,类级 `[Authorize]` 全部保留。
5. 交付自查表: 逐控制器「端点→键」1:1 对账(35=35),写入报告。

## 验证与提交
- `dotnet build` 0 Error;全量测试(基线 1689 绿,T2 后)不许跌。
- 单 commit,message 说明贴点计数;commit 后立即 push(用户硬性纪律)。

## 报告契约
详细报告写入 `C:\CP6\.superpowers\sdd\erp-t3a-report.md`(逐控制器对账表+自查+测试输出摘要)。回复只返回: 状态(DONE/DONE_WITH_CONCERNS/NEEDS_CONTEXT/BLOCKED)、commit sha、一行测试结论、concerns(若有)。
