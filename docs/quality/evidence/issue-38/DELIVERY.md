# BUG-38 — wave14 A DELIVERY

## COORD集成记录 — 2026-10-17

固定候选 `09017d7ad14c7647601edac258422365ff81daf9` 已经 [D的一次集中SPEC/QUALITY审查](https://github.com/GTX537/CP6.AI-Handoff/blob/84b6d2de0f7b30d493cf068db94fafc20fb7594b/handoffs/CP6-DEV-20261012/tasks/REVIEW-BUG-38/REVIEW.md)通过：本地Ready to merge = Yes，无阻断；交付 `ddc65ece4b922e8c49e9ddc4878a54b7af27f4de` 已回读。[正常代码PR](https://github.com/GTX537/CP6/pull/255)合入远端main `17ce6f742bf9aa885f7a293e13c30fcba124ca59` 后，COORD核对候选包含性及本任务七个文件与候选的精确一致性。候选基线f85cf46c到合并前主线cb7d3274仅有协调文档变化，产品/依赖未漂移，复用下方有效验证，没有重新运行产品测试。共享台账与四状态在本交付窗口同步。

证据解释以D审查为准：相关认证50项包含HTTP13项，最终13为同组受影响重跑，其他37复用；browser-7为最终完整应用PostgreSQL浏览器证据。postgres-final-state.log早于最终browser-7，只作同一实例补充，不能说是最终运行后的查库；database-init.log为空，不能独立当逐项迁移日志。实际可用的PG成功链与A初始化报告保留其范围。HTTP InMemory保存失败、浏览器verify/HTTP enroll不扩为PG事务故障恢复或浏览器enroll验收。

**本地代码已集成；真实DEV NOT_RUN，#38保持Open。** 待确认实际DEV目标、修复后的API/Web身份、合法测试租户/账号与操作范围后完成原步骤冒烟。没有部署、既有数据库操作、Actions或正式发包；未验证模式/业务范围仍按下方记录。以下为A原交付历史，其“待D审查/未合main”描述保留当时状态，由本节接续。

2026-10-17；原对话 A；Refs #38。**代码候选已完成本地验证，交 COORD / D 做一次集中 SPEC/QUALITY 审查；真实 DEV NOT_RUN，Issue 保持 Open。** 本文件是本任务唯一交付报告；固定候选提交由 Handoff DELIVERY 引用，不在报告中制造自引用 SHA。

## 来源和范围

- 基线 `f85cf46cbc0608fe22e88814324596f556e69bfc`（PR250）；开工和交付准备时核对 local main / origin/main 一致。任务分支 `codex/bug-38-password-relogin-20261017`，工作树 `D:/CP6-worktrees/bug-38-password-relogin-20261017`。
- 准入：`docs/development/dispatch/20261017-wave14/A-BUG-38.md` / dispatch.json、roadmap.current_wave；协调输入 `62b45cfc06b4162df129423b59716c17b058b55a`。一次真实 START：Handoff `c534cd3fec1dad30e1ef162fc676fb1c358d6748`，`tasks/BUG-38/messages/20261017T065402Z-start/MESSAGE.md`。
- 已读 D 准备意见 `7c999da8b30f923788e29dde4970f51833ee4623` 与通知 `9cb9ada25a1f62adac4d2637128144aba912dbaf`。此准备不是最终候选审查。
- 仅改两个产品文件、一个既有 2FA 测试、新 HTTP 测试、专属 Playwright spec/config 和本报告。未改共享台账/四状态、Program、权限上下文/聚合器、Cookie writer、CSRF、前端产品、EF/迁移、项目/锁或公共 Playwright 配置。

## 结果

1. ChangePassword 在密码、历史与 refresh 吊销的一次 SaveChanges 成功后、成功审计之前清四个认证 Cookie。密码错误、策略拒绝、保存失败不提前清会话。
2. Login 通过既有凭据/锁定/启用/tenant/SSO检查并生成 pending 后，先清旧完整会话，再写新 pending/CSRF。实际持久 Cookie jar 证明 at/rt 消失而新 pending/CSRF 保留。
3. 强制改密中间件只给 POST `/api/auth/login`（含框架接受的大小写与单尾斜杠）增加重登例外；不放过 GET、login-extra、login/extra。语言例外同时要求 GET、真实 matched LangController action 与 IAllowAnonymous；不放开整个 lang 或全部匿名端点。
4. 保留 profile/refresh/业务/self-2FA及语言管理限制、CSRF 双提交、pending 非法/过期/重放拒绝、尚未改密用户重登后继续受限。既有 impersonation/logout/change-password 例外不变。

## 实际验证

证据私有根：`D:/CP6-local-test/BUG-38/wave14-a/`，ACL 限当前用户和 SYSTEM；原始失败均保留，不提交临时密码、JWT、TOTP secret、数据库或完整日志。下列目录是同机复核入口，不是生产制品。

| 证据 | 实际结果与适用范围 |
| --- | --- |
| `results/red.trx`、`red.log` | 产品修改前真实 HTTP RED：13项中8失败、5通过。两种用户改密后 Cookie 残留；旧 Cookie 重登/2FA/语言均403。不是编译错误冒充 RED。 |
| `results/green.trx`、`green2.log` | 相关五类认证测试50通过、0失败、0跳过；包含新HTTP13项。此前 `green.log` 是新增 mock 缺 Moq 扩展 using 的编译失败，保留，未计通过。 |
| `results/final-http.trx`、`final-http.log` | 加入 pending 非法值/签名有效但已过期的负向断言后，仅重跑受影响HTTP13项，全部通过。未变37项复用上述green结果。 |
| `browser-7.log`、`browser-7/` | 最终专属配置的真实 Chromium / Vite / 完整 API / PostgreSQL 原步骤通过，单worker、新context、无共享storageState；没有模拟API响应。之前 `browser-4` / `browser-5` 成功作为同一链路的迭代记录，最终证据以browser-7为准。 |
| `database-init.log`、`postgres.log`、`postgres-final-state.log` | 新 PostgreSQL 18.6 实例和 `bug38_isolated` 库，一次 DatabaseInit 完成当前Core/Space/Identity/ERP迁移与种子，再用Api模式跳过初始化。数据库记录实查密码历史、MustChangePassword和refresh吊销；没有SQL Server验收。 |

HTTP fixture 使用真实 Kestrel、JwtHelper、AuthCookieWriter、CurrentPermissionContext、PermissionAggregator、AuthSessionService、Tenant/CSRF/强制改密中间件及实际 MVC 路由，持续 CookieContainer 不丢弃未在响应出现的旧值。fixture 的数据库为 InMemory，SSO与文件存储为限定替身；保存失败用拦截器注入，因此该负向结果不冒充 PostgreSQL 事务故障注入。完整浏览器另走真实 PostgreSQL 和原 Program 管线。

浏览器实际执行：

- 种子管理员有真实 RoleId=1及菜单/操作权限；专属夹具将其标为首次改密。浏览器登录→改密→最终jar为空→profile401→新密码登录，确认真实管理员画像。
- 管理员在 `/user` 实际表单创建普通用户；服务端设置 MustChangePassword。普通角色38仅具dashboard菜单，无管理员授权；表单选择该真实角色。普通用户同一context完成临时密码→改密→登录页→新密码，确认画像及角色、mustChangePassword=false。
- 重新注入普通用户原始强制改密Cookie，登录页四种公开语言GET返回200/404，管理GET仍403；页面真实切English触发语言API200并展示英文，再用另一tenant账号重登，核对新用户名和JWT tenant_id。
- 再注入旧Cookie，登录另一tenant已启用TOTP用户；jar只有新pending/CSRF，没有at/rt，profile401；页面输入正确TOTP后取得完整会话、pending清除、画像正确。HTTP另覆盖强制enroll/setup/成功及verify/enroll的错误码、CSRF缺/错、pending非法/过期/重放。
- 最小截图只保留无凭据登录页和恢复后页面。普通角色dashboard部分业务卡片提示无操作权限，是该夹具未授予业务操作的真实结果；未把它算业务验收通过，也未扩大授权消除提示。

浏览器失败记录：browser-1缺锁定版本Chromium（随后安装到私有目录）；browser-2点击Element Plus隐藏input超时（改点可见开关）；browser-3普通用户无角色导致既有空菜单重定向（为自有夹具建立租户角色38/dashboard并通过真实表单选择）；browser-6专属配置保护回调参数错误（改为beforeEach读取TestInfo）。它们均非产品修复失败，不删除记录。最初外部种子脚本表名及重置脚本字段拼写错误亦保留工具/私有日志记录，之后才成功配置自有夹具；没有修改生产 schema。

## 复现与隔离

后端：在任务工作树执行（SDK `C:/Users/tt/.dotnet/dotnet.exe`，locked restore已完成，NuGet复用既有私有缓存）：

```powershell
& C:/Users/tt/.dotnet/dotnet.exe test CP6.Tests/CP6.Tests.csproj --no-restore --artifacts-path D:/CP6-local-test/BUG-38/wave14-a/artifacts --filter 'FullyQualifiedName~AuthControllerChangePasswordTests|FullyQualifiedName~AuthControllerLoginSecurityTests|FullyQualifiedName~AuthController2faTests|FullyQualifiedName~SecurityMiddlewareTests|FullyQualifiedName~PasswordChangeReloginHttpTests' -p:NuGetAudit=false -v quiet
```

全应用使用PG55448/API18238/Web15238，显式CSRF=true、Cookie Secure=false（仅loopback HTTP）、CrmOidc=false，隔离存储目录、内存缓存；关闭hosted services，Kafka留空走既有DB降级，无外部broker。JWT和数据库凭据运行时随机生成。未采用CrmOidc的refresh-family拒绝来遮蔽旧must_change claim问题；CrmOidc enabled模式未额外执行。

私有 `runtime.py` / `seed.py` / `roles.py` / `reset-admin.py` / `browser.ps1` 保留本机复现设置：新库种子admin，另建BUG38_OTHER租户、普通/TOTP账号，真实租户角色及dashboard关联；普通需改密用户始终由浏览器管理员表单创建。重复夹具验证只重置自有admin并生成不同新密码，保留密码历史。新运行者应新建自有实例/库，不对既有环境运行种子脚本。

前端用既有lock执行 `npm ci --ignore-scripts --no-audit --no-fund`，没有锁文件变化。在cp6.web设置仅自有夹具的 `BUG38_ADMIN_PASSWORD`、`BUG38_NEW_PASSWORD`、`BUG38_TEMP_PASSWORD`、`BUG38_OTHER_TENANT`、`BUG38_TOTP_SECRET` 与私有 `PLAYWRIGHT_BROWSERS_PATH` 后运行：

```powershell
node node_modules/@playwright/test/cli.js test --config playwright.password-change.config.ts
```

配置固定loopback15238、单worker，无自动启动共享服务。beforeEach仅允许专属 `bug38-isolated` project；共享配置虽然发现此spec，但会跳过，不能借其共享admin/storageState改密。专属与共享配置均做过 `--list` 注册检查（非共享产品测试）。trace/video/自动截图关闭，避免记录凭据。

## 审查、资源与剩余范围

执行者已检查相对最新确认主线的完整改动、登记白名单、Cookie顺序、精确路径与endpoint元数据、失败边界和证据范围，`git diff --check`通过。最终合并的SPEC/QUALITY审查由D消费固定候选；本报告不替代D结论。

启动前端口空闲；关闭时核对持有PID的命令行属于本任务，核对PostgreSQL `data_directory` 为私有根，停止仅本任务API/Web/PG；数据库和证据保留。不会清理其他对话进程或worktree。

Actions：候选与origin/main工作流/相关依赖无变化；普通工作流仅workflow_dispatch，r2-candidate只匹配 `v*.*.*` tag，本次仅推任务分支。任务分支无排队/运行项，取消0、触发变更0、远程运行0。Handoff私有、Actions disabled、默认和自身分支无workflow后推送；未推tag、未创建PR/合main、未关闭Issue。

**LOCAL_ISOLATED PASS；DEV NOT_RUN。** 没有部署/切Tunnel/覆盖既有库授权。仍需COORD确认真实DEV目标、最终API/Web源码身份、合法测试用户/租户及操作授权后执行Issue规定的普通/管理员浏览器冒烟。此交付不宣称完整WP02-03、通用JWT用途隔离、已导出access全局吊销、业务流程或生产候选验收完成。
