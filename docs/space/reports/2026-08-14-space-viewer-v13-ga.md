# Space Studio V1 Viewer GA 性能复验

- 状态：**PASS**
- 证据级别：`FORMAL_GA`
- 产品代码提交：`bd206ff800b12254d3a994fa7c06ab9280523924`
- 数据集：`E08-S05-STANDARD`，500 个货架、10,000 个库位
- 执行时间：2026-08-14 04:22:42Z–04:23:39Z

## 1. 结论

当前 Space Viewer 在冻结验收环境中通过 V1 性能门槛：30 次独立冷浏览器 Context、3,000 次真实命中拾取全部成功，失败率为 0；未出现浏览器控制台错误、WebGL 降级或 GPU 切换。该结果关闭“当前仓库 SHA 的 Iris Xe/WebGL2 500 货架/10,000 库位性能证据”任务，但不替代 Published-only 生产 Viewer 边界核验、独立 UX/辅助技术验收、双仓 Pilot 或 GA 签字。

## 2. 验收环境

| 项目 | 实测环境 |
|---|---|
| 操作系统 | Windows 11 Pro，10.0.26200 |
| CPU / 内存 | Intel Core i7-12700H，20 逻辑核，15.7 GiB |
| 验收 GPU | Intel Iris Xe Graphics，驱动 31.0.101.4502 |
| WebGL | WebGL 2.0，ANGLE Direct3D11；无 SwiftShader/Software |
| 浏览器 | Chrome 151.0.7922.77，扩展关闭，全新 Context |
| Node | v20.19.0 |
| 视口 | 1920×1080，devicePixelRatio 1 |

机器同时安装 NVIDIA RTX 3060 Laptop GPU，但 30 次正式运行及预热均由 `WEBGL_debug_renderer_info` 验证为 Intel Iris Xe；证据聚合器要求渲染器在全部运行中一致。

## 3. 方法

1. 在跟踪文件和索引均无改动的提交 `bd206ff8` 上启动本地 Vite 页面。
2. 先执行 1 次单独记录、不进入稳定分布的预热。
3. 每次正式运行创建全新浏览器 Context，加载确定性标准仓，完成首帧、标签、100 次实际命中拾取、30 次 10,000 库位着色及 180 帧轨道渲染。
4. 30 次运行汇总 P50、P95、最大值和失败率；帧门槛直接使用 4,500 个稳定帧间隔的 P95，不以平均 FPS 代替。
5. 任一软件渲染、非 WebGL2、控制台错误、拾取 miss、数据规模漂移、运行失败、渲染器切换、样本不足或脏跟踪工作区均失败关闭。

预热结果为 PASS：可交互 60.6ms、帧时间 P95 8.2ms、拾取 P95 0.3ms、着色 P95 1.5ms。

## 4. 正式结果

| 指标 | 样本数 | P50 | P95 | 最大值 | 冻结门槛 | 结果 |
|---|---:|---:|---:|---:|---:|---|
| 首次可交互 | 30 | 57.9ms | 62.3ms | 65.1ms | P95 ≤3,000ms | PASS |
| 帧时间 | 4,500 | 7.5ms | 8.2ms | 34.9ms | P95 ≤20ms | PASS |
| 标签更新 | 600 | 2.3ms | 4.5ms | 7.0ms | P95 ≤16ms | PASS |
| 单对象拾取 | 3,000 | 0.1ms | 0.3ms | 4.0ms | P95 ≤150ms | PASS |
| 10,000 库位批量着色并提交渲染 | 900 | 1.2ms | 2.0ms | 5.1ms | P95 ≤3,000ms | PASS |
| WebGL draw calls | 30 | 36 | 36 | 36 | 最大值 ≤100 | PASS |
| 同屏标签 | 30 | 34 | 34 | 34 | 最大值 ≤200 | PASS |

完整性结果：30/30 冷启动成功，3,000/3,000 次拾取命中，0 console errors，0 软件渲染，0 失败运行。

## 5. 与 2026-08-01 基线对比

旧报告只保存单次硬件结果；本次升级为 30 次冷启动和原始样本，因此百分比仅用于回归观察，不解释为产品优化收益。

| 指标 | 旧单次报告 | 当前正式 P95 | 方向变化 |
|---|---:|---:|---:|
| 可交互 | 275ms | 62.3ms | -77.3% |
| 帧时间（旧 83.3fps 折算） | 12.0ms | 8.2ms | -31.7% |
| 标签更新 | 3.5ms | 4.5ms | +28.6%，仍为门槛的 28.1% |
| 拾取 | 0.4ms | 0.3ms | -25.0% |
| 批量着色 | 3.0ms | 2.0ms | -33.3% |
| draw calls | 36 | 36 | 0% |

## 6. 证据与复现

- [原始 JSON](./2026-08-14-space-viewer-v13-ga.json)，SHA-256 `73CB9A9731BD515D3F4679E1E68BA0933DD75F9132C3481A78FA7077704AF300`
- [硬件截图](./2026-08-14-space-viewer-v13-ga.png)，SHA-256 `4B62FA4DA1A84706B6396C564F1D67F90A59A681D3686BC2E631B87776B0A546`

```powershell
npm run dev -- --host 127.0.0.1 --port 4175 --strictPort
$env:SPACE_PERFORMANCE_RUNS='30'
$env:SPACE_PERFORMANCE_REQUIRED_GPU='Iris.*Xe'
$env:SPACE_PERFORMANCE_SCREENSHOT='../docs/space/reports/2026-08-14-space-viewer-v13-ga.png'
$env:SPACE_PERFORMANCE_EVIDENCE='../docs/space/reports/2026-08-14-space-viewer-v13-ga.json'
npm run benchmark:space-browser
```

性能场景、预算、浏览器执行器和证据聚合器的输入文件 SHA 均写入原始 JSON。若上述输入、Three.js/浏览器主版本、数据集或生产 Viewer 渲染路径发生影响性能的变更，必须重新执行正式门禁，不能沿用本报告。
