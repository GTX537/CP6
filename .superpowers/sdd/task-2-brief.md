# Task P0-T2: JWT 过期配置双写清理

（提取自 docs/superpowers/plans/2026-07-07-p0-platform-hardening.md）

**Goal（包级）:** 修掉平台级生产隐患：JWT 过期配置双写（`JWT.ExpireMinutes` 与 `Security.Token.AccessTokenMinutes` 两处配置，实际生效后者，前者是误导性死配置）。

## Global Constraints

- 基线不许跌：后端 `dotnet test` 全绿（当前 1570）；每 commit 立即 push。

## Files

- Modify: `CP6.WebApi/appsettings.json:35`、`appsettings.Development.json`/`appsettings.Local.json` 同键
- Test: 无新增（配置删除）

## Steps

- [ ] Step 1: `grep -rn "ExpireMinutes" CP6.WebApi CP6.Core CP6.Tests` 确认除 appsettings 外零代码引用（审计结论：AuthController.cs:76 用 Security.Token.AccessTokenMinutes）。若有引用，改为读 Security.Token 后再删。
- [ ] Step 2: 删除 `JWT.ExpireMinutes` 配置项（保留 JWT 节其余签名相关键）；在 `Security.Token` 节旁加一行注释指明"令牌时长唯一配置源"。
- [ ] Step 3: 全量测试绿 → commit + push。
