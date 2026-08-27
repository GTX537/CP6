# Space Studio Development V1 100% 验收

日期：2026-08-27
源基线：`main@4927fe4d499f6c8215926a8abbb3add96d14e819`
DeliveryOwner：`BUBAO.GAO`

## 结论

Space Studio 第一版在 **RepositoryAndDevelopment** 范围达到
`DevelopmentComplete` / 100%。六个开发 Gate 全部通过，统一 Draft 与模板、CAD/Excel/
手工三路径、编辑器、2D/3D Viewer、开发 CAD Worker，以及发布/WMS/安全/恢复的仓库级
能力已经闭环。

这不是生产 GA。`formalGaEligible=false`、`countsTowardProductionGa=false`；20 份 CAD 是
合成 `DevelopmentSeed`，没有使用真实客户 CAD、生产许可证、独立 Backup、生产 WMS 窗口
或 Pilot。正式 Core GA 在本次验收时继续为 72% / `NoGo`。

## 数据与容量复验

在源基线重新生成 `development-v2.0.0`，得到 20 个样本、49,983 bytes；L1～L5 各 4
份，覆盖 `AC1009/1015/1021/1027/1032`。Dataset Auditor 结果为
`integrityPassed=true`。

额外生成并审计两份不入库的压力资产：

| 角色 | 字节 | 实体 | SHA-256 |
|---|---:|---:|---|
| 50 MiB | 53,190,207 | 670,000 | `2a13c6c4d1e8487d760349764ca2423106cfa9aea4a1915d06d12ddbb2661bc7` |
| 100 万实体 | 79,517,079 | 1,000,000 | `c8e7694093aadd266cf752b5ac6185b13226ea26573196e34fc71d61558f95f0` |

仓库包与再生包共 29 个文件。27 个字节相同；两个 JSONL 各有 20 行，工作树因系统
`core.autocrlf=true` 使用 CRLF，再生包使用 LF。归一化换行后内容和 SHA-256 完全一致，
不是生成器漂移。

## 正式边界负向验证

对同一数据显式执行正式 E02 Ready 审计时，退出码为 3，结果
`e02ReadinessPassed=false`。失败项恰为：0 份正式黄金样本、无 10/5/5、无 DWG、无 DWG
版本矩阵；DXF 版本矩阵、50 MiB 与 100 万实体仍通过。这证明开发数据可以关闭开发版，
但不能越权关闭正式黄金集。

开发版校验器还会：

1. 逐文件复核 20 个种子的 SHA-256、L1～L5 分布和 DXF 版本矩阵；
2. 从六个 Gate 派生完成度，不允许 Pending Gate 与 100% 并存；
3. 强制单人开发范围不包含授权 CAD、正式 Provider、Backup、生产 WMS/Pilot 或正式签字；
4. 扫描正式 GA 的 accepted evidence，禁止引用开发数据集、开发验收索引或本报告。

## 双轨状态

| 轨道 | 状态 | 完成度 | 含义 |
|---|---|---:|---|
| Development V1 | `DevelopmentComplete` | 100% | 单人、仓库与开发环境第一版已闭环 |
| Core GA | `NoGo` | 72% | 等待真实 CAD/授权、正式 Provider/许可证、生产联调、Pilot 与 Owner 签署 |

后续没有开发版功能 Gate；只有用户未来提供真实外部输入时，才继续推进正式 GA。两条轨道
不得合并表述。

## 本地门禁结果

- Development V1 派生校验：6/6 Gate、20/20 样本、100%、`developmentReady=true`。
- Development V1 失败关闭：8/8；GA attestation 35/35、Pilot 21/21、Golden CAD
  31/31、Kickoff 22/22、单人开发身份边界 8/8，总计 125/125。
- AutoCAD 安装环境完整 CAD Experiment：57/57、0 failed、0 skipped；Core Console 与
  Autodesk Floor Plan 样例 SHA-256 和既有受控记录一致。
- 正式 GA 普通校验通过并输出 `NoGo / 3 / 9 / 1`；`-RequireGaReady` 按设计退出 2。
