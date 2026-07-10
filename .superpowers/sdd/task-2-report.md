# Task P0-T2 Report: JWT 过期配置双写清理

**Status:** DONE
**Commit:** a34a337 chore(platform): 删除 JWT.ExpireMinutes 死配置——令牌时长唯一源=Security.Token.AccessTokenMinutes

(Note: this file previously held a stale report from an unrelated plan — Space wave4 BizException migration. Overwritten with the current P0-T2 report.)

## What Changed

Only `CP6.WebApi/appsettings.json` required changes (1 file, +2/-2):

1. **Deleted** `JWT.ExpireMinutes: 120` (was line 35) — the dead, misleading duplicate. Removed the now-trailing comma on the preceding `"Audience"` key to keep valid JSON. Retained all signing-related JWT keys (`_comment`, `Secret`, `Issuer`, `Audience`).
2. **Added** a `_comment` sibling inside the `Security` section, immediately above `Token`, marking it the single source of truth:
   `"_comment": "令牌时长唯一配置源 = Token.AccessTokenMinutes（旧 JWT.ExpireMinutes 死配置已删，见 AuthController）"`
   (Followed the file's existing `_comment` pseudo-key convention.)

No changes needed in the other variants — none contained `JWT.ExpireMinutes`:
- `appsettings.Development.json` — has `Security.Token.AccessTokenMinutes: 120` (the real dev override; untouched).
- `appsettings.Docker.json` — no JWT.ExpireMinutes, no Token override.
- `appsettings.Local.json` — gitignored; no JWT.ExpireMinutes (only `JWT.Secret`).

## Step 1 Grep Evidence

Command: `grep -rn "ExpireMinutes"` across the repo.
Output — the only match in a config/code file was appsettings.json; the rest are docs:
```
docs\superpowers\specs\2026-06-21-auth-hardening-design.md:55: ...ExpireMinutes)...
docs\superpowers\plans\2026-07-07-p0-platform-hardening.md:46: (this task's own step text)
docs\superpowers\plans\2026-07-07-p0-platform-hardening.md:47: (this task's own step text)
CP6.WebApi\appsettings.json:35:    "ExpireMinutes": 120
```
Zero references in `CP6.WebApi`/`CP6.Core`/`CP6.Tests` C# code — confirms the audit finding (AuthController reads `Security.Token.AccessTokenMinutes`). No fallback edit was needed.

## Test Results

`dotnet test` full suite:
`Passed! - Failed: 0, Passed: 1570, Skipped: 5, Total: 1575, Duration: 44 s`

Matches baseline exactly (1570 green + 5 skipped). No drop.

JSON validity verified independently: `appsettings.json valid JSON`.

## Self-Review Findings / Concerns

- Edit is minimal and config-only; no behavior change (the deleted key was never read).
- JSON remains valid (parsed clean; full test suite loads config at startup and passes).
- The added `_comment` sits as the first key of the `Security` object — JSON key order is irrelevant to .NET config binding, and `_comment` is not a bound config key, so no functional impact.
- No new tests required (pure dead-config deletion), per brief.
- No concerns.
