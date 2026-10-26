# W148-01 冷登录当前权限初始化（P1 Issue565）

Main基线9d7be099c0b58d7ed4a686287a3cf107fa4a9f57，COORD单人复用DEV-1/DEV-2，无新工作树。限定WP02-03浏览器权限初始化，不关闭完整授权任务或原七天目标。预计80–120分钟；真实Main START至当前登记36.5分钟，Handoff不叠加，最终以官方finish及wave148-final-verification.json为准。[Issue565](https://github.com/GTX537/CP6/issues/565)。

## 实际问题与改动

main.ts旧预载只在初始路径不是/login时运行，冷登录后的普通业务路由没有统一权限读取；业务页面在setup/onMounted依赖permission.loaded。普通空间菜单ControlTowerLandingView会自行读取，另一个带siteId的空间路由有meta.permission并触发旧守卫，所以先经过空间页暂时恢复。START消息把空间守卫作为概括，源码核对后计划/本报告准确区分两者。

当前所有已认证普通业务路由在页面挂载前等待可读认证状态与当前权限；移除main.ts重复预载，保留W146 router.isReady后挂载。匿名、SSO落地、待2FA、平台管理员/动态菜单与强制改密边界保持。权限空清单继续拒绝，读取失败保持未确认，后续正常导航可重试。

permission store按同代在途请求合并；reset推进代并允许新身份独立请求。身份戳在请求完成和导航放行前复核，旧结果不能写入；旧finally不能清新请求。新增定向用例发现旧导航catch会覆盖新身份已完成的页面，现以导航代区分：过期守卫取消自身，仍当前的初始导航可退回登录以完成bootstrap。不用前端身份戳替代服务器授权，不变更权限动作、原Owner或数据库合同。

## 实际验证与失败记录

- red.json：14项中11过3失败，真实复现冷登录不读取、慢权限下页面提前挂载、并发两次请求。初修green.json14/14通过。boundaries.json19/21：一项显式路由fixture遗漏siteId，另一项真实身份切换后旧导航覆盖；修正后boundaries-final.json21/21通过。
- final.json及final-execution.json：最终一次60/60，0失败/跳过、exit0。包含startup17、permission4、HTTP恢复3、browserAuthentication16、loginExperience10、auth.probe3、SsoIdentityRecovery2和SsoLanding5；21项已包含，不累加数量。实际bootstrap/router/App/AuthenticationBoundary、密码登录/身份切换/退出协议运行生产代码；HTTP与页面数据边界受控，非生产验收。
- build-execution.json：最终vue-tsc --build与Vite build均exit0，日志typecheck.log/build.log；既有体积警告保留。输出web为dev/unknown测试资产，无发布身份/签名或推广。
- browser-red.json：真实Edge新上下文用W147原静态资产接cp6.uk真实登录/API，直接仓库等待10秒仍禁用，页面权限请求0，独立HTTP读取证实服务器有实际动作键；不写浏览器store。
- browser-green-observed.json：候选静态资产仅在测试浏览器替换，真实API/认证不拦截。冷登录直接仓库4、设备6、工单11条且查询200，未经过空间页；仓库深链、刷新、退出后键盘重登通过，匿名深链进入/login且profile401，共8个检查点，无脚本异常。原release.json实际报告ffdb5ac833841287bdb668c7d9a13f3a5b818f6b。首次browser-green.json在工单响应后未等待DOM就读到0行，保留失败；增加响应完成及真实DOM等待后通过，产品源码未因观察器改动而重建。

原始证据统一D:/CP6-local-test/wave148/foundation，final-validation.json记录来源及实际runtime blob。真实demo数据仅证明本次登录/只读回归；无权限、换身份/迟到、失败重试由上述受控生产代码测试覆盖，未操作真实跨租户切换。未部署demo/生产、改变既有数据库、运行Actions或Docker/socket；本轮无数据库改动，不新增无关PostgreSQL运行。

## 集中审查与收尾快照

本COORD对完整diff进行一次SPEC/QUALITY：权限加载时序、现有公开/受限路由、身份与导航竞态、旧响应/请求归属、失败/空集拒绝、相关验证真实范围及文档/链接，无未解决实质合并阻塞。完整diff及审查记录保存在wave148-main-review.diff/json。普通收尾文档只做差异和台账结构检查。

Handoff正常PR https://github.com/GTX537/CP6.AI-Handoff/pull/124，source 308bc3289c5b5b424c5671454c55b73dbaf72949，远端目标9d4c515153955305c3e6c9b5837cbe4d86e87a2a；准确分支已删除，DEV-2已finish释放。Main本提交仍待正常PR、远端采用、原步骤已验证后的Issue关闭、分支清理及finish；如实保持未收尾快照。最终proof由后继准入归档一次，不在main另作自证提交。

仅WP02-03.wave148_permission_bootstrap有限记录；375卡/125完整目标/247运行验收状态不提升。原七天ACTIVE，仅D1/D4阶段闭合，2/7是阶段数，不是工作量百分比。原币种、双Owner工程、工程冻结、正式WO五域及SCHED同UoW/UI，以及D2/D5/D6/D7全部出口继续。
