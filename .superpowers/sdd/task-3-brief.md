### Task 3: Space 服务 BizException 化（56 throws + 36 catch + 7 测试文件）

**Files:**
- Modify: `CP6.Core/Services/Space/` 8 文件（CodeEngineService 10 处/SpaceMasterService 26/SceneService 7/LocationPublishService 5/TemplateService 3/ConnectorService 3/SceneIoService 1/LocationGeometryService 1）——`throw new InvalidOperationException("E-SPACE-xxx[: 中文]")` → `throw new BizException("E-SPACE-xxx")`；`SpaceMasterService:400` → `new BizException("E-SPACE-009", 409)`；同文件补 `using CP6.WebApi.Localization;`
- Modify: `CP6.WebApi/Controllers/Space/` 6 控制器——删 36 处 `catch (InvalidOperationException)`（try 壳一并简化）；**保留** LocationPublishController 3 处 DbUpdateConcurrencyException 409 catch
- Modify: 7 测试文件（SpaceMaster/Scene/LocationPublish/CodeEngine/Template/BindCodes/ConnectorServiceTests）——`ThrowsAsync<InvalidOperationException>` 32 处 → `ThrowsAsync<BizException>`；`StartsWith("E-SPACE-xxx", ex.Message)` 15 处 → `Assert.Equal("E-SPACE-xxx", ex.Code)`；其余码文本引用同步
- **CodePrecheck.Validate 返回的错误码字符串**（E-303/305/306 是返回值非异常）**不动**——它们进 PrecheckErrors 列表由前端展示，非 throw 路径

注意：BizExceptionMiddleware 只在 HTTP 管道生效——单测直接调 Service 断言 BizException 本身，不经中间件，无翻译依赖。前端影响：错误 message 从「E-SPACE-401: 中文」变为词条译文（miss 回退码本身）——波3 各页 catch 逻辑不解析 message 内容（只有 409 分支看 status），无前端改动；**但发布中心 spec 若 mock reject 消息文本需核对**。

- [ ] Step 1: 按文件逐个替换（一文件一编译），controller catch 清理，测试文件更新
- [ ] Step 2: 全量 `dotnet test` 绿（数量不变 1559 级）；前端 `npm run test` 绿（확认无依赖 message 文本的 spec）
- [ ] Step 3: Commit `refactor(space): 错误码 BizException 化——56 throw/36 catch/7 测试文件，中间件统一翻译`

---

