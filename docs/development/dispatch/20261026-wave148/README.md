# W148 登录后当前权限初始化（Issue565）

COORD在本对话单人执行，沿用systematic-debugging、TDD与writing-plans的根因/红绿/步骤；项目规则要求一次任务级集中SPEC/QUALITY，已有授权直接实施。原七天目标ACTIVE，W147已合并及释放，不重复。

## 问题、前置与范围

[P1 Issue565](https://github.com/GTX537/CP6/issues/565)：从/login冷登录后，服务器my-actions已授456键，但普通仓库页面permission.loaded仍false；先经过空间控制塔后正常。源码核对确认普通空间菜单的ControlTowerLandingView会自行加载权限；另一个带siteId的空间路由具有meta.permission，也会触发现有守卫加载。main.ts只在初始启动不是/login时加载，普通业务路由及AuthenticationBoundary/navigateAuthentication均没有统一初始化。业务页在挂载时依赖当前权限，故不能只在挂载后补发异步请求。

Main最新9d7be099c0b58d7ed4a686287a3cf107fa4a9f57，Handoff目标1a939148968245096e833d4140cee80577975885。四仓AGENTS/origin/引用、开放PR、任务Actions与重复任务预检wave148-preflight.json通过；WP02-03为Main当前授权与撤权最终门，本修复仅关联其浏览器当前权限初始化采用，不提升完整任务。另一个DEMO任务已自行释放集成席位；本轮仅领取DEV-1/2，保持其内容，不使用额外目录或改变demo。

W147最终proof用于一次归档W147与关闭唯一WP04-33.wave147_uom_owner，完整375卡/125目标/247验收状态不变。原单位提供者已采用，币种、双Owner工程、冻结/正式工单/SCHED和D2/D5/D6/D7仍保留。

|任务|预计实际执行|
|---|---|
|核对真实Issue/源码调用链、自动化与浏览器失败证据|20–30分钟|
|初始化顺序修复、当前身份/迟到响应/失败重试定向回归|20–30分钟|
|类型构建、候选静态资产接真实demo API冷登录验证|20–30分钟|
|一次审查、交付/台账、正常PR/Issue/分支与finish|20–30分钟|

合计80–120分钟，最终按Main真实START至finish登记，不与Handoff叠加。持续目标沿用原CP6-7D-20261022，不创建替代目标。

## 设计与边界

以现有工作正常的meta.permission路由守卫为依据，在所有已认证业务路由进入前加载当前权限，而不要求用户先访问某个页面。登录/SSO落地/待2FA、强制改密原入口保持独立，不预取尚不可用的业务权限；W146初始router.isReady顺序保留。移除main.ts重复的启动后预加载，使首次路由与后续登录使用相同入口。

等待权限返回时固定当前UI身份戳；身份/租户/模拟身份发生变化，旧导航不能用旧结果放行。permission store保留generation reset并合并同代并发读取；reset允许新身份独立读取，旧请求结束不能清掉新身份的在途请求。加载失败保持未确认状态与拒绝能力，后续正常导航可重查；不通过前端缓存、默认允许或伪造权限解决。

业务页仍按真实动作键显示能力，服务器权限/租户/独立Owner最终门不变。没有权限的账户即使成功加载空清单也不得启用操作。需验证身份切换、退出重登、慢返回/失败重试、匿名/强制改密/2FA及首次深链；实际浏览器从冷登录直接进仓库、设备及工单，不能通过空间页预热或浏览器store注入。

## 实施步骤

- [x] 在实际bootstrap/router/App/AuthenticationBoundary和真实permission store边界复现：完整授权角色响应已准备，但冷登录后普通页面不发读取/不等待初始化。保留自动化红灯及已有真实浏览器失败证据。
- [x] 修改cp6.web/src/router/index.ts的当前权限初始化顺序与身份等待检查、stores/permission.ts的同代在途读取，移除main.ts重复预载；不修改业务页来掩盖根因。
- [x] 扩展router/startup.spec.ts与stores/permission.authRecovery.spec.ts的实际行为：冷登录、慢权限、空权限、失败再进入、身份变化/退出重登/迟到结果、并发读取；实际身份协议与HTTP/SSO既有相关成功范围按输入变化选取。
- [x] 最终相关Vitest、vue-tsc与Vite；任务自有浏览器上下文仅替换候选静态资源，真实demo API及登录处理所有身份/权限/业务查询。保留真实未授权、网络/环境限制与原失败，不将测试资产视为demo推广。
- [ ] 集中审查完整diff，单份DELIVERY、台账/生成视图/四状态、Handoff START/DELIVERY正常采用；Main正常PR后核远端、原步骤回归，才关闭Issue565；准确分支清理和官方finish。

原始本机验证目录D:/CP6-local-test/wave148/foundation；复用固定node_modules，不复制源码/依赖。不运行Actions、不迁移或替换既有环境、不发布demo/生产。后继回到原WP04-33币种/双Owner工程及完整七天出口。

## 实际交付

见[单份报告](../../evidence/W148-01/DELIVERY.md)。当前登记36.5分钟；前四步完成，集中审查通过，最后生命周期步骤待Main正常PR/Issue/finish后由最终proof确认。Handoff已采用并释放，原七天ACTIVE；不把快照写作最终完成。
