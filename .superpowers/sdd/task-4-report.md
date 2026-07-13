### Task 4 报告: Space 波5 UpdateSite 锚护栏(E-SPACE-406)

**Status:** ✅ 完成

**Commit:** c79c97e (feat/space-wave5, 已 push)

**改动文件:**
- `CP6.Core/Services/Space/SpaceMasterService.cs` — UpdateSiteAsync 加锚护栏(取实体后、赋值前)
- `CP6.WebApi/Seed/I18nSpaceScreenSeed.cs` — E-SPACE-406 五语词条(发布/停用/删除段, E-405 与 E-407 之间)
- `CP6.Tests/SpaceMasterServiceTests.cs` — 3 测 + SeedSiteWithPublishedLocationAsync 帮手(挨着既有 Site 测试)

**实现要点:**
- 护栏条件: `e.SiteCode != d.SiteCode || e.WarehouseCd != d.WarehouseCd` 才查询(锚未变不跑, 避免每次改名多两查)
- 命中查询: `Space_Floors.Where(SiteId==id).Select(Id)` → `Space_Locations.AnyAsync(FloorId∈floors && Status==1 && !IsDeleted)` → 抛 `BizException("E-SPACE-406")`(默认 400)
- 实体链: Space_Location 无 SiteId, 走 Location.FloorId → Space_Floor.SiteId(brief 指定路径)
- SiteName/Address/坐标/Enable 改动不受限

**测试:** 全量 1816 passed / 5 skipped(基线 1813+3 新, 达标)。三测 TDD 红(2 guard fail, SiteName-only pass)→ 实现 → 绿。

**Self-review:**
- ja 词条用 brief 指定原文「公開済みロケーションが存在するため、サイトコード/倉庫コードは変更できません」✓
- 连字符键 E-SPACE-406(仓约定)✓；词条位置在发布/停用/删除段 ✓
- `!IsDeleted` 显式保留(Space_Location : BaseBizEntity 有 IsDeleted; 全局软删过滤存在时冗余但无害, 与 brief 口径一致)
- 拒绝后锚字段原样(测试断言 SiteCode/WarehouseCd 未变)✓
- 护栏置于 FirstOrDefault(取实体)之后、字段赋值之前——锚字段仍持旧值可比对 ✓

**疑虑:** 无。InMemory 测试口径与既有 SpaceMasterServiceTests 一致; 真库集成非本任务范畴。
