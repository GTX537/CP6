# Task A-T1 报告：NotifyMatrix 纯函数（三态坍缩 + 遗留兼容 + 反射类型轴）

**状态：DONE**
**Commit：** `d8e49097acb54b405ebaea461484eb41e053b748`（分支 `feat/wfs-inbox-ux`，已 push）
**测试小结：** NotifyMatrixTests 10/10 绿；全量 `dotnet test CP6.slnx` = 1989 passed / 5 skipped（1979 基线 + 10 新增）。

## 交付物
- `CP6.Core/Services/Oa/NotifyMatrix.cs`（新建，88 行）—— `record NotifyMatrixRow`、`static class NotifyMatrix`（`ChannelInApp`/`ChannelEmail`、`IsEnabled`、`Rows`），逐字转写自简报 Step 3。
- `CP6.Tests/Oa/NotifyMatrixTests.cs`（新建，81 行）—— 逐字转写简报 Step 1，外加一条 BranchPruned 补正断言（见下）。

## TDD 记录
- **RED（Step 2）：** 写测试后 `dotnet test --filter NotifyMatrixTests` 编译失败 `CS0103: The name 'NotifyMatrix' does not exist`（15 处引用全报错）—— 符合预期（类型未建）。
- **GREEN（Step 4）：** 落最小实现后同命令 `Passed! 10/10, 0 failed`。
- **回归闸（Step 5）：** 全量 `CP6.slnx` 1989 passed / 5 skipped，零失败。

## BranchPruned 适配详情（控制器授权，非静默漂移）
简报 R1 摘录称 `BranchPruned` "尚未合入"，属预检漂移 —— 实读证实已由内核 hardening 波合入：
- `CP6.Entity/DomainModels/Wf/WfNotificationType.cs:22` —— `public const int BranchPruned = 5;` 已在位（现共 5 个常量）。
- `CP6.WebApi/Services/PersistentWfNotifier.cs:166-199` —— `BranchPrunedAsync` 已实现，其 `:196-198` 与 `FlowRejected` 完全同款邮件动作（`GetNotifyPrefsAsync` → `prefs.Email` → `TrySendEmailAsync`）。**故 `branchPruned` 支持标志 = (inApp:true, email:true) 是正确的**，与简报 Step 3 `Support` 字典的预留项一致，实现无需改动。

**测试适配（唯一一处）：** 简报给定的 `Rows_ReflectsEnum_WithSupportFlags` 本已前向兼容——它对 4 个已知类型用 `Assert.Contains` 正向断言，对 `branchPruned` 仅用 forgiving `foreach`（存在时才断言双通道 true，不做存在性负断言）。**它并未硬断言"恰好 4 行"，也未枚举行集**，因此严格意义上无需修改。鉴于 BranchPruned 现已确定在位，我**追加一条正向断言**强化覆盖：
```csharp
Assert.Contains(rows, r => r is { TypeKey: "branchPruned", TypeValue: 5, InAppSupported: true, EmailSupported: true });
```
该断言现在为绿（反射轴此刻确含 5 行）。这是控制器授权的增强，已在测试内以注释标注来由。原 `foreach` 保留（防御任意合并顺序）。

## 自审
- **三态坍缩**：空串/`{}`/`{"notify":{}}`/类型对象无通道键/畸形 JSON → 全部 true。畸形 JSON 由 `try/catch(JsonException)` 兜底，与 `PrefService.ParseNotifyPrefs`（`:38-62`）畸形回落 Default 语义一致。
- **遗留扁平逐位等价（C2）**：类型键非对象时经 `LegacyKeyMap` 回落——事件键=false→双通道关；全局 `email`=false→仅邮件关（`eventOn && emailOn`）。与 `ParseNotifyPrefs` 的 `Get()`（仅字面 `false` 为关，缺键/true/非布尔→true）逐位对齐。新类型（无遗留映射）→ 直接 true。
- **新形态优先**：类型键为 `Object` 即走新矩阵分支，无视同级遗留 `email` 全局开关（测试 `IsEnabled_NewShapeWinsOverLegacy` 覆盖）。
- **反射类型轴数据驱动**：`Rows()` 反射 `WfNotificationType` 全部 `public const int`，PascalCase→camelCase，按 `TypeValue` 排序。新增枚举常量将自动长出行。
- **零迁移 / 零引擎改动 / 零跨模块污染**：仅新增两文件，均为纯函数/测试，无 DbContext/实体/DI 触碰。`dotnet ef migrations has-pending-model-changes` = "No changes"。
- **surgical git add**：`git show --stat HEAD` 仅两个简报文件（169 insertions），启动时既存的 `.superpowers/sdd/*.md` 未污染本 commit。

## 关注点 / 交接
- 无阻塞。A-T2/A-T3/A-T4 可直接消费 `NotifyMatrix.IsEnabled` / `Rows()` / `ChannelInApp` / `ChannelEmail` / `NotifyMatrixRow`（共享契约名字精确匹配）。
- `timeout` 行双通道禁用（`Support` = (false,false)），R1 核定其全库无生产者；将来接独立发送路径时改 `Support` 字典即自动点亮，无需动 UI。
- 本报告文件（`.superpowers/sdd/w4-AT1-report.md`）不在功能 commit 内，按台账惯例另行入库。
