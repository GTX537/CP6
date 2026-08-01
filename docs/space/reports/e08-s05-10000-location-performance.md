# E08-S05 10,000 库位性能基线交付报告

- 状态：功能分支已完成，待进入 Space 受控集成分支
- 功能分支：`codex/space-e08-s05-performance`
- 起始基线：`5d37865aa5619c5ebe8694460e5638959d6c8e90`
- 基准日期：2026-08-01

## 1. 锁定门槛

本卡把 Viewer 设计规范与 E08-S01/E07-S04 已有契约固化为可执行门禁：

| 指标 | 锁定门槛 |
|---|---:|
| 标准规模 | 10,000 库位、7 库区、20 巷道、500 货架 |
| 最坏分桶压力 | 10,000 库位、50 库区桶 |
| 完整场景 WebGL draw call | ≤ 100 |
| Medium tier P95 帧间隔折算帧率 | ≥ 50fps |
| 首帧可交互 | ≤ 3,000ms |
| 标签计算 P95 | ≤ 16ms |
| 同屏标签对象池 | ≤ 200 |
| 单对象拾取 P95 | ≤ 150ms |
| 10,000 条库存着色 P95 | ≤ 3,000ms |
| 10,000 库位运行态查询 | 每次 ≤ 3,000ms，固定 20×500 分块 |

门槛集中定义在 `cp6.web/src/space-viewer/performance/budgets.ts`。普通前端单测只验证
结构与正确性；`npm run test:space-performance` 才执行 CPU 侧 P95 性能门禁，避免把时间型
基准混入无关测试。硬件 WebGL 门禁由 `npm run benchmark:space-browser` 执行。

## 2. 改前基线与修复

改前 10,000 库位 CPU 基准为：建图 P95 65.48ms、标签 7.08ms、拾取 0.05ms、
10,000 次着色 6.55ms；这些指标均在预算内。唯一失败项是完整场景约 535 个 draw call。

根因是 500 个货架框分别创建 `LineSegments`，即使库位已按 Zone 使用 `InstancedMesh`，
货架仍独占约 500 次提交。修复后：

1. 500 个货架统一进入一个 wireframe `InstancedMesh`，保持货架中心、Z 轴旋转和包络尺寸；
2. 库位 `instanceColor` 在建桶时一次性预分配并填充默认灰色，不再在首次库存刷新时临时创建缓冲；
3. 新增 `setColors`/`setInstanceColors` 批量通道，同一桶的 10,000 条颜色写入只标记一次 GPU 更新；
4. `StockOverlay` 优先走批量通道，同时保留旧 ViewerHandle 的逐条兼容回退；
5. 标准仓与 50 桶压力场均为确定性生成，性能测试不依赖数据库、随机数或外部 WMS。

## 3. 最终测量

### 3.1 CPU/结构门禁

命令：`npm run test:space-performance -- --reporter=verbose`

| 指标 | 改前 | 修复后 | 门槛 | 结果 |
|---|---:|---:|---:|---|
| 完整场景 draw call | 535 | 36 | ≤100 | PASS |
| 建图 P95 | 65.48ms | 54.86ms | ≤3,000ms | PASS |
| 50 桶标签计算 P95 | 7.08ms | 9.16ms | ≤16ms | PASS |
| 拾取 P95 | 0.05ms | 0.03ms | ≤150ms | PASS |
| 10,000 条批量着色 P95 | 6.55ms（逐条） | 5.40ms（批量） | ≤3,000ms | PASS |
| 同屏标签 | 10 | 10 | ≤200 | PASS |

时间结果会受运行时调度影响，验收以“是否跨过锁定门槛”为准，不把单次微小波动解释为性能收益。

### 3.2 硬件 WebGL 门禁

硬件执行器使用本机 Chrome headed 会话，并把窗口移出屏幕；执行器读取
`WEBGL_debug_renderer_info`，发现 SwiftShader/Software 时以“GPU 未验证”退出，不会把软件渲染
结果冒充 Medium tier 结论。

最终设备：Intel Iris Xe Graphics，ANGLE Direct3D11，WebGL 2.0。

| 指标 | 实测 | 门槛 | 结果 |
|---|---:|---:|---|
| 库位 | 10,000 | =10,000 | PASS |
| WebGL draw call | 36 | ≤100 | PASS |
| 首帧可交互 | 275ms | ≤3,000ms | PASS |
| P95 帧间隔折算帧率 | 83.3fps | ≥50fps | PASS |
| 标签计算 P95 | 3.50ms | ≤16ms | PASS |
| 拾取 P95 | 0.40ms | ≤150ms | PASS |
| 10,000 条批量着色 P95 | 3.00ms | ≤3,000ms | PASS |
| 同屏标签 | 35 | ≤200 | PASS |
| 浏览器控制台 error | 0 | =0 | PASS |

验收截图：`docs/space/reports/e08-s05-performance-browser-hardware.png`。

headless gstack Chromium 首次测量回落到 SwiftShader，帧率为 8.57fps。该环境诊断未计入产品结论；
随后硬件执行器明确验证为 Intel/D3D11 后才形成上表 PASS 证据。

### 3.3 运行态批量查询边界

`SpaceWmsRuntimeServiceTests.Exactly_10000_locations_use_twenty_500_item_chunks` 现使用精确
10,000 个 Published/Active 库位，同时验证库存与任务查询各拆为 20 个 500 条批次，且各自
CP6 处理时间不超过 3,000ms；本机整条测试（含数据装载和两次查询）约 2 秒。

既有 `More_than_10000_requested_locations_fail_before_wms` 继续证明 10,001 个不同库位在调用
WMS 前以 400 拒绝。也就是说 10,000 是可执行上限，而不是只写在常量中的理论值。

### 3.4 功能分支门禁

| 门禁 | 结果 |
|---|---|
| Space Unit Release | 220 passed / 0 failed / 0 skipped |
| Space Integration Release | 105 passed / 0 failed / 48 SQL 环境门禁 skipped |
| Design V1 OpenAPI/SDK | 18 passed / 0 failed |
| 前端全量 | 106 files / 607 tests passed |
| 前端类型检查 | passed |
| 前端 production build | passed；仅既有大 chunk 提示 |
| CPU 性能门禁 | 1 passed / 0 failed |
| 硬件 WebGL 门禁 | Intel Iris Xe / D3D11，PASS，0 console errors |
| WebApi Release build | 0 warning / 0 error |
| C# SDK Release build | 0 warning / 0 error |
| TypeScript SDK | strict no-emit compile passed |
| OpenAPI/C#/TypeScript SDK drift | `-Check` exit 0 |

.NET 首次全量编译中出现的 warning 均来自本卡未修改的既有 OA/WMS/测试文件；最终 WebApi 与
C# SDK 增量 Release build 均为 0 warning / 0 error。

## 4. 可重复执行

```text
npm run test:space-performance -- --reporter=verbose
npm run dev -- --host 127.0.0.1 --port 4175 --strictPort
npm run benchmark:space-browser
dotnet test CP6.Space.IntegrationTests/CP6.Space.IntegrationTests.csproj \
  --filter "FullyQualifiedName~Exactly_10000_locations_use_twenty_500_item_chunks"
```

浏览器执行器默认查找本机 Chrome，再回退到 Edge；也可用
`SPACE_PERFORMANCE_BROWSER_PATH`、`SPACE_PERFORMANCE_URL` 和
`SPACE_PERFORMANCE_SCREENSHOT` 显式覆盖。

## 5. 范围与后续

- 本卡锁定单仓 10,000 库位的 P1 基线；多仓或 100,000 级库位仍属于 P2+。
- 服务端时间门禁隔离了 CP6 查询、映射、排序和分块成本；真实 WMS 网络/数据库延迟仍应由部署环境 canary 持续监控。
- 硬件 FPS 是本次 Intel Iris Xe/D3D11 的可重复证据，不代表所有设备恒定 83.3fps；真正的回归判据仍是 ≥50fps。
- 本卡没有修改运行态 API、OpenAPI、SDK 或数据库模型。
