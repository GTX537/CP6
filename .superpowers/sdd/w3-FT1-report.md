# Task F-T1 报告：FlowTriggerValidator 全量保存时校验（E-WF-022/023 双检之保存侧）

## STATUS: DONE ✅

- 提交 SHA: `c62ffa1ec852f93bf912b7fe8e69b0e26f53fe6d`
- 分支: `feat/wfs-event-trigger`（已 push 到 origin，f2cab75..c62ffa1）
- 测试小结: 新增 7 例 FlowTriggerValidatorTests 全绿；全量 `dotnet test CP6.slnx` = **1969 passed / 5 skipped / 0 failed**（baseline 1962 + 7 新例 = 1969，无回归）。

## 做了什么

将 E-T1 遗留的 `FlowTriggerValidator.cs` 最小桩（仅 FlowKey 必填 / 类型范围 / StarterUserId 非空）扩成 spec §5 全量保存侧校验：

- **通用**：FlowKey 空、类型越界、StarterUserId=Empty。
- **Timer**：`WfCronHelper.IsValid(cron)` 失败 → E-WF-022；`varsJson` 非空但不是 JSON 对象 → E-WF-022。
- **Event**：`eventKey` 缺失或不匹配 `^[A-Za-z0-9_.-]+\|[A-Za-z0-9_.-]+$`（即 `{SourceModule}|{HookName}`）→ E-WF-022；`varsMap` 变量名空或点路径/模板值空 → E-WF-022。
- **Message**：`varsSchema` 含空字段名 → E-WF-022。
- **引用存在性（保存侧）**：`Sys_Users.Any(Id==starter && Enable)` 否 → E-WF-022；`Wf_FlowDefs.Any(FlowKey==req.FlowKey && Enable)` 否 → E-WF-023（目标流程不存在或未启用）。

签名不变（`static Task ValidateAsync(CP6Context, FlowTriggerSaveReq, CancellationToken)`），E-T1 调用点（FlowTriggerAdminService.CreateAsync/UpdateAsync）零改动。

## E-T1 错误码语义分歧的处置

E-T1 评审曾指出桩把「FlowKey 必填」标为 E-WF-023，与码表（023=目标流程不可发起）语义错配。F-T1 全量版按 brief-verbatim 的实现**保留** FlowKey-空 用 E-WF-023、而把「目标流程不存在或未启用」这一真正的 023 语义也归到 E-WF-023。即：023 现在同时覆盖「FlowKey 缺失」与「目标流程不可发起」两种目标流程侧问题——两者都属「无法定位到一个可发起的目标流程」，语义已收敛一致，不再是 022/023 混淆。配置类无效（cron/eventKey/varsMap/starter）统一归 022。此为 brief 权威实现，符合 spec §5 码表分工。

## 门禁核对

1. ✅ 新测试全绿；`dotnet test CP6.slnx` 全绿（1969/5/0）。
2. ✅ `dotnet ef migrations has-pending-model-changes` → "No changes have been made to the model since the last migration."
3. ✅ `git show --stat HEAD` 仅 brief 两文件：`FlowTriggerValidator.cs`（改）+ `FlowTriggerValidatorTests.cs`（新）。**无需改动任何 E-T1 测试 fixture**——FlowTriggerAdminTests 的 9 例 fixture 本就 seed 了 enabled 的流程+发起人并用合法 cron/schema 配置，全量校验对它们无破坏（16 例 validator+admin 联合跑全绿实证）。

## TDD 轨迹

- RED: 新测试先跑 → 6 failed / 1 passed（ValidThreeTypes 因桩不抛而误过）。
- GREEN: 实现后 validator+admin 16 例全绿，再全量 1969 绿。

## 关注点 / 遗留

- 无必修遗留。引擎零改动、零迁移，surgical add。
- 运行时侧（FireAsync, A-T2）已存在，与本保存侧构成 spec 所述双检（发起人/流程保存后可能被停用）。
