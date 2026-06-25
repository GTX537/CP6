# 2FA (双因素认证) — T10 真浏览器 + HTTP-layer QA 证据

实施计划 `docs/superpowers/plans/2026-06-22-2fa.md`（T1~T10）。本目录是 T10 的可复现 QA 环境与证据。
QA 日期 2026-06-25。后端 `http://localhost:5177`（Development，`Security:Csrf:Enabled=false` 设计如此），
前端 `http://localhost:5173`，库 `CP6DB`（SQL Server `localhost\KOUSQLSERVER`）。

## 复现步骤
1. 起后端：`dotnet run --project CP6.WebApi --launch-profile http`
2. i18n 快照重建（验证 T8 词条）：起后端后 `cd cp6.web && npm run i18n:pull && npm run i18n:gen-types && npm run i18n:check`
   → 4337 keys，`✅ 无缺失 key`（较 SSO 基线 4302 +35 = T8 新增 9 E-SEC + 6 事件 + 20 画面词条）。
3. 种子：`sqlcmd -S localhost\KOUSQLSERVER -d CP6DB -E -C -i seed.sql`（admin 补邮箱 + 建 `tfaforce`）。
4. HTTP e2e：`BLOG=<后端日志路径> bash qa2fa.sh`（依赖 `totp.mjs` 算 TOTP；邮件 OTP 从后端 DEV-EMAIL 日志取）。
5. 浏览器：见下方截图流程。

## HTTP-layer e2e（qa2fa.sh，全部通过）
> 注：后端 `BizException` 返回的是**本地化译文**（非裸错误码），下列断言以译文/HTTP 状态/Cookie 核验。

- **FLOW1 可选自助绑+挑战**：admin（DEFAULT mode0）明文登录正常发 `cp6_at`；`setup-self` 返 secret；
  `enroll-self`+TOTP→200；`status` enabled+canDisable(mode0)；登出后重登→`twoFactorRequired`/`mustEnroll=false`、
  仅 `cp6_2fa` pending Cookie**无** `cp6_at`；`verify` TOTP→200 发三 Cookie；**重放同 pending→E-SEC-013**（一次性）。
- **FLOW2 邮件回退**：`email-otp`（verify 态）→200，DEV-EMAIL 日志取 6 位 OTP→`verify method=email`→200 发 `cp6_at`。
- **FLOW3 强制入会**：admin `PUT /api/sys/two-factor-policy {mode:2}`→200；**非法 `{mode:5}`→E-SEC-012**；
  `tfaforce` 登录→`twoFactorRequired`/`mustEnroll`；**入会态 `email-otp`→E-SEC-014**（评审#4 邮件不绕过）；
  `setup`+`enroll`+TOTP→200 发 `cp6_at`。
- **FLOW4/5**：强制租户 `disable-self`→**E-SEC-019**；admin `POST /api/user/reset-2fa`→200。
- **FLOW6/7 边界**：无 pending Cookie 打 2FA 端点→E-SEC-013；`SecurityLog.Reason` **不含** secret/otpauth（0 行）。
  CSRF：dev 关（设计），2FA 端点非豁免（`CsrfMiddleware` 仅精确豁免 `/api/auth/login`），生产 `Enabled:true` 时双提交生效。

## 真浏览器（gstack browse，headless Chromium）
- **挑战屏**（`2fa_challenge.png`）：admin（已绑 2FA）登录→自动路由 `/sys/2fa-challenge`，
  渲染「二要素認証の確認」+ 验证码输入 +「確認」+「メール確認コードを使う」，i18n 全解析无裸 key。
- **入会屏**（`2fa_enroll.png`）：DEFAULT 设 mode2，`tfaforce` 登录→路由 `/sys/2fa-enroll`，
  渲染「二要素認証の設定」+ **QR 二维码 `<img>`** + 手输 secret 备选 +「送信」；
  填**实时 TOTP**（totp.mjs）→提交→完成登录→落 `/dashboard`，DB 确认 `TwoFactorEnabled=1`/Secret/EnrolledAt 已写。

## 清理
QA 后已还原 DEFAULT mode0 + admin/tfaforce 2FA 关（见 seed.sql 末注释），dev 常规登录不受影响。
