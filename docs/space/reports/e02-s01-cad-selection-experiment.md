# E02-S01 CAD 技术选型实验

状态：**Blocked — 中立试验工具已就绪，最终选型不可签收**
当前复验日期：2026-07-30
工作分支：`codex/space-e02-s01-cad-selection`
集成基线：`de27ef22`
历史候选证据：`0d25da4d`（原实验基线 `94bcb496`，验收资产来源
`f27efeff`）

## 结论

1. 下一轮**主试候选**为 ODA Drawings SDK 27.6。
2. 下一轮**备试候选**为 Autodesk APS AutoCAD Automation，必须使用自定义
   AppBundle 输出版本化 CP6 CAD IR；Model Derivative 不替代该备试。
3. Aspose.CAD 26.6.0 已从主备名单淘汰：L5 Seed 5/5 稳定加载失败，且
   L1-L4 的原始实体图层名全部退化为 `0`。
4. RealDWG 2027 不满足当前 Linux 容器硬门槛，只保留为需要显式架构变更的
   Windows Worker 备忘。
5. 在 ODA 和 APS 通过同一套授权样本、正式黄金集、DWG/DXF 版本矩阵和容量
   试验之前，ADR-0001 的实现选型不得从 `Experiment-gated` 改为最终
   `Accepted`。
6. 按冻结回退规则，**DWG Beta 保持阻断**；DXF、PDF 和编辑器路径可以继续，
   但不得把本轮中立探针或 Aspose 评估版结果当成发布门禁。

## 已交付

- 中立数据包审计器：SHA-256、JSON/JSONL、路径逃逸、DXF 成对行/EOF、
  版本、单位、实体/图层/Handle 与答案数量。
- 确定性 50MiB 和 100 万实体 DXF 生成器；大文件生成在 `tmp`，不进入 Git。
- 子进程实验运行器：重复次数、超时、取消、进程树终止、退出码、崩溃、
  峰值工作集、stdout/stderr、观察结果哈希。
- v1 适配器观察协议。
- Aspose.CAD 26.6.0 隔离实验适配器。它不加入 `CP6.slnx`，避免生产/常规
  CI 恢复商业 SDK；只有显式构建实验项目时才下载该包。
- 候选兼容/授权矩阵与本机证据摘要。
- `.gitattributes` 将 DXF/DWG 固定为非文本资产，阻止 Windows
  `core.autocrlf` 改写冻结 CAD 字节并造成伪哈希失败。

入口：

- [`candidate-matrix.md`](../experiments/e02-s01/candidate-matrix.md)
- [`adapter-contract-v1.md`](../experiments/e02-s01/adapter-contract-v1.md)
- [`2026-07-26-local.json`](../experiments/e02-s01/evidence/2026-07-26-local.json)
- [`2026-07-30-revalidation.json`](../experiments/e02-s01/evidence/2026-07-30-revalidation.json)
- [`CP6.Space.CadExperiment`](../../../tools/CP6.Space.CadExperiment/README.md)

## 数据包审计

冻结包最初由独立提交 `f27efeff` 交付，当前 `de27ef22` 集成基线已经包含
同一份 `docs/space/acceptance/v1.0.0` Manifest 与 Seed。本次直接审计该
冻结目录，没有从候选检查点导入 `CP6.Tests/TestData` 的后续副本。

2026-07-30 首次复验发现 Windows `core.autocrlf=true` 会把五份 DXF 检出为
CRLF，从而让 Manifest 的原始字节 SHA 全部失配。新增 `*.dxf -text` /
`*.dwg -text` 后，从 Git 对象恢复原始字节，5/5 SHA 再次严格通过。该修复
只冻结 CAD 资产字节，不修改样本语义或 Manifest。

通过：

- 5/5 Seed 文件 SHA-256 与 Manifest 一致。
- 5/5 DXF 具备成对 group-code/value 行并以 `0/EOF` 结束。
- 30/30 expected element 行与每个样本声明数量一致。
- 覆盖 L1-L5 五类 Development Seed。

未通过 E02 完成门槛：

- Seed 明确 `countsTowardReleaseGate=false`，不是正式 20 份黄金集。
- 没有 DWG。
- 只有 DXF `AC1015`，缺少 `AC1009/AC1021/AC1027/AC1032` 版本矩阵。
- 最大 Seed 891B、最多 9 个实体。
- 正式包中没有 50MiB 或 100 万实体样本。

### 严格复验入口

`audit` 现在把正式黄金集和压力资产分开接收，避免为了让门槛变绿而把容量样本伪装成黄金样本。严格审计新增：

- `Calibration=10`、`Validation=5`、`Release Holdout=5`；
- L1-L5 正式样本各至少 4 份；
- DWG `AC1015/AC1021/AC1027/AC1032` 六字节版本头证据；
- DXF `AC1009/AC1015/AC1021/AC1027/AC1032` 实际 Header 证据；
- 显式 50MiB 和 100 万实体压力文件的绝对路径、SHA-256、大小和流式探针结果。

用当前 Development Seed 加两份既有压力文件执行时，完整性通过，50MiB 与 100 万实体门槛通过，
命令按预期以退出码 `3` 失败关闭。剩余缺口被收敛为：正式 20 份及其 10/5/5 分层、每类 4 份、
DWG 文件与 DWG/DXF 版本矩阵。DWG Header 只证明数据包版本，不替代候选适配器的完整解析和保真试验。

## 实验工具校准

中立 DXF 探针只用于证明运行器和压力数据有效，不计候选得分。

| 场景 | 运行 | 成功 | P50 | P95 | Max | 峰值工作集 |
|---|---:|---:|---:|---:|---:|---:|
| 5 Seed，各 5 次 | 25 | 25 | 137.569ms | 169.915ms | 170.260ms | 27.836MiB |
| 50MiB / 860,000 LINE | 20 | 20 | 1,012.12ms | 1,111.86ms | 1,171.94ms | 104.96MiB |
| 1,000,000 LINE | 5 | 5 | 1,122.16ms | 1,137.41ms | 1,137.41ms | 104.98MiB |

两份压力样本的每次 observation SHA 均一致。额外夹具证明退出码 17 被记录为
`Crash`，1 秒超时在约 1.037 秒被记录为 `Timeout` 并终止进程树。

这些数字来自本机 Windows x64/.NET 8.0.25，并非 ADR-0004 冻结的
8 vCPU / 32GB Worker；只能校准工具，不能与最终候选性能排名混用。

## Aspose.CAD 26.6.0 结果

### Seed

| Seed | 结果 | 实体/Handle | 单位 | 图层 |
|---|---|---|---|---|
| L1 | 5/5 成功 | 7/7，匹配 | Millimeter | 全部错误归为 `0` |
| L2 | 5/5 成功 | 6/6，匹配 | Millimeter | 全部错误归为 `0` |
| L3 | 5/5 成功 | 4/4，匹配 | Millimeter | 全部错误归为 `0` |
| L4 | 5/5 成功 | 9/9，匹配 | Millimeter | 全部错误归为 `0` |
| L5 | 0/5 | 加载阶段失败 | 未到达 | 未到达 |

L5 每次均由 `Aspose.CAD.CadExceptions.ImageLoadException` 包装同一
`NullReferenceException`，堆栈位于 TEXT/尺寸更新路径。成功的 20 次运行
P50 573.699ms、P95 698.649ms、最大 729.198ms、峰值 91.785MiB。

冻结 Seed 是合法但最小化的合成 DXF，没有 TABLES/LAYER 定义；实体 code 8
仍明确携带 `WALL/RACK/...`。CP6 契约要求保留原始 Layer/Handle，不能要求
客户先“修图”来规避解析器丢失。因此图层退化和 L5 崩溃都是硬门槛失败。

### 容量

无许可证评估版在 50MiB 与 100 万实体输入上都只返回 100 个实体。本轮观测
耗时分别约 9.974s / 11.450s，峰值约 568.691MiB / 654.352MiB，但因为输出
已截断，这些数字不构成容量通过或失败。Aspose 官方授权说明要求在使用 SDK
前加载许可证；其评估说明建议申请 30 天临时许可证以移除评估限制。

由于 Aspose 已在 Seed 保真与稳定性硬门槛失败，本卡不再申请临时许可证，
除非厂商先确认并修复上述两个可复现问题。

2026-07-30 使用绝对适配器路径独立复跑 25 次，结果仍为 L1-L4 20/20
成功、L5 0/5；20 个成功 observation 的图层仍全部只有 `0`。因此淘汰结论
保持不变。性能数字不进入最终评分，因为运行环境不是冻结 Worker，且无许可
评估版容量仍不可判定。

## ODA File Converter 27.1 历史边界探测

为判断能否先补合成 DWG/版本语料，本轮下载了 ODA 官方最新免费 Converter 的 Windows MSI 和 Linux
AppImage。MSI 及解包后的 `ODAFileConverter.exe` 的 Authenticode 均有效，签名主体为
Open Design Alliance；MSI/AppImage SHA-256 已写入本地证据。Windows 使用 MSI 管理式镜像解包到
`tmp`，没有安装产品；Linux AppImage 也只解包到 `tmp`，没有安装 FUSE、Xvfb 或其他 WSL 包。

技术结果为负证据：

- [官方 Converter 页面](https://www.opendesign.com/guestfiles/oda_file_converter)只列出输入字段，
  没有公开 27.1 的参数顺序或 CLI help 契约；
- `/?` 和两种受超时保护的参数顺序都进入 GUI，所有进程均已关闭，未生成 CAD 文件；
- [ODA 官方 FAQ](https://www.opendesign.com/faq/question/what-are-oda-viewer-and-oda-file-converter)
  明确该工具是 SDK 能力示例，非会员只能用于非商业应用。

因此它不进入 CP6 验收资产生成链，也不替代 ODA Drawings SDK 授权试验。版本矩阵仍必须来自获批、
可追溯的正式黄金资产或法务批准后的 SDK 试验。

2026-07-30 官方产品页已显示 Drawings SDK 27.6。历史 Converter 27.1
负证据不被改写为 27.6 SDK 结果；下一次获批主试必须记录实际 SDK 包版本和
SHA-256。

## 授权试验 Preflight

冻结 Backlog 声明 E02-S02 依赖 E02-S01，主 Spec 又要求选型试验在实现前完成。因此本轮没有开始生产
`ICadConverter` 或厂商适配器，而是增加只属于 E02-S01 的 fail-closed preflight。

Preflight 在执行任何 SDK/服务调用前检查：

- 正式黄金集和两份独立压力资产必须通过严格审计；
- 法务记录必须明确覆盖多租户 SaaS、Worker 扩缩容、灾备、非生产及重新分发/托管服务；
- Worker 必须是冻结的 8 vCPU / 32GiB，并具备受限身份、无业务凭据、独立临时目录和进程树终止证据；
- ODA 必须同时提供 Windows/Linux SDK 包、SHA-256、License 环境变量和 `DenyAll` 网络策略；
- APS 必须提供获批区域、DPA、保留/删除证据、固定 Engine 版本、Secret 环境变量和批准端点网络策略。

ODA 与 APS 模板当前都按预期退出 `4`。报告只记录 Secret 环境变量名及是否已配置，不序列化 Secret
值。即使未来 preflight 通过，也只授权开始获批试验，不代表 E02-S02 或生产部署获批。

额外只读盘点覆盖全部 Git refs 和本机工作区：验收历史只有 `f27efeff`，没有
`countsTowardReleaseGate=true` 的 Manifest、DWG Git 对象、本地正式 DWG、ODA Drawings SDK 包，
三个候选 Secret 环境变量也都未配置。该盘点只记录 Secret 是否存在，从未读取或保存 Secret 值。

入口与模板：

- [`vendor-trial-intake.md`](../experiments/e02-s01/vendor-trial-intake.md)
- [`oda-trial-preflight.example.json`](../experiments/e02-s01/oda-trial-preflight.example.json)
- [`aps-trial-preflight.example.json`](../experiments/e02-s01/aps-trial-preflight.example.json)

## 授权与采购结论

当前只能形成试验采购顺序，不能形成生产法律批准：

- ODA 官网当前说明 Commercial 级别不含 Web/SaaS，Sustaining/Founding
  包含；CP6 至少按 Sustaining 预算占位，但 Worker 服务、并发、容灾、
  非生产环境和重新分发必须让法务基于正式协议确认。
- APS 是外部受控服务，必须批准数据区域、DPA、网络出站、凭据、保留/删除、
  用量付费和引擎迁移成本。
- Aspose 公网/SaaS 需要匹配的 OEM 类授权，但因技术硬门槛已失败，不进入
  当前采购短名单。
- RealDWG 的授权与评估由 Autodesk 指向合作伙伴；在 Linux 门槛不变时无需
  进入本轮报价。

候选能力、平台和当前官网价格/条款均见候选矩阵。网页价格是研究证据，不是
供应商报价。

## 解阻清单

以下全部完成后，E02-S01 才能签收：

1. 数据/QA 提供至少 20 份授权、脱敏、不可变黄金 CAD：
   Calibration 10、Validation 5、Release Holdout 5，L1-L5 每类至少 4。
2. 补齐 DWG 2000/2007/2013/2018+ 与 DXF R12/2000/2007/2013/2018，
   以及 BLOCK/INSERT/ATTRIB/ATTDEF/TEXT/MTEXT/HATCH/SPLINE/ELLIPSE/
   DIMENSION/XRef 证据。
3. 法务/采购确认 ODA 正式协议；工程获取 Windows 与 Linux SDK 包、版本号
   和校验值。
4. 平台/安全为 ODA 试验 Worker 提供 8 vCPU / 32GB、受限服务身份、
   网络 deny-by-default、CPU/内存/磁盘/时间配额和恶意文件隔离。
5. APS 提供非生产凭据、批准区域和删除/保留证明；构建 AppBundle 输出同一
   v1 observation/CAD IR，并记录 engine alias/version。
6. ODA 与 APS 在同一环境运行：黄金样本各 5 次、50MiB 20 次、100 万实体、
   200MiB 硬上限、崩溃、超时、取消、重试与并发。
7. 依据 ADR-0001 权重计算最终分数；低于 80 不得为主选。若都不通过，
   保持 DWG Beta 阻断。

## 验证命令

```powershell
dotnet test tools\CP6.Space.CadExperiment.Tests\CP6.Space.CadExperiment.Tests.csproj `
  -c Release --no-restore

dotnet run --project tools\CP6.Space.CadExperiment -c Release --no-build -- `
  audit `
  --manifest <golden-package>\manifest.json `
  --stress-50mb <tmp>\stress-50mb.dxf `
  --stress-million <tmp>\stress-1m-entities.dxf `
  --output <evidence>\dataset-audit.json `
  --require-e02-ready

dotnet run --project tools\CP6.Space.CadExperiment -c Release --no-build -- `
  preflight `
  --config <evidence>\preflight.json `
  --output <evidence>\preflight-result.json

dotnet build tools\CP6.Space.CadExperiment.AsposeAdapter\CP6.Space.CadExperiment.AsposeAdapter.csproj `
  -c Release --no-restore
```

当前结果：实验工具 10/10 测试通过（含相对适配器参数工作目录回归）；工具与 Aspose
实验适配器均 0 warning /
0 error。当前 Seed 严格审计为 `integrityPassed=true`、预期退出码 `3`；
ODA 与 APS 模板 preflight 均按预期退出 `4`。常规解决方案不包含 Aspose
实验项目，也未开始 E02-S02 或任何生产 CAD 适配器。

## 2026-07-30 官方资料复核

- ODA 官方当前显示 Drawings SDK 27.6，覆盖 AC1009～AC1032、Windows 与
  Linux；会员表仍明确 Commercial 不允许 Web/SaaS，Sustaining/Founding
  允许，公开首年价格分别为 USD 3,000 / 7,500 / 37,500。
- Autodesk 官方仍将 AutoCAD Automation 定义为运行自定义 add-in/script
  处理 DWG 的云服务；新业务模型下 Automation 属于 rated API。AutoCAD
  2027 engine `Autodesk.AutoCAD+26_0` 使用 .NET 10，当前公布移除日期为
  2032-03-29。
- Aspose 官方要求在使用 CAD 类前加载许可证；公开 Web/SaaS 场景需要匹配
  的 OEM/SDK 类授权。RealDWG 2027 官方系统要求仍是 Windows 11、
  Visual Studio 2026 与 .NET 10。

这些资料只维持“ODA 主试、APS 备试、Aspose/RealDWG 淘汰”的试验顺序，
不构成采购报价、法律意见或 E02-S01 验收。
