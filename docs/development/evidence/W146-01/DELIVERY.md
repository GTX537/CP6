# W146-01 已登录产品刷新与深链启动

[Issue556](https://github.com/GTX537/CP6/issues/556)，Main基线2522a2c9145cff05dec1811e2c6ff7609f5a23f5，COORD单人，固定DEV-1/DEV-2复用。唯一运行时修改：main.ts在app.mount前等待router.isReady()。首次路由守卫完成认证/菜单装载及重匹配后才挂载App/AuthenticationBoundary；不改变服务器身份、原始权限、强制改密、2FA或独立Owner门。

## 根因及红绿证据

旧main先挂载认证边界，边界onMounted调用addDynamicRoutes并把dynamicRoutesAdded设true；最初已按空layout解析的深链尚未完成，随后守卫跳过其重匹配分支，提交未匹配的旧目标。URL正确但RouterView无组件。边界也可能在首次导航到达SSO落地前按占位路径把匿名用户送login。菜单导航时路由已经安装，因此正常。

本轮原demo真实浏览器复现刷新前10行、刷新后0行/body为空、profile200：D:/CP6-local-test/wave146/foundation/browser-red-diagnostic.json及同名png。原第一次401观测另保留，不能用它推断路由根因。实际main/bootstrap/router/App/AuthenticationBoundary自动回归修复前6项中2项失败，分别为认证深链空白和SSO错误重定向；red-startup-bounded.json。没有用模拟router或源码字符串断言证明行为。

修复后最终10项启动行为通过，包括已/未预装菜单、query/hash、菜单切换、匿名围栏、SSO、强制改密、pending2FA两页、unknown隔离、无菜单独立窗口和平台非管理员围栏；另40项已有认证/HTTP恢复/权限/SSO行为通过。ui.json原46含早期6项启动，与startup-final.json的最终10项去重后共50，0跳过。真实页面载荷/翻译在启动单测内stub，完整真实UI/API由下面浏览器验证，不把单测当服务端身份资格验收。

最终vue-tsc --build实际exit0（typecheck-final.log/session18218），Vite候选构建实际exit0（build.log/session67771）。只构建一次运行时代码；新增后续测试不影响候选资产。源基线、唯一运行时blob和实际返回41份资产SHA-256保存在final-validation.json，最终提交/远端树进一步核对；release.json仍明确dev/unknown，不伪造正式发布身份。

## 真实浏览器复测和边界

候选静态资源只在本任务自有Edge浏览器中拦截替换，API仍是原http://localhost:28082的demo ffdb5ac833841287bdb668c7d9a13f3a5b818f6b及其PostgreSQL，旧环境/镜像/数据库均未替换。浏览器通过原表单真实登录，未写入假认证/菜单或mock API。最终Verify-Candidate.cjs执行exit0（session90935），browser-green.json的7个检查点：正常登录菜单、刷新、query/hash深链、新标签和退出完成后重新登录均显示10行/profile200；退出完成和独立匿名深链显示login/0行/profile401；pageErrors为空。实际截图browser-green.png已人工查看，列表和布局正常。

保留观察器/扩展验证失败：最初通用表格选择器包含正在退出的dashboard8行，改为当前产品.cp-list后准确统计10行；重新登录曾在退出协议尚未结束就重新加载文档，后续按真实anonymous终态/恢复遮罩隐藏等待，再以正常表单Enter重登通过；原点击动作超时记录未改为通过。一次新标签profile401观测亦保留，不承诺所有认证/环境可用性。最终预期退出/匿名401与既有通知WebSocket Local Network Access控制台错误仍在原证据，后者不属于产品路由故障，未修复或宣称通过。公网cp6.uk只读release探测403，未绕过或部署；本Issue原步骤在本机原目标/API上验证，公网推广与生产资格不在本轮。

证据总入口D:/CP6-local-test/wave146/foundation/final-validation.json；原失败日志、当前红/绿及脚本均保留。API/DB实现未改，无新PG集成测试或迁移；复用既有服务端的真实适用范围。一次任务级SPEC/QUALITY覆盖完整diff、启动等待/认证门、真实证据和台账，见wave146-main-review.json；文档只检查diff/链接。无Actions、生产或已有环境替换。

## 交付、台账和后继

W145按其最终proof一次归档，W145类型修复限定记录核销，不重复。W146 START提交主题误沿用旧文字，但消息正文为本Issue，本轮DELIVERY明确更正且不重写提交。限定记录关联更正为WP02-18工作台菜单/动态路由，WP02-13审计卡无本轮交付；正式任务状态、125目标和247验收均不提升。七天ACTIVE，仍仅D1/D4阶段关闭，不以2/7冒充实际工作量完成率，不重算历史65%或42–70小时。

Handoff [https://github.com/GTX537/CP6.AI-Handoff/pull/122](https://github.com/GTX537/CP6.AI-Handoff/pull/122)已正常合并、准确任务分支删除并finish释放DEV-2；Main本提交仍待PR采用及Issue关闭，最后分支/工作区/席位结果以wave146-final-verification.json为准，避免提前记已完成。固定目录/依赖缓存/根目录用户资料保留，没有增树。

计划80–120分钟，当前登记32.46分钟，最终按Main START至finish不叠加核对，包含复现、失败调查、验证和运输，非纯编码时间。单位Owner原120–180分钟计划未实施，完整单位/工程/排程等范围保留。下一步：Resume WP04-33 versioned unit profile and dual-Owner engineering mapping, then the retained original D3 Root/Snapshot/Occurrence and SCHED same-UoW. Preserve all D2/D5/D6/D7 exits.
