# A：BUG132唯一实施窗口

先读取Issue132、docs/space/reports/2026-09-16-space-publish-recovery.md的撤回原因、runtime.ts、SpaceWmsRuntimeController、SpaceWmsRuntimeService.LoadScopeAsync和当前OpenAPI。旧d2836f33不得cherry-pick；旧33项局部绿灯未覆盖大站点精确子集，不能当作本轮依据。

## 已接受范围

1. 先以真实GUID的大量精确选择复现URL越界；覆盖101个及站点总库位>10,000但请求子集合法。不能省略ID、扩大到整站、截断或静默丢失部分结果。
2. 优先采用向后兼容、单次有界POST查询请求承载完整精确ID集合，现有GET保持兼容，响应DTO不变。让前端inventory入口使用有界传输；查询仍调用现有QueryInventoryAsync，以同一请求/Published范围验证全部ID。等价方案可自行选择，但客户端拆批不得混合版本/来源、吞掉部分失败或越过已有数量限制；若扩大公共语义或服务层范围，先向COORD说明具体变更。
3. 请求体/URL有明确边界并覆盖恰好/超界行为；体积应容纳现有10,000唯一GUID合法集合，去重按既有语义，不能用原始重复项计数误拒合法唯一集合。空列表/缺失/null/非法/空GUID语义需明确且不意外退回整站。超限失败关闭，不添加全站fallback。保留租户、SiteAccess、Current Published、缺失/越权ID、库存来源及StockOverlay拒绝过时响应的合同。
4. 新POST沿用Authorize/RequirePermission和既有CSRF机制，真实HTTP分别检查允许与拒绝路径、JSON绑定/大小限制；不能只用直构Controller或逐字元数据断言代替行为。前端axios已有CSRF支持，禁止全局豁免。不修改Program、公共权限/CSRF中间件、DB模型/迁移或依赖/锁文件。
5. 写集见dispatch。SpaceWmsRuntimeContracts仅增库存请求DTO/边界，生成design-v1.openapi.json仅相关增量；其余runtime接口、MES/WMS查询语义不变。StockOverlay源码仅在本修复确有需要时改，不顺手重构。Application/Infrastructure服务实现只读，现有内存服务测试可补大站点/精确范围证据；不将InMemory称PG验证。接口/SDK变更做相应相关合同验证。
6. 复用既有服务成功证据，新增回归覆盖具体漏洞：大站点精确子集、去重、请求边界、SiteAccess/Published一致及StockOverlay；只编译受影响项目和必要依赖、跑相关测试/页面fixture。没有存储改变，不启动PG或SQL Server。可用自有18232/15232，先查空闲/所有权；禁止访问5432/19991/18080或任何既有服务。

交固定候选和单份docs/quality/evidence/issue-132/DELIVERY.md给D/COORD，列实际失败→成功、证据与剩余范围。D一次集中审查；修正由A唯一实施并只复查影响。COORD在远端main核对及Issue指定原步骤源码/前端复验满足后处理关闭，不追加无关真实数据库部署门禁。

## 执行规则

先读根AGENTS、[本轮入口](README.md)、[资源与写集](dispatch.json)和roadmap。COORD在本准入合并后从最新main创建A产品工作树和A/D任务资料分支。Handoff仍为GTX537/CP6.AI-Handoff，协调分支codex/dev-coordination-20261012，本轮waves/20261017-wave16.md；各自只写tasks/BUG-132或tasks/REVIEW-BUG-132。真实开工发布一次START并安全推送/回读后连续推进，无运输ACK；D收到A固定DELIVERY后接续同一REVIEW，无第二START。派工不代表开工。

精简验证：相关行为、故障和边界加一次集中SPEC/QUALITY；修正只复查影响，未变证据直接引用，不跑全仓或为文档编译。每任务一份DELIVERY/REVIEW。COORD统一共享台账/四状态、正常合并和远端核对；执行者不改main或共享状态。push/PR检查候选及main工作流事件/路径，未变内容复用；Handoff私有/Actions disabled且默认/自身无workflow后推送，不带tag。无Actions、既有数据库/服务操作、部署或force-push授权。
