### Task 4: UpdateSite 锚护栏(E-SPACE-406)

**Files:**
- Modify: `CP6.Core\Services\Space\SpaceMasterService.cs:60-74`(UpdateSiteAsync)
- Modify: `CP6.WebApi\Seed\I18nSpaceScreenSeed.cs`(发布/停用/删除段加 E-SPACE-406)
- Test: `CP6.Tests\Space\`(挨着既有 SpaceMasterService 测试)

**要点:** `SiteCode`/`WarehouseCd` 任一与库中值不同,且站点下(`Floor.SiteId==id` → `Location.FloorId∈floors`)存在 `Status==1 && !IsDeleted` 库位 → `throw new BizException("E-SPACE-406")`。词条五语,语义:「站点下存在已发布库位,不可修改站点编码/仓库码(WMS 锚)」/ ja:「公開済みロケーションが存在するため、サイトコード/倉庫コードは変更できません」(En/ZhTW/Ko 同义自拟)。**其余字段(SiteName/Address/坐标/Enable)不受限**。两锚字段未变时护栏不触发(护栏查询也不要跑,避免每次改名多两查)。

- [ ] **Step 1: 失败测试×3**(①改 SiteCode+有已发布库位→BizException E-SPACE-406 ②改 WarehouseCd 同 ③只改 SiteName+有已发布库位→成功)
- [ ] **Step 2: 红**
- [ ] **Step 3: 实现护栏+词条**
- [ ] **Step 4: 绿+全量绿**
- [ ] **Step 5: Commit + push**(`feat(space): 波5 UpdateSite锚护栏E-SPACE-406——已发布库位在时拒改SiteCode/WarehouseCd`)

---

