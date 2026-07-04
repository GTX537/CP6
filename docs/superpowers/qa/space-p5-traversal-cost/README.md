# Space P5 — Traversal-Cost QA (真库 + 真浏览器)

End-to-end QA for the Space P5 feature: multi-floor pick-path routing switched from
pure distance to **time** weights (per-connector `WaitSec` + `TravelSecPerFloor`), with a
**dual distance + time** panel and a cost-aware what-if optimizer.

All 5 acceptance points below were run against a **real database** and a **real headless
browser** (not mocks). Status: **5/5 PASS**.

---

## Environment (THIS machine — differs from the original brief)

| Piece | Value |
|-------|-------|
| Repo / branch | `C:\CP6` @ `feat/space-p5-frontend-cost` (T1–T10 done, green) |
| SQL Server | **Docker** container `cp6-db` inside WSL2 Ubuntu, published to Windows host. sa password in `C:\CP6\.env` (`MSSQL_SA_PASSWORD`). |
| QA database | `CP6DB_SpaceQA` — **bootstrapped fresh** on this machine (the old machine's QA db was not migrated). Backend auto-migrates on first start. |
| Backend | `dotnet run` on `http://localhost:5177` (`ASPNETCORE_ENVIRONMENT=Development`). |
| Frontend | Vite on `http://localhost:5180`; its `/api` proxy already defaults to `:5177` (no env var needed). |
| Login | `admin` / `123456` (auto-seeded on the fresh db by `Program.cs`; tenant `…A1` matches the seed data). |

### 坑 (gotchas hit on this machine — read before repeating)

1. **`.NET SqlClient` + WSL2 + `localhost` = connection refused (10061).**
   The connection string **must use `127.0.0.1,1433`, not `localhost,1433`.**
   `localhost` resolves to IPv6 `::1` first, and WSL2's localhost-forwarding of the
   Docker-published port is unreliable for SqlClient's SNI over `::1` (raw TCP and
   `Test-NetConnection` succeed, but `SqlConnection.Open()` refuses). Forcing IPv4 with
   `127.0.0.1` fixes it. This is baked into `appsettings.Local.json`.

2. **gstack headless browser cannot run on this Windows host.**
   gstack's Playwright server runs under **bun**, and `chromium.launch()` **hangs
   indefinitely under bun on Windows** (reproduced with a 1-line launch test → timeout;
   no `chrome-headless-shell` process ever spawns). Node.js drives the exact same
   Playwright + browsers fine. **Workaround:** the browser acceptance steps were run via a
   small **Node + Playwright** script (`qarun.mjs`, not committed — lives in the gstack
   skill dir) using the browsers gstack already installed. Same engine, same evidence.
   If gstack is fixed for bun-on-Windows later, the standard `browse` CLI can replace it.

3. **Software WebGL works.** Launching chromium with
   `--use-gl=angle --use-angle=swiftshader --enable-unsafe-swiftshader` gives real WebGL,
   so the Three.js viewers actually rendered the 3D scene and the pick path (see
   `01-stacked-dual.png`). The SP4-era "synthetic wheel can't reach near-LOD" limitation
   did **not** bite here — the path lines render at the default camera. Path *animation
   frame-by-frame* was not stepped; correctness of the cost math is additionally covered by
   T2/T4/T5/T6 unit tests and the contract test below.

---

## Seed chain (run in this exact order against `CP6DB_SpaceQA`)

The QAWH site chain was originally hand-built in the editor UI (no SQL existed), so this QA
adds a **bootstrap** section that materializes it before the older seeds can resolve their
hard-coded GUIDs.

| # | File | Creates |
|---|------|---------|
| 0 | `space-p5-traversal-cost/seed.sql` **(bootstrap section)** | Site `QAWH` `F31F48C2…`, Floor `F1` `5C92E6A8…` (L1), Zone `A` `4D426CAE…`, Rack `R1` `0A00…0001` |
| 1 | `space-p3-sp3/seed.sql` | F1 cross-grid aisles + 4 `SP3-*` locations + single-floor zig-zag order **`OB-SP3-CROSS`** |
| 2 | `space-p4-multifloor/seed.sql` | Floor `F2` (L2) + zone/rack/locations + elevator **`E1`** (F1↔F2) + cross-floor order **`OB-P4-CROSS`** |
| 3 | `space-p5-traversal-cost/seed.sql` **(E1-cost section)** | `UPDATE Space_Connector SET WaitSec=20, TravelSecPerFloor=6 WHERE ConnectorCode='E1'` |

Because the sp5 file holds both the bootstrap **and** the E1 cost update, and the E1 update
needs sp4's connector to exist first, the file is run **twice**: once first (bootstrap;
E1 update hits 0 rows), and once last (E1 now exists → cost applied). The file is fully
idempotent (`IF NOT EXISTS`), ASCII-only, `SET QUOTED_IDENTIFIER ON`.

Run each file via (password from `.env`):
```
wsl -d Ubuntu -u root -- sh -c \
  "docker exec -i cp6-db /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '<PW>' -C -d CP6DB_SpaceQA \
     < /mnt/c/CP6/docs/superpowers/qa/space-p5-traversal-cost/seed.sql"
```

---

## Acceptance results (5/5 PASS)

### 1. Contract — `GET /api/space/site/{QAWH}/pick-path?taskNo=OB-P4-CROSS` → 200 ✅
`data.connectors[0]` = E1 with `waitSec: 20, travelSecPerFloor: 6`; every floor carries
`level` + `z` (F2 → level 2 / z 6000, F1 → level 1 / z 0). Verified with a cookie-jar
`curl` login (admin/123456). Response envelope `{code:0, message:"OK", data:{…}}`.

### 2. Stacked viewer dual-value panel — `01-stacked-dual.png` ✅
`/space/stacked/{QAWH}` → load `OB-P4-CROSS` → panel shows:
> **实际 33.2 米 / 91 秒 ・ 优化 17.2 米 / 35 秒 ・ 省 61%**

Both distance (米) and time (秒) shown; savings% is on the **time** basis. The cross-floor
path renders in the 3D scene (cyan lines through the elevator, pink stop marker).

### 3. Cost effect — `02-cost-effect.png` ✅
Raise E1 cost (`WaitSec=120, TravelSecPerFloor=60`) → reload → panel becomes:
> **实际 33.2 米 / 553 秒 ・ 优化 17.2 米 / 189 秒 ・ 省 66%**

Distances are unchanged (33.2 / 17.2 m) — only the **time** weights move (91→553s actual,
35→189s optimized) and the savings shifts 61%→**66%**, proving connector cost feeds the
optimizer. Restored to 20/6 afterward.

### 4. Single-floor time line — `03-floor-time.png` ✅
`/space/viewer/{QAWH}?floorId={F1}` → load single-floor zig-zag order `OB-SP3-CROSS`:
> path info: **拣货路径：4 点，总距 15.3 米**
> compare:  **实际 15.3 米 / 13 秒 ・ 优化 9.3 米 / 8 秒 ・ 省 39%**

Time row present, path renders; matches the sp3 seed's documented ~15.3m→9.3m / ~39%.

### 5. Editor ConnectorPanel — `04-editor-connector.png` ✅
`/space/editor/{F1}`:
- **New-connector type prefill:** selecting 类型 = 楼梯 (stairs) changed the cost fields from
  `等待秒 20 / 每层秒 6` (elevator default) to `等待秒 0 / 每层秒 15` (stairs default) — matches
  `TYPE_DEFAULT_COST`.
- **Edit list item + save:** changed E1's 等待秒 and clicked **保存成本** → green success
  toast **成本已保存**. (E1 restored to 20/6 after the test.)

---

## Screenshots
| File | Acceptance |
|------|-----------|
| `01-stacked-dual.png` | #2 stacked dual distance+time panel |
| `02-cost-effect.png` | #3 expensive-E1 → shifted time / savings |
| `03-floor-time.png` | #4 single-floor time line |
| `04-editor-connector.png` | #5 type prefill + 保存成本 toast |

---

## Post-QA state
- `E1` cost left at the canonical **20 / 6**.
- `appsettings.Local.json` restored to `Database=CP6DB` (correct local-dev config for this
  machine; still uses `127.0.0.1,1433` per 坑 #1). Only its DB name and server host differ
  from the committed template — the file is gitignored.
- Background `dotnet` (5177) and `vite` (5180) processes stopped.
