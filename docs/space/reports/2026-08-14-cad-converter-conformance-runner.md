# CAD Converter 共合同执行器

日期：2026-08-14

范围：Space Studio V1 核心 GA / WP3 仓库实现

结论：`ICadConverter` 已有供应商无关、失败关闭的统一执行边界；真实 Provider 适配、隔离 Worker 注册、黄金集评分和 Site 批准仍为 Pending，WP3 保持 Partial/Pending，核心 GA 保持 72% / No-Go。

## 问题

`ICadConverter` 已定义 ODA、APS 和后续候选必须共同实现的输入输出合同，但此前调用方可以直接调用适配器。这样无法由共同边界证明原始 Source 始终只读、流式 CAD IR 顺序和数量一致、Sink 确实完成，也无法保证适配器返回的 Provider 身份、Artifact SHA、Summary 和 Issues 就是 Sink 提交的产物。

## 实现

- 新增 `SpaceCadConverterContractRunner.ConvertAsync`，作为所有 `ICadConverter` 调用的强制入口；开发转换工具已切换到该入口。
- Runner 不转移调用方 Source Stream 的所有权，并以只读包装拦截同步/异步 Write、WriteByte 和 SetLength；适配器即使捕获并忽略异常，Runner 仍在返回前失败关闭。
- Guarded Sink 固定单线程顺序：Document 必须唯一且最先，Layer/Block 必须在 Entity 前，SourceRef、LayerId、BlockId 唯一，Complete 只能成功一次。
- 完成前逐项验证公开 CAD IR 合同、Layer 声明数量、Entity 支持/不支持数量、未知 Layer、Issue 计数和 Bounds；聚合值不一致时不调用底层 Complete。
- Runner 要求转换结果的 Source SHA、Provider Key/Version、Artifact SHA、Summary 和 Issues 与 Sink 实际完成证据完全一致。无完成、结果漂移或非规范 SHA 使用稳定内部错误码失败关闭。
- 公共合同校验同时补齐未定义 SourceFormat、Unit、EntityType、IssueSeverity 以及负计数拒绝，避免 JSON 数字枚举或适配器对象绕过约束。

稳定内部错误码：

- `SPACE_CAD_CONVERTER_PROTOCOL_VIOLATION`
- `SPACE_CAD_CONVERTER_SOURCE_WRITE_ATTEMPT`
- `SPACE_CAD_CONVERTER_NOT_COMPLETED`
- `SPACE_CAD_CONVERTER_RESULT_MISMATCH`

## 自动化证据

- Runner 与 CAD IR 合同聚焦测试 23/23 通过，覆盖正常完成、Source 只读与所有权保留、被适配器吞掉的 Source/Sink 违规、未 Complete、结果 SHA/Summary/Issues/空 Provenance 漂移、重复 SourceRef、错误记录顺序、Layer 数量漂移和非规范 Artifact SHA。
- CAD IR 合同测试覆盖 DWG/DXF 同合同、Provenance、未知单位、重复/未知引用、汇总一致性，以及新增的未定义枚举和负计数拒绝。
- `CP6.Space.CadExperiment` 生产入口只通过 Runner 调用适配器；适配器自身单测仍可直接调用被测实现，以隔离定位转换算法错误。
- 本次分支验证：Space Unit 525/525、CAD Experiment 34/34、完整 Release solution 0 warning / 0 error。

## 未关闭门禁

- 没有安装、授权、实现或注册真实 ODA、APS 或评分后替代者。
- 没有把真实 Provider Worker 部署到受控隔离环境，也没有 Secret、数据区域、删除保留和客户批准证据。
- 没有在 20 份授权黄金 CAD 和冻结环境上运行两个真实 Provider 版本。
- 没有目标 Site 的一主一备认证、真实 DWG/DXF 回放、故障切换和客户接受证据。

因此，本项只让后续所有 Provider 适配器接受同一套可机器验证的执行合同，不把开发 DXF Converter、Mock、fixture 或单元测试冒充生产 Provider 与 GA 证据。
