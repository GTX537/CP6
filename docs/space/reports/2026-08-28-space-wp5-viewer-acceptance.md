# Space WP5 Viewer 正式接受

- 状态：**Pass / Accepted**
- DeliveryOwner：`BUBAO.GAO`
- 应用提交：`4b774bb4a724613b44511f204784ea60f3ae22f7`
- 正式执行：2026-08-28 07:26:05Z–07:27:30Z

## 结论

WP5 的三个结果条件均已关闭：生产 Viewer 只消费 `Current Published Design Revision`；冻结性能预算全部通过；1440×900、1280×720、键盘、Chromium 辅助技术树和 4.5:1 对比度检查全部通过。唯一 DeliveryOwner 已按单人交付规则完成可重复自验收，不要求第二人或独立签字。

本项不使用 UI fixture 冒充生产数据，不声称生产 WMS，也没有执行生产部署。Core GA 仍保持 72% / `NoGo`，剩余 WP6、WP8 和最终 DeliveryOwner 签署。

## Published-only 边界

- 生产入口 `FloorViewer.vue` 只调用 `designPublishedSceneApi`，并由 `indexPublishedViewerScene` 拒绝错误 Authority、Draft、内容哈希漂移和运行态混入。
- `SceneBuilder.buildPublished` 对非 Published 或缺少几何权威失败关闭。
- 聚焦 Vitest：3 个文件、12/12 通过，0 failed，0 skipped。
- 浏览器请求级检查确认只请求 `/space/design/v1/sites/{siteId}/published-scene`，未访问可变 Version/Floor Scene 或 Legacy Scene API。

## 性能结果

环境为 Windows 11 Pro、Chrome 151、ANGLE D3D11、NVIDIA RTX 3060 Laptop GPU、硬件 WebGL2。正式运行先预热一次，再创建 30 个独立冷浏览器 Context。全部运行使用同一渲染器，未出现 SwiftShader/Software、控制台错误或拾取 miss。

| 指标 | 实测 | 冻结门槛 | 结果 |
|---|---:|---:|---|
| 冷启动 | 30/30 成功 | ≥30，失败 0 | PASS |
| 首次可交互 P95 | 89.6ms | ≤3,000ms | PASS |
| 帧时间 P95 | 8.2ms | ≤20ms | PASS |
| 标签更新 P95 | 5.4ms | ≤16ms | PASS |
| 拾取 P95 | 0.3ms | ≤150ms | PASS |
| 拾取完整性 | 3,000/3,000 | 全命中 | PASS |
| 10,000 库位着色 P95 | 2.2ms | ≤3,000ms | PASS |
| Draw calls 最大值 | 36 | ≤100 | PASS |
| 同屏标签最大值 | 34 | ≤200 | PASS |

旧执行器把 `Iris.*Xe` 当成默认硬门禁。双显卡 Windows 上当前 Chrome 选择 RTX 3060，但所有产品相关门槛均通过；因此精简为“硬件 WebGL2 + 禁止软件渲染 + 固定绝对预算 + 全程同一渲染器”，GPU 品牌正则只保留为可选环境诊断。该调整删除机器品牌偶然性，没有降低任何性能、正确性或稳定性预算。

## 可访问性与视口

- 生产 Canvas 现在可聚焦，具有可达名称、快捷键说明和 1/2/3、Home、O、F、P 键盘操作。
- Viewer 工具栏使用 `toolbar`、命名按钮、`aria-keyshortcuts` 和切换态 `aria-pressed`；楼层列表使用可聚焦按钮与 `aria-current`。
- 关键控件有高可见焦点环，加载/错误分别使用 `status` / `alert`。
- Playwright 4/4 通过：Published-only 请求边界、1440×900、1280×720、键盘、Chromium Accessibility Tree、关键 Viewer 控件 4.5:1 对比度、零页面/控制台错误。
- 浏览器固定数据在界面和证据中明确标记 `SIMULATED · PLAYWRIGHT-FIXTURE`，只验证 UI 行为，不计生产数据验收。

## 证据

外部受控目录：`D:\CP6-Space-Evidence\viewer-wp5\2026-08-28-4b774bb4`。GA 索引只记录无机器路径的受控 URN 与 SHA-256。

| 证据 | SHA-256 |
|---|---|
| Published 边界 JSON | `ECBC8F954857A2E0E8054984ADC2C28C3DB11E801DDF702A36641909980949E1` |
| 性能原始 JSON | `B0C969B1411034E560253A06174F6A2E9ACCB5615F846AD2B93F3F8D8E48B3DD` |
| 性能截图 | `D60004A870332AE81065193B23D86825A993BC8C3F3C7046C2AB69D54593987C` |
| 无障碍 Playwright JSON | `BD661C9998198F83D8736AE0E8FB95FC511E2A413AD83955AF6B7A8E7B093BC0` |
| 1440×900 截图 | `E96456353474588A3B604B3E81B7D327F2CB63C59118E061E311293105A0573A` |
| 1280×720 截图 | `0607D2E36873052F5E35510A2235B8214CA575AD4D9494349D9678A0473AFC6C` |

结构化接受证据为 [`viewer-formal-evidence-v1.0.0.json`](../acceptance/v1.3-ga/viewer-formal-evidence-v1.0.0.json)。它绑定精确应用提交、13 个生产/测试源 Git Blob 与 SHA-256、全部预算/实测值、原始证据哈希和非生产边界。
