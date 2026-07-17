# X-SWEEP T1 任务简报：HttpPatch 反射谓词 sweep（八文件齐补）

## 背景与位置
六模块授权波 P0 已收官（M-PLAN/PUB 2026-07-17 完成）。本波=跨波双票 sweep 第一票：各波反射 fail-closed 测试的 mutating 谓词不含 `HttpPatchAttribute`——未来任何控制器新增 `[HttpPatch]` 写端点会**静默逃出扫描面**（fail-open），漏贴 RequirePermission 不报红。票源：M-PUR 完成记录票#2（plan docs/superpowers/plans/2026-07-07-module-waves-crosscutting.md 文末）+ M-PLAN/PUB T3 的 NoPatchEndpoints_InScope 自检注释钉票。

## 现状（主控已 grep 实证）
八个反射测试文件，七个零 HttpPatch 提及，PlanPub 一处提及（在自检测试，谓词本体是否含须你核实）：
- CP6.Tests/Wms/WmsPermissionAttributeTests.cs
- CP6.Tests/ErpPermissionAttributeTests.cs
- CP6.Tests/MesPermissionAttributeTests.cs
- CP6.Tests/OawfPermissionAttributeTests.cs
- CP6.Tests/PurPermissionAttributeTests.cs
- CP6.Tests/PlanPubPermissionAttributeTests.cs
- CP6.Tests/Fin/FinPeriodPermissionAttributeTests.cs
- CP6.Tests/Space/SpacePermissionAttributeTests.cs

## 需求
1. 逐文件找到 mutating 谓词（IsMutating 或同型：HttpPost/HttpPut/HttpDelete attribute 判定），**补 `HttpPatchAttribute`**。八文件风格各异（不同波次先例演化），改动照各文件自身惯用法最小侵入，勿顺手重构。
2. PlanPub 的 `NoPatchEndpoints_InScope` 自检测试：其注释钉的跨波票即本任务——更新注释为「票已于本 sweep 落地，谓词现含 HttpPatch；本测试继续钉住扫描面 0 PATCH 的现状事实」（自检保留，语义从『钉票』变『现状 pin』）。其他文件若有同型自检/注释一并对齐。
3. **RED 实证**：全仓当前零 PATCH 端点（你先 grep `HttpPatch` 于 CP6.WebApi/Controllers 确证），谓词补齐后全量应持平绿。为证明谓词真有牙：任选一个控制器**临时**加一个裸 `[HttpPatch]` action（工作区实验，不 commit），确认对应模块反射测试红（漏贴报错），随即还原，RED 输出实录进报告。八文件不必逐个实弹，抽 2 个不同风格文件实证即可，其余以谓词 diff 一致性论证。
4. **纯测试改动**：零生产代码 commit（实验性 PATCH action 必须还原干净，`git status` 实证）。
5. **全量绿**：基线 2190 绿/5 skip 不许跌。提交前全量一次（前台串行，8GB 机）。

## 全局约束
- 每 commit 立即 push。commit 前缀 `test(xsweep):`。
- 不动各波真相源文档、不动种子、不动控制器。

## 报告契约
报告写入 `C:\CP6\.superpowers\sdd\xsweep-t1-report.md`（八文件逐个改动点、RED 实录、还原实证、自审、concerns），git add -f 随 commit。回复只返回（12 行内）：状态、commits、一行测试摘要（全量数）、八文件改动一行表、concerns。
