# E02 合成开发 CAD 图纸包

日期：2026-08-02
范围：开发用 DXF 图纸生成，不替代 E02-S01 正式供应商试验或发布黄金集

## 交付结论

CP6 现在具备一套可重复生成、可审计的 20 份合成 DXF 开发语料，无需在开发电脑安装 AutoCAD，也不依赖客户、供应商或互联网下载图纸。

- 目录：`docs/space/acceptance/development-v2.0.0`
- L1～L5：每类 4 份，共 20 份
- DXF 文件头：AC1009、AC1015、AC1021、AC1027、AC1032
- 单位：毫米；坐标：FloorLocal-ZUp
- 图元覆盖：LINE、POLYLINE、LWPOLYLINE、ARC、CIRCLE、INSERT、ATTRIB、TEXT、MTEXT、HATCH、SPLINE、ELLIPSE、DIMENSION
- 场景覆盖：规则仓库、多楼层、非正交、自动化/冷库/高位库、脏图层、缺失属性、未解析 XRef 和文字噪声
- 权利边界：全部由仓库内生成器专门为 CP6 合成，不含客户、供应商、地址、人员、标题栏或设备序列号数据

## 可重复生成

```powershell
dotnet run --project tools\CP6.Space.CadExperiment -c Release -- `
  generate-dev-corpus `
  --output docs\space\acceptance\development-v2.0.0
```

生成器同时写入：

- `manifest.json`：样本路径、SHA-256、布局分类和发布边界；
- `case-index.json`：版本、图元、图层与开发目标；
- `expected-elements.jsonl`：每份图纸至少一个机器可读目标；
- `expected-issues.json`：L5 噪声、未知图层和未解析 XRef 预期；
- `provider-ir.jsonl`：不包含原始文件、路径、客户或用户信息的最小化特征；
- `layer-mapping.json`：Space 语义映射；
- `LICENSE.md`：合成来源与开发使用声明。

## 验证

- `CP6.Space.CadExperiment.Tests`：12 passed / 0 failed / 0 skipped；
- 20/20 源文件存在且 SHA-256 与清单一致；
- 20/20 DXF group-code/value 成对并以 `0/EOF` 结束；
- 20/20 无重复实体 Handle；
- 五类布局完整性门禁：L1=4、L2=4、L3=4、L4=4、L5=4；
- DXF 文件头矩阵门禁通过；
- 数据包完整性门禁通过。

## 不解除的正式门禁

清单固定为：

```text
purpose=DevelopmentSeed
countsTowardReleaseGate=false
```

因此本交付可以用于解析器、语义映射、问题生成、CAD/AI IR、UI、回归测试、演示和候选 SDK 技术试验，但不能证明：

- 原生 DWG 读取或写回保真；
- ODA/APS 商业授权和生产部署权利；
- 真实复杂图纸覆盖和客户图纸脱敏；
- Calibration 10 / Validation 5 / Release Holdout 5 的独立正式黄金集；
- 50 MiB、100 万实体和 200 MiB 上限压力门禁。

E02-S01 的“没有足够开发图纸”问题已经解决；剩余阻塞收敛为原生 DWG、供应商授权、冻结 Worker 和独立正式验收证据。
