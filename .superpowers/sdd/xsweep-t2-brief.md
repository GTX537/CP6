# X-SWEEP T2 任务简报：「贴点⊆种子」互锁测试扩六模块

## 背景与位置
跨波双票 sweep 第二票。现有各波反射测试锁「漏贴」（非 GET 无 RequirePermission 报红）与「种子偏离测试 oracle」，但存在盲区：**新端点贴了键、忘了往 *PermissionSeed 加行**——任何测试闸不红，只会在现网表现为 admin 403（种子无 RoleAction 行→admin 无键）。票源：M-ERP 完成记录票#3 起、逐波扩容至六模块（M-MES 票#3/M-OA/WF 票#3/M-PUR 票#3/M-PLAN/PUB 完成记录票#3，plan 文末各小节）。

## 需求
1. **新互锁测试**（建议单文件 `CP6.Tests/PermissionSeedInterlockTests.cs`，六模块各一 [Fact] 或 Theory）：对每个模块——
   - 反射读该模块命名空间控制器全部 `[RequirePermission("menu-key","action")]` 贴点元组；
   - 经模块的 menu-key→MenuId 锚定映射（**测试内字面量锚定表**，照各波真相源 docs/seeds/*-permission-keys.md 的锚定小节抄）；
   - 断言每个 (MenuId, action) 在该模块种子类的 Actions 清单中**存在**（⊆ 方向；种子多于贴点不红——view 类种子键属正常）。
2. **模块与种子对应面**（主控已盘点，须核实）：
   - WMS→WmsPermissionSeed / ERP→ErpPermissionSeed / MES→MesPermissionSeed / PUR→PurPermissionSeed / PLAN+PUB→PlanPubPermissionSeed；
   - **OA/WF 特殊**：贴点面横跨 OawfPermissionSeed + InboxBatchTransferPermissionSeed + FlowTriggerPermissionSeed + WorkCalendarConnectorPermissionSeed（后三个系 WFS 各波追加）——OA 断言面取**种子并集**。
3. **读取种子 Actions**：各种子的 Actions 数组多为 private static readonly——用反射 `GetField(..., NonPublic|Static)` 读取，**零生产代码改动**（不改 private→internal）。注意各波种子元组形状可能不一（(MenuId,Code,Name) 或含 Sort 等），逐个适配。
4. **已知豁免须显式登记**（发现即查证，不许静默吞）：M-OA/WF 票#4 提及「2 个 view 豁免键无 MenuAction 行」——核实这 2 键在种子中 RoleAction/Actions 清单的实际在缺；若贴点有而种子确无，**这正是本测试要抓的形态**——先判断是既知记票现状还是真缺陷：查 plan 文末 M-OA/WF 完成记录与 OawfPermissionSeed 注释；属既知现状则入测试内显式豁免表（附票号注释），属意外缺陷则 BLOCKED 报回附证据。其他模块同理：互锁首跑红的每一条都要逐条归因，不许为绿而豁免。
5. **纯测试任务**：零生产代码改动。
6. **全量绿**：基线=T1 后全量数（T1 报告为准）。提交前全量一次。

## 全局约束
- 锚定表字面量独立写死（可参照真相源文档，勿 import 生产菜单常量）；断言目标=种子类实际内容（这是本测试的对账对象，反射读取属设计内非违规）。
- 每 commit 立即 push。commit 前缀 `test(xsweep):`。

## 报告契约
报告写入 `C:\CP6\.superpowers\sdd\xsweep-t2-report.md`（六模块对账清单、首跑红逐条归因、豁免表依据、RED/GREEN 证据、自审、concerns）。回复只返回（15 行内）：状态、commits、一行测试摘要（全量数）、六模块互锁结果一行表、豁免清单一行、concerns。
