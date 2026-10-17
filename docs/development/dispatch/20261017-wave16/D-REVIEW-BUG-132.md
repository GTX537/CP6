# D：BUG132准备与唯一集中审查

只读准备Issue132、旧补丁撤回原因、现有GET/前端inventory、服务LoadScopeAsync、OpenAPI和StockOverlay。重点：保留完整精确集合，站点>10,000而子集合法仍可用；去重/空集合/null不能暗中变整站；请求大小有界，数量不缩水；SiteAccess/tenant/Current Published在同一请求保持一致，权限与CSRF不绕过。准备不当作候选审查。

A固定DELIVERY到达后，在同一tasks/REVIEW-BUG-132/REVIEW.md完成一次集中SPEC/QUALITY。核对完整相关diff、HTTP绑定/大小/权限拒绝、OpenAPI/SDK兼容、大站点子集及StockOverlay证据，拒绝以旧33项通过或静态字段断言代替行为。复用未变成功结果，不为SHA变化重跑。

有实质阻断给固定文件/行与触发，A修正，D只复查影响；无阻断给本地Ready to merge Yes并说明适用范围。Issue明确只验证源码及受影响前端，不替换服务、不操作数据库；本轮不要求PG/SQL Server/实际DEV/生产矩阵，不将InMemory或页面fixture说成业务验收。COORD负责共享状态、正常集成、远端核对和关闭。D无产品写集、端口或环境操作权。

## 执行规则

先读根AGENTS、[本轮入口](README.md)、[资源与写集](dispatch.json)和roadmap。COORD在本准入合并后从最新main创建A产品工作树和A/D任务资料分支。Handoff仍为GTX537/CP6.AI-Handoff，协调分支codex/dev-coordination-20261012，本轮waves/20261017-wave16.md；各自只写tasks/BUG-132或tasks/REVIEW-BUG-132。真实开工发布一次START并安全推送/回读后连续推进，无运输ACK；D收到A固定DELIVERY后接续同一REVIEW，无第二START。派工不代表开工。

精简验证：相关行为、故障和边界加一次集中SPEC/QUALITY；修正只复查影响，未变证据直接引用，不跑全仓或为文档编译。每任务一份DELIVERY/REVIEW。COORD统一共享台账/四状态、正常合并和远端核对；执行者不改main或共享状态。push/PR检查候选及main工作流事件/路径，未变内容复用；Handoff私有/Actions disabled且默认/自身无workflow后推送，不带tag。无Actions、既有数据库/服务操作、部署或force-push授权。
