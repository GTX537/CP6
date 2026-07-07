### Task 8: 场景保存状态机护栏（H1：堵住 Status/CodeOrigin 后门）

**Files:**
- Modify: `CP6.Core/Services/Space/SceneService.cs:207-235`（Locations 差量块）
- Test: `CP6.Tests/SceneServiceTests.cs`（新增 2 个测试；该文件帮手为 `Make()`，返回 `(db, svc)`）

**Interfaces:**
- Consumes: 无新依赖
- Produces: 行为变化——场景保存对已存在库位**不再接受** `Status`/`CodeOrigin` 覆盖；新建库位强制 `Status=0, CodeOrigin=1`。状态流转唯一通道：publish / deactivate / adopt / bind-codes。**注意**：若 `SceneServiceTests`/`SceneIoServiceTests` 中存在断言"场景保存能写入 Status=1 或 CodeOrigin=2"的既有测试，那是 H1 漏洞的固化，应更新断言并在 commit message 说明；若 `SceneIoService` 导入路径依赖经 SaveSceneAsync 保留 Status，则导入处应改为直接操作实体（绕开场景保存护栏），执行时核实并报告。

- [ ] **Step 1: 写失败测试**

`CP6.Tests/SceneServiceTests.cs` 末尾追加：

```csharp
    [Fact]
    public async Task SaveScene_CannotFlipPublishedStatus_OrCodeOrigin()
    {
        // H1：场景保存曾可任意覆盖 Status/CodeOrigin——绕过 publish/deactivate 状态机的后门
        var (db, svc) = Make();
        var floorId = Guid.NewGuid();
        var locId = Guid.NewGuid();
        db.Space_Locations.Add(new Space_Location
        {
            Id = locId, FloorId = floorId, RackId = null,
            Placed = false, Status = 1, CodeOrigin = 2, LocationCode = "EXT-001", Version = 3
        });
        await db.SaveChangesAsync();

        await svc.SaveSceneAsync(floorId, new SceneSaveDto
        {
            Locations = new List<SceneLocationSaveDto>
            {
                new SceneLocationSaveDto { Id = locId, RackId = null, Col = 1, Level = 1, Depth = 1, Placed = false, Status = 0, CodeOrigin = 1 }
            }
        }, "u");

        var loc = await db.Space_Locations.SingleAsync();
        Assert.Equal(1, loc.Status);       // 发布状态不被场景保存改写
        Assert.Equal(2, loc.CodeOrigin);   // 来源标签（对账依据）同理
    }

    [Fact]
    public async Task SaveScene_NewLocation_ForcedDraft()
    {
        var (db, svc) = Make();
        var floorId = Guid.NewGuid();

        await svc.SaveSceneAsync(floorId, new SceneSaveDto
        {
            Locations = new List<SceneLocationSaveDto>
            {
                new SceneLocationSaveDto { Id = Guid.NewGuid(), RackId = null, Col = 1, Level = 1, Depth = 1, Placed = false, Status = 1, CodeOrigin = 2 }
            }
        }, "u");

        var loc = await db.Space_Locations.SingleAsync();
        Assert.Equal(0, loc.Status);       // 编辑器新建恒草稿；发布走 publish、采纳走 adopt
        Assert.Equal(1, loc.CodeOrigin);
    }
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~SaveScene_CannotFlip|FullyQualifiedName~SaveScene_NewLocation_ForcedDraft"`
Expected: 2 个 FAIL（当前 DTO 值直通落库）。

- [ ] **Step 3: 加护栏**

`SceneService.cs` Locations 差量块（:207-235）改为：

```csharp
                if (existing != null)
                {
                    existing.RackId     = ld.RackId;
                    existing.Col        = ld.Col;
                    existing.Level      = ld.Level;
                    existing.Depth      = ld.Depth;
                    existing.Placed     = ld.Placed;
                    // H1 状态机护栏：Status/CodeOrigin 不接受场景保存覆盖——
                    // 状态只经 publish/deactivate 通道流转（ch04 §4），来源标签只在生码/采纳时落定
                    existing.Modifier   = user;
                    existing.ModifyDate = DateTime.Now;
                }
                else
                {
                    _db.Space_Locations.Add(new Space_Location
                    {
                        Id         = ld.Id ?? Guid.NewGuid(),
                        RackId     = ld.RackId,
                        FloorId    = floorId,
                        Col        = ld.Col,
                        Level      = ld.Level,
                        Depth      = ld.Depth,
                        Placed     = ld.Placed,
                        Status     = 0,   // H1：编辑器新建恒草稿（发布走 publish、采纳走 adopt/bind-codes）
                        CodeOrigin = 1,
                        Creator    = user,
                        CreateDate = DateTime.Now
                    });
                }
```

- [ ] **Step 4: 跑 Scene 全套测试确认无回归**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~SceneServiceTests|FullyQualifiedName~SceneIoServiceTests|FullyQualifiedName~BindCodesTests"`
Expected: 全 PASS。若有既有测试断言场景保存写入 Status/CodeOrigin → 按本 Task 头部说明处理并报告。

- [ ] **Step 5: Commit**

```bash
git add CP6.Core/Services/Space/SceneService.cs CP6.Tests/SceneServiceTests.cs
git commit -m "fix(space): 场景保存状态机护栏——Status/CodeOrigin 拒绝 DTO 覆盖，新建强制草稿（评审 H1）

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

