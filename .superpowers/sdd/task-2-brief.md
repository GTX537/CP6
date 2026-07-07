### Task 2: BizException 迁 Core + I18nSpaceScreenSeed（零行为变化的基建步）

**Files:**
- Move: `CP6.WebApi/Localization/BizException.cs` → `CP6.Core/Localization/BizException.cs`（**namespace 保持 `CP6.WebApi.Localization` 不变**，文件头加注释「定义于 Core 供服务层抛出；namespace 保留历史值以零涟漪迁移，2026-07-07 波4」）
- Create: `CP6.WebApi/Seed/I18nSpaceScreenSeed.cs`——24 码五语（探查清单为准：E-SPACE-001/002/003/004/006/007/009/301~307/401/402/403/405/407/408/501/502/601 + W-SPACE-404）。**中文消息来源**：throw 站点现有内联消息（有 12 个左右）+ 契约 04 §11 消息表 + 总纲 Spec §16.3 语义（以代码/契约为准）——每码给五语，多义码（如 E-SPACE-002 参数校验族）用概括文案。
- Modify: `CP6.WebApi/Program.cs`（Concat 链 `:1804` 附近加 `.Concat(CP6.WebApi.Seed.I18nSpaceScreenSeed.Items)` + 分段注释）

- [ ] Step 1: 迁移文件（git mv + csproj 无需改[SDK 风格自动包含]），全量编译+测试确认零破坏（1559 级不变）
- [ ] Step 2: 种子 + Concat；`dotnet build CP6.slnx` 0 err；启动期种子逻辑不跑测试（幂等 MERGE 在启动），人工双检 24 码齐全与五语非空
- [ ] Step 3: Commit `refactor(space): BizException 迁 Core（namespace 零涟漪）+ E-SPACE 24 码五语词条种子`

---

