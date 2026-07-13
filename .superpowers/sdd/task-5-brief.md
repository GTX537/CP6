### Task 5: SpaceLocateController 裸 BadRequest → BizException(E-SPACE-601/004)

**Files:**
- Modify: `CP6.WebApi\Controllers\Space\SpaceLocateController.cs:27,41`
- Test: 既有 SpaceLocate 测试(若断言了裸 400 信封需同步改断言为 BizException 语义)

**要点:** 两处 `return BadRequest(new { code=400, message="E-SPACE-xxx" })` 改 `throw new BizException("E-SPACE-601")` / `("E-SPACE-004")`,走 BizExceptionMiddleware 按 culture 翻译(词条已在 seed,零新增)。

- [ ] **Step 1: 失败/改写测试**(断言 message 不再是裸码——单测层面断言抛 BizException 且 code 正确)
- [ ] **Step 2: 红 → 实现 → 绿 → 全量绿**
- [ ] **Step 3: Commit + push**(`fix(space): 波5 E-SPACE-601/004 BizException化——定位端点统一走中间件翻译`)

---

