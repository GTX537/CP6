### Task B-T2: 批量转单端点 + 权限点 seed

**Files:**
- Modify: `CP6.WebApi/Controllers/Oa/InboxController.cs`
- Modify: `CP6.WebApi/Program.cs`（权限点 seed，插在 OA 菜单 seed 块之后，:1354-1358 附近）
- Test:（控制器薄壳走 build + B-T1 服务测试承载 + E-T2 QA harness e2e）

**Interfaces:**
- Consumes: `IInboxService.BatchTransferAsync/BatchTransferPreviewAsync`（B-T1）、`RequirePermissionAttribute(menu, action)`（`CP6.Core/Auth`）。
- Produces: `POST /api/oa/inbox/batch-transfer`、`POST /api/oa/inbox/batch-transfer/preview`（同请求体 `BatchTransferReq`）；权限点 `("oa-inbox","batch-transfer")` = spec `OA.Inbox.BatchTransfer` 的落地映射（C4）。B-T3 前端依赖。

- [ ] **Step 1: 控制器 action**（`InboxController.cs` 追加，DTO 记在文件底部既有 record 区；文件头加 `using CP6.Core.Auth;`）

```csharp
    // ── 在途批量转单（wfs-inbox-ux §3；权限点 = spec OA.Inbox.BatchTransfer → (oa-inbox, batch-transfer)）──
    // 审计：OperLogFilter 全局记 POST 请求体（操作者/from/to）+ 引擎 Wf_FlowHistory/Wf_FlowFormTo 逐条记录（R3）。

    public record BatchTransferFilterReq(string? FlowKey, DateTime? BeforeUtc, List<Guid>? TaskIds);
    public record BatchTransferReq(Guid FromUserId, Guid ToUserId, string? Comment, BatchTransferFilterReq? Filter);

    private static BatchTransferFilter? ToFilter(BatchTransferFilterReq? f) =>
        f is null ? null : new BatchTransferFilter(f.FlowKey, f.BeforeUtc, f.TaskIds);

    [HttpPost("batch-transfer")]
    [RequirePermission("oa-inbox", "batch-transfer")]
    public async Task<IActionResult> BatchTransfer([FromBody] BatchTransferReq r)
    {
        try
        {
            var me = await CurrentUserIdAsync();   // 操作者=登录管理员本人（管理动作不走 act-as）
            return Ok2(await _inbox.BatchTransferAsync(me, r.FromUserId, r.ToUserId, r.Comment, ToFilter(r.Filter)));
        }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpPost("batch-transfer/preview")]
    [RequirePermission("oa-inbox", "batch-transfer")]
    public async Task<IActionResult> BatchTransferPreview([FromBody] BatchTransferReq r)
    {
        try
        {
            return Ok2(await _inbox.BatchTransferPreviewAsync(r.FromUserId, ToFilter(r.Filter)));
        }
        catch (InvalidOperationException e) { return Err(e); }
    }
```

- [ ] **Step 2: Program.cs 权限点 seed** — 在 OA 菜单 seed 块（`MenuId == 733`，Program.cs:1354-1358）之后追加（照 Fin 块 :1128-1158 的既有 idiom；**HasActionAsync 无 admin 旁路，必须授 RoleId=1**）：

```csharp
        // ── OA 信箱批量改派权限点（wfs-inbox-ux §3.1；spec OA.Inbox.BatchTransfer → (oa-inbox, batch-transfer)）──
        {
            var inboxMenu = db.Sys_Menus.FirstOrDefault(m => m.MenuId == 733);
            if (inboxMenu is not null && string.IsNullOrEmpty(inboxMenu.MenuKey))
                inboxMenu.MenuKey = inboxMenu.RoutePath!.Trim('/').Replace('/', '-');   // "/oa/inbox" → "oa-inbox"
            if (!db.Sys_MenuActions.Any(x => x.MenuId == 733 && x.ActionCode == "batch-transfer"))
                db.Sys_MenuActions.Add(new Sys_MenuAction { MenuId = 733, ActionCode = "batch-transfer", ActionName = "批量改派", Sort = 0 });
            if (!db.Sys_RoleActions.Any(x => x.RoleId == 1 && x.MenuId == 733 && x.ActionCode == "batch-transfer"))
                db.Sys_RoleActions.Add(new Sys_RoleAction { RoleId = 1, MenuId = 733, ActionCode = "batch-transfer" });
            db.SaveChanges();
        }
```

- [ ] **Step 3: 编译 + 回归闸 + commit**

```bash
dotnet build CP6.WebApi/CP6.WebApi.csproj
dotnet test CP6.Tests/CP6.Tests.csproj --filter "Oa|Wf"
git add -A && git commit -m "feat(wfs-inbox): B-T2 batch-transfer/preview 端点+RequirePermission(oa-inbox,batch-transfer)+权限点seed"
```

---


---
## 附: R4权限机制
### R4 权限机制（spec 名映射）

- 全库**无** `OA.Inbox.BatchTransfer` 式点分常量；实际机制 = `[RequirePermission(menu, action)]`（`CP6.Core/Auth/RequirePermissionAttribute.cs`，menu=`Sys_Menu.MenuKey`，`/oa/inbox`→`oa-inbox`）+ `Sys_MenuAction`/`Sys_RoleAction` seed。**映射：spec `OA.Inbox.BatchTransfer` → `[RequirePermission("oa-inbox", "batch-transfer")]`**。
- `PermissionService.HasActionAsync` **无 admin 旁路**（Program.cs:1121 注释）→ 必须 seed 动作点 + 授 `RoleId=1`，否则 admin 也 403。OA 菜单 733（`/oa/inbox`）已 seed，但 OA 目前零动作点（本计划为首个）。

## ⚠ 主控交接注记(binding, 覆盖brief陈旧口径)
1. R4"OA目前零动作点(本计划为首个)"已过时: M-OA/WF波建立OawfPermissionSeed逐租户模式, 波③又加FlowTriggerPermissionSeed sibling——batch-transfer种子照该逐租户模式(新建sibling或并入既有, 参考CP6.WebApi/Seed/FlowTriggerPermissionSeed.cs), 锚定MenuId 733(oa-inbox), 非brief可能引的Program.cs内联RoleId=1范本。
2. OawfPermissionAttributeTests守卫: "batch-transfer"须入ActionVocabulary, taggedCount重基线(新增2个带RequirePermission的POST端点: batch-transfer与preview——若brief只提1个端点, C8冲突已定preview同权限点)。仅词表+计数常量+注释可改, 断言逻辑零弱化。跑测验证精确计数勿盲信。
3. InboxController已有5处RequirePermission贴点先例可循。
