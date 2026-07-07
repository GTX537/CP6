# Task 9 报告：端到端联调验证（真库迁移 + HTTP 冒烟）

分支 `feat/space-wave1-publish-loop`；无产品代码变更（验证任务，未发现需修复的缺陷）。

## 环境与目标库

- Docker 栈起始为 **Stopped**（WSL Ubuntu 未运行）→ 启动后 7 容器全部 Up（cp6-db/api/web/mq/redis/kafka/cloudflared）。
- **目标库 = `CP6DB`**（开发主库）。核实依据：`appsettings.Local.json`（Program.cs:20 在 env 之后加载、优先级最高）`DefaultConnection = Server=127.0.0.1,1433;Database=CP6DB;User Id=sa;…`，与 `appsettings.Docker.json`（`Server=cp6-db;Database=CP6DB`）一致。迁移与后端均打到 CP6DB。
- **网络避坑**：本机 docker 跑在 WSL Ubuntu 内，其发布端口只在 WSL 的 `127.0.0.1` 可达、在 WSL eth0 IP 上被拒；WSL2 localhostForwarding 对 docker DNAT 端口不生效 → Windows 主机直连 `127.0.0.1:1433` 被拒。解决：WSL 内起一个 python 原生转发器（`0.0.0.0:21433/25672/39092 → 127.0.0.1:1433/5672/29092`，原生监听可被 Windows 经 eth0 访问）+ Windows `netsh portproxy 127.0.0.1:{1433,5672,29092} → WSL-eth0:{2..}`。验证结束已全部拆除。

## Step 1：应用迁移

`dotnet ef database update --project CP6.Core --startup-project CP6.WebApi`（host, EF 8 / ProductVersion 8.0.12）

输出关键行：
```
CREATE TABLE [T_WmsBin] (... PK_T_WmsBin PRIMARY KEY ([Id]))
CREATE UNIQUE INDEX [IX_T_WmsBin_TenantId_WarehouseCd_LocationCode] ON [T_WmsBin] (...)
INSERT INTO [__EFMigrationsHistory] VALUES (N'20260705172214_SpaceWave1WmsBin', N'8.0.12');
Done.
```
核验：`sys.tables` 含 `T_WmsBin`；`__EFMigrationsHistory` 含 `20260705172214_SpaceWave1WmsBin`。

## Step 2：7 步 HTTP 冒烟（结果 7/7 PASS）

后端：host `CP6.WebApi`（net8.0，Development 环境，`http://127.0.0.1:5100`；Development 关 CSRF、cookie 非 secure）。登录 `POST /api/auth/login {admin/123456}` → 200，JWT 走 `cp6_at` httpOnly cookie（-WebSession 自动携带）。

冒烟数据经 API 现建（TenantId 由中间件自动盖章 = admin 租户 `…A1`），最终干净单次运行 **site=E2E4875**（SiteCode 无 WarehouseCd 映射 → 发布/停用两路 WarehouseCd 均 = SiteCode `E2E4875`）：
- site E2E4875 / floor / zoneA(ZA4875) / zoneB(ZB4875) / rackA(cols3) / rackB(cols2)
- 场景保存建 3 草稿 locA1/locA2/locB1（Task 8 护栏强制 Status=0）
- 编码规则 ScopeType=0 + IsDefault=true，Segments=[zone-code, col]
- generate-codes fill-empty → `ZA4875-1 / ZA4875-2 / ZB4875-1`（DB 核验 Status=0）

| # | 请求 | 响应/HTTP | 库证据 | 结论 |
|---|------|-----------|--------|------|
| 1 | POST /floor/{f}/publish `{}` | 200 `{published:3}` | T_WmsBin 3 行：`ZA4875-1/2, ZB4875-1`，WarehouseCd=`E2E4875`，Version=1，IsActive=1，LastPublishedBy=`admin` | PASS |
| 2 | 查 T_WmsBin (WarehouseCd=E2E4875) | — | 3 行，字段同上（映射值=SiteCode，publishedBy 溯源落 `LastPublishedBy`） | PASS |
| 3 | 再次 POST publish `{}`（幂等，变体B） | 200 `{published:0}` | WmsBin 仍 3 行、无新增；SPACE 事件无重复批次行 | PASS |
| 4 | PUT /location/{locA1}/deactivate（无库存） | 200 | Space_Location `Status=2,Version=2`；T_WmsBin(locA1) `IsActive=0,Version=2` | PASS |
| 5 | 插 T_Stock(E2E4875, ZA4875-2, qty=5) → PUT deactivate locA2 | 400 `E-SPACE-401 库位仍有库存，无法停用` | Space_Location locA2 `Status=1,Version=1`（未前进）；冒烟后删除该 T_Stock 行 | PASS |
| 6 | POST publish `{zoneId: zoneB}`（新增 locA3@zoneA + locB2@zoneB 两草稿后） | 200 `{published:1}` | locB2(zoneB) `Status=1` 且落 T_WmsBin(IsActive=1)；locA3(zoneA) `Status=0` 且**未**落 T_WmsBin（H5 库区收窄） | PASS |
| 7 | POST /floor/{f}/scene 载荷把已发布 locB1 置 `status:0` | 200 | Space_Location locB1 `Status=1`（护栏拒绝 DTO 覆盖，H1） | PASS |

**Step 5 口径说明**：brief 预期 `W-SPACE-404`，实测为 **`E-SPACE-401`**。这是符合设计的：`DeactivateAsync` 先走 Space 侧库存前置校验（`WmsStockQuery.GetStockQtyAsync` 读 T_Stock，qty>0 即抛 E-401），根本到不了后面的同步 RPC（W-404）——两者读同一张 T_Stock，前置闸门更早命中。DoD 本质「有库存停用被拒 + Status 保持 1」完全满足，非缺陷。

**过程小插曲（非产品缺陷）**：首轮脚本 step5 因 sqlcmd 未加 `SET NOCOUNT ON` 导致 `$codeA2` 混入「(1 rows affected)」尾串、插了脏 LocationCd 的 T_Stock，误使 deactivate 放行。修正脚本（Sql 统一加 NOCOUNT）+ 隔离复现后确认 step5 正确拒绝。产品逻辑始终正确。

## 事件表状态（T_IntegrationEvent, SourceModule='SPACE'）

累计 7 行（3 轮冒烟合计；干净单次运行 = 0005/0006/0007）：
```
SourceNo             HookName                 Status   Attempts  Op(Items)
LPUB-…-0007          OnLocationPublishedAsync SUCCESS  1         UPSERT×1     (step6 库区发布)
LPUB-…-0006          OnLocationPublishedAsync SKIPPED  1         DEACTIVATE×1 (step4 停用异步兜底)
LPUB-…-0005          OnLocationPublishedAsync SUCCESS  1         UPSERT×3     (step1 整层发布)
```
- 全部终态 **SUCCESS / SKIPPED**，**零 Failed / 零 DeadLetter**，Attempts 恒 =1（首发基线，非重试增长）。
- 每次 publish 唯一 BatchNo（LPUB-yyyyMMdd-NNNN），**无重复批次行**。
- SKIPPED 属设计内幂等：停用同步 RPC（`WmsBinDeactivator`）已权威落定 bin（IsActive=false + Version+1），随后的异步兜底事件经 `WmsBinConsumer` 命中「version<=lastVersion」→ SKIPPED（幂等收敛，§6.1④），非失败。

## 清理确认

- T_Stock 测试行：全删（`WarehouseCd LIKE 'E2E%' OR ProductCd IN ('E2E-P1','DBG')` → COUNT=0）。
- Space 主数据 / T_WmsBin 行：按 brief 保留（开发库无妨）。
- 后端进程：已停止（`127.0.0.1:5100` 不再监听）。
- 网络脚手架：netsh portproxy 三条已删；WSL python 转发器已 kill、`/tmp/fwd.py` 删除。
- Docker 7 容器：保持运行（起始即应在运行状态）。
- 仓库：无代码改动、无新增 commit（`git status` 仅原有未跟踪的 picture/shots 文件）。

## 全量测试基线

`dotnet test CP6.Tests/CP6.Tests.csproj`
```
Passed! - Failed: 0, Passed: 1528, Skipped: 5, Total: 1533, Duration: 49 s (net8.0)
```
= 既有基线，符合预期（1528 passed / 5 skipped）。

## 发现的问题

1. **无产品缺陷**。波1 发布闭环 7 步全部按设计工作，T_WmsBin 真落库、幂等、库区收窄、状态机护栏、库存停用闸门均实证通过。
2. **Step 5 错误码与 brief 描述差异**（E-SPACE-401 vs W-SPACE-404）：设计预期内的更早闸门命中，已在上文说明；如需 brief 与实现口径一致，可在后续文档订正 brief 表述。
3. **环境注意**（非本波问题，供后续 executor）：本机 docker-in-WSL，Windows 主机无法直连 docker 发布端口；需 WSL 内原生转发器 + netsh portproxy 才能让 host 的 `dotnet ef` / host 后端连到 `127.0.0.1:1433`。或改为在容器/WSL 内运行工具链。
