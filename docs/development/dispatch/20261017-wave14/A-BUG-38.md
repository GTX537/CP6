# A：首次改密后重新登录 Implementation Plan

> 当前交付：固定候选09017d7a已通过D审查84b6d2de并经[代码PR](https://github.com/GTX537/CP6/pull/255)合入远端main；源码/相关验证直接复用。A本地代码窗口已交付，无新增修正；#38真实DEV仍NOT_RUN，后续环境窗口另行确认。

> 使用 executing-plans 连续实施；项目精简规则优先。原对话 A 是 BUG-38 唯一实施者，D负责最终一次集中审查，无需另派审查代理。

**Goal：** 改密后浏览器进入干净未登录态，持旧强制改密 Cookie 的合法重登和2FA挑战可恢复，业务与安全守卫保持有效。

**Architecture：** 复用 AuthCookieWriter 和现有认证流程，在改密原子保存后以及合法凭据进入2FA pending前清旧Cookie。只给真实重新登录和已确认的公开语言读取建立窄例外；不扩大整个 auth/lang 前缀或所有 AllowAnonymous。使用已合入的真实 GUID/tenant 权限读取，保留其 token_use 拒绝。

**Tech Stack：** .NET8、ASP.NET Core、EF/PostgreSQL、现有 Vue/Playwright；不新增依赖或数据库实现。

## 输入与独占写集

先读 [Issue #38](https://github.com/GTX537/CP6/issues/38)、[权限最终交付](../../evidence/WP02-03/current-permission-consumption/DELIVERY.md)、AuthController、MustChangePasswordMiddleware、AuthCookieWriter、AuthSessionService、Tenant/CSRF 中间件及真实 Program 顺序。原只读准备 `C:/Users/tt/CP6-planning-reports/20261017-foundation/bug38-scope.md` 可复用；其基线6c372085为历史，实际从本轮准入后的最新main开始。

分支 `codex/bug-38-password-relogin-20261017`；工作树 `D:/CP6-worktrees/bug-38-password-relogin-20261017`。准确白名单见 dispatch.json，产品只改 AuthController 与 MustChangePasswordMiddleware；另允许相关现有认证测试、新 PasswordChangeReloginHttpTests、独立 Playwright 测试/配置和单份 `docs/quality/evidence/issue-38/DELIVERY.md`。

Program、Cookie writer、AuthSessionService、CSRF、EF模型/迁移、项目/锁、现有权限上下文、前端router/store/i18n与公共Playwright配置均只读。需要调整名单先向COORD说明具体原因；不得为了fixture放宽生产主体/租户校验。

## 实施步骤

- [ ] 核对远端main、分支及准入；无本任务代码改动时可ff-only到最新main。发布一次真实START。使用独立构建输出及既有私有NuGet缓存；不要顺手更新包或全量构建。
- [ ] 先补真实HTTP Cookie往返失败回归，再改产品：临时密码登录→带有效CSRF改密→浏览器最终Cookie jar→新密码重登。普通用户与真实管理员均覆盖；原Cookie重新注入后另账号/租户重登也要验证。至少一条链使用真实 JwtHelper、Cookie writer、CurrentPermissionContext/PermissionAggregator和Tenant中间件，不能只有Name或fake权限服务。
- [ ] 验证保存失败/错误旧密码/策略拒绝不提前清会话；对真正成功的SaveChanges，在既有成功审计之前插入 `_cookies.ClearAuthCookies(Response);`。保留原密码历史与refresh吊销的一次原子保存，不拆事务。
- [ ] 真实Login完成密码、锁定、启用、tenant和SSO策略检查并生成pending后，在 `_cookies.WritePendingCookies(...)` 之前插入 `_cookies.ClearAuthCookies(Response);`。以最终Cookie jar证明旧access/refresh消失、新pending/CSRF保留；不是检查响应有没有字符串cp6_at。不要在未验证凭据时无条件清会话。
- [ ] 在强制改密中间件增加精确POST登录例外，核对框架接受的大小写/尾斜杠，拒绝login-extra与login/extra等后代路径。可采用 `HttpMethods.IsPost(ctx.Request.Method)` 配合准确路径（`/api/auth/login`与框架接受的尾斜杠）或已匹配AuthController.Login的endpoint元数据；不得用现有可匹配后代的PathMatches直接扩大登录路径。保留refresh/profile/业务及self-2FA限制。
- [ ] 本窗口包括Issue所述旧Cookie登录页语言恢复：仅放行实际LangController上明确AllowAnonymous的GET action；核对matched endpoint的ControllerActionDescriptor与IAllowAnonymous元数据。管理列表、publish/review和修改路径必须仍拒绝，不放开所有匿名端点。若真实路由时序不支持该办法，向COORD报具体依据，不退化为前缀放行。
- [ ] 保留CSRF：cookie模式change-password/2FA写操作缺失或错误header仍拒绝。未完成强制改密的用户重新登录后新JWT仍受限制；合法2FA成功后才能获得完整会话，pending错误/过期/重放仍拒绝。复用已有相关验证，只补当前改动缺口。
- [ ] 运行受影响认证测试及隔离真实应用浏览器原步骤，保存真实RED/GREEN和失败记录；检查完整diff，交固定候选与单份报告给COORD/D，不自行合main或关闭Issue。

相关测试入口（按实际变更选择，不以数量作为目标）：

```powershell
& 'C:/Users/tt/.dotnet/dotnet.exe' test CP6.Tests/CP6.Tests.csproj --no-restore --filter 'FullyQualifiedName~AuthControllerChangePasswordTests|FullyQualifiedName~AuthControllerLoginSecurityTests|FullyQualifiedName~AuthController2faTests|FullyQualifiedName~SecurityMiddlewareTests|FullyQualifiedName~PasswordChangeReloginHttpTests' -v quiet
```

首次新增回归须在未改产品时呈现Cookie保留/重登E-SEC-009等真实失败，最终相关行为通过；不要用编译错误代替RED。已有2FA测试从每次响应新建字典会遗漏保留旧Cookie，关键回归使用真实CookieContainer/浏览器或等价保留旧值的完整jar。

## 隔离环境与完成边界

唯一资源：PG55448/API18238/Web15238；私有根 `D:/CP6-local-test/BUG-38/<owner>`，启动前核对空闲，不停止他人进程。PostgreSQL新实例/新库，临时账号/密钥不进仓或输出；不用现有cp6、cp6-dev、demo或真实用户。完整应用按已有一次性DatabaseInit→Api规则初始化自有库，后台服务可关闭并如实记录。显式启用 `Security__Csrf__Enabled=true`，loopback HTTP仅本实例允许Cookie Secure=false。优先核对CrmOidc禁用时的原Cookie路径，不能被其refresh-family提前拒绝掩盖；启用配置的额外Cookie语义按受影响范围说明。

独立Playwright配置只使用该API/Web、单worker/新context，不复用默认5173、共享storageState或他人服务。浏览器原步骤含管理员通过真实用户入口创建需改密账号、普通/管理员首次改密重登、旧Cookie重注入恢复、语言加载、账号切换及2FA转换；最小必要截图和状态，不保存密码或完整令牌。保留所有失败，核对PID/data_directory/ownership后只停止自有资源。

本地代码可独立交付；报告必须把LOCAL_ISOLATED与真实DEV区分。Issue明确要求真实DEV普通/管理员浏览器冒烟，目前没有部署/切Tunnel/覆盖既有库授权；没有合法目标和已部署修复时保持DEV NOT_RUN，用Refs #38，不写Closes。D核对DEV缺口，COORD处理后续发布授权。完整WP02-03、通用JWT用途隔离、已导出access全局吊销及业务验收不因此完成。

## 执行与通信

读取根 AGENTS、[本轮入口](README.md)、[资源登记](dispatch.json)及唯一台账 `docs/development/roadmap.json`。本轮使用原四个用户对话，不另起代理实施同一任务。原 wave13 分支、工作树和报告保留；不能把本轮审查视为原候选已准入合并。

自己的新 Handoff 分支和工作树由 COORD 在本轮准入合并后建立，名称见 dispatch。先读取协调分支 `codex/dev-coordination-20261012` 中 `handoffs/CP6-DEV-20261012/waves/20261017-wave14.md` 的固定提交，核对实际 main。自己的回复只写 `handoffs/CP6-DEV-20261012/tasks/<本窗口ID>/`，不改 README/CURRENT/协调消息或他人文件。START 写入 `messages/<UTC>-start/MESSAGE.md`，列明原聊天身份、窗口、固定输入、允许范围和实际开始时间；安全 push、远端回读后即可连续工作，不等运输 ACK。A 的候选交付发 `messages/<UTC>-delivery/MESSAGE.md`，审查者只维护本窗口一份 REVIEW.md，最终消息引用其固定提交；不要复制原测试日志和哈希清单。

Git 一律带 `-c core.longpaths=true`。push/PR 前核对候选和受保护分支 workflow/依赖/事件及路径；相同内容复用已有分析，无新授权不得触发/重跑 Actions。Handoff 私有、Actions disabled、默认及自己的分支无 workflow 后才推送分支，不推 tag。普通文档不做产品测试。不要改全局配置、force-push、删除远端分支或使用管理员绕过。

PostgreSQL 是唯一新增数据库目标。项目精简规则优先：相关行为验证与一次合并的 SPEC/QUALITY 审查；修正只复查受影响部分，代码/依赖/环境未变就引用有效证据。没有因为固定 SHA、提交或文档变化重跑全仓的要求。数据库、并发、身份或安全风险保留必要专项检查。

执行者不写共享台账/生成页/四状态、不合 main、不关闭 Issue。COORD 统一协调范围、共享状态、正常 PR 合并、远端核对及满足条件后的 Issue 关闭。读到新的实质阻断立即通过 Handoff 报 COORD；禁止自行扩大公共写集或另开实施者。没有生产/DEV部署、既有数据库迁移/覆盖、正式发包或新 Actions 授权。Handoff 发布不自动唤醒聊天，也不等于真实 START 或验收。
