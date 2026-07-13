# WFS 波① 终审两 Important 修复报告

分支：`feat/wfs-cleanup-tickets`　日期：2026-07-13　单 commit 覆盖两项，纯注释 + seed 字串，零代码行为变更。

## Finding #1：CSRF 豁免注释安全论据不实

文件：`CP6.WebApi/Middleware/CsrfMiddleware.cs`（`IsExempt` 上方 XML doc 注释 ②段）

实查证据（本次亲验）：
- 4 个 hub `SpaceHub/WmsHub/MesHub/NotifyHub` 均**无** `[Authorize]` 标注 → 匿名可达，旧注释「hub 自身经 JWT/cookie 认证」论据不成立。
- 各 hub 公开方法仅 `OnConnectedAsync/OnDisconnectedAsync` 覆写 + `Subscribe*/Unsubscribe*` 组操作（`Groups.Add/RemoveToGroupAsync`），无状态变更方法。
- `Program.cs:653-659` CORS 显式 allowlist：`policy.WithOrigins(corsOrigins).AllowCredentials()`，无通配源。

改动前（②段）：
```
/// ② SignalR hub 路径（negotiate 是 POST 但不改业务状态，hub 自身经 JWT/cookie 认证；
/// 票11：否则 /hubs/*/negotiate 被 403 拦，实时通知连不上。/hubs 前缀覆盖 notify/mes/wms/space 全部 hub）。</summary>
```

改动后（②段）：
```
/// ② SignalR hub 路径（negotiate 是 POST 但不改业务状态）——豁免安全论据：
///    (a) 现有 4 个 hub(notify/mes/wms/space)均无状态变更方法，仅 Subscribe/Unsubscribe 组操作
///        (Groups.Add/RemoveToGroupAsync)，即便被跨站触发也无业务副作用；
///    (b) CORS 显式 allowlist（Program.cs WithOrigins + AllowCredentials，无通配源）挡跨站 negotiate。
///    ⚠ 前瞻警示：未来若给任一 hub 加可 invoke 的状态变更方法，须重新评估此豁免（届时应移除 /hubs 整段
///      豁免或改为仅豁免 negotiate 端点），否则跨站可经 hub 方法绕过 CSRF。
/// 票11：否则 /hubs/*/negotiate 被 403 拦，实时通知连不上。/hubs 前缀覆盖 notify/mes/wms/space 全部 hub。</summary>
```

代码逻辑（`IsExempt`/`PathMatches`/`Invoke`）一字未动。

## Finding #2：T8 timerActionKind 组 Ko 对齐 T10 风格

文件：`CP6.WebApi/Seed/I18nOaServiceTaskScreenSeed.cs`

T10 已确立的 Ko 用词基准（同文件参照键，本次未动）：
- `svc.action` Ko = "동작"（액션→동작）
- `svc.kind.dataWriteback` Ko = "데이터 쓰기"（기록→쓰기）
- `svc.timerAction` Ko = "실행 시 동작"

timerActionKind 组四键逐键 Ko 前后值：

| LangKey | 改前 Ko | 改后 Ko | 说明 |
|---|---|---|---|
| `svc.timerActionKind`       | 실행 시 액션 유형 | 실행 시 동작 유형 | 액션→동작，与 `svc.timerAction`「실행 시 동작」一致 |
| `svc.timerActionKind.none`  | 없음(대기만)     | 없음(대기만)     | 无 액션/기록 词，已合风格，未动 |
| `svc.timerActionKind.write` | 데이터 기록 액션  | 데이터 쓰기 동작  | 데이터 기록→데이터 쓰기 且 액션→동작 |
| `svc.timerActionKind.api`   | API 호출        | API 호출        | 与 `svc.kind.webApi`「API 호출」一致，未动 |

只动 Ko 两键，其他语言（ZhCN/ZhTW/En/Ja）及 T10 已改的 6 键未触碰。因 SeedLangs 为 insert-only 且本支未部署，改常量即免去将来一张逐租户 UPDATE 票（零成本窗口）。

## 验证

- `dotnet test CP6.Tests/CP6.Tests.csproj` → **Failed 0 / Passed 1843 / Skipped 5 / Total 1848**（与预期基线持平，纯注释+seed 字串无测试影响）。
