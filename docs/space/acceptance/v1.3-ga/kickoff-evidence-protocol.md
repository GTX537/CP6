# CP6 Space Studio V1 M0 开工证据协议

## 1. 目的

本协议把核心 GA 的五类外部输入从“附一份说明”提升为结构化、可逐项验证的开工资料包。它不代替 WP3 Provider 认证、WP7 正式评估、WP8 双仓 Pilot 或最终五方签字，只证明这些工作已经具备真实的人员、数据、审批、环境和窗口。

复制 [`kickoff-evidence-template.json`](./kickoff-evidence-template.json) 创建版本化 Manifest。原始客户 CAD、供应商凭据和个人敏感材料保存在受控外部系统；仓库只保存不透明引用、SHA-256、非敏感元数据和实名接受记录。

## 2. 增量关闭规则

五个 `externalInputs` 可以分批完成：

1. Manifest 的 `conclusion` 使用 `InProgress`；已完成分区标记 `Complete`，未完成分区保持 `Pending`。
2. 在 `ga-evidence-index.json` 对应输入中填写相同的实名 `ownerName`、Manifest 仓库相对路径 `verificationManifest`，并用 `evidence` 证明 Manifest 自身哈希。
3. 运行专项校验器并指定输入 ID。
4. 五个分区全部完成后，生成新版本或更新尚未签字的工作副本，将 `conclusion` 改为 `Pass`，再执行不带 `-InputId` 的完整校验。

已经被验收或签字引用的 Manifest 不得原地覆盖；任何内容变化都创建新版本并重新计算哈希。

## 3. 五类输入的冻结语义

### NAMED_GA_SIGNERS

- 恰好包含 Product、QA、WMS、Architecture、Security 五个角色。
- 每个角色登记真实姓名、确认审批权并附任命/授权证明。
- 总 GA 索引中的五个 `signers[].name` 必须逐角色与本登记一致；此处只是实名确定，最终 `Signed` 仍在 GA 签字阶段完成。

### CORE_TEAM_ALLOCATION

- 至少两名 Backend、两名 Frontend3D、一名 QA，核心成员不得重复。
- 每项分配记录 1–100% 投入、开始/结束日期和证明，周期覆盖 kickoff 至目标 GA。
- Product、WMS、Architecture、Security、DevOps 五个共享角色均登记实名负责人和分配证明。

### AUTHORIZED_GOLDEN_CAD_CANDIDATES

- 恰好 20 份唯一真实候选，DWG 与 DXF 均存在，L1～L5 各至少四份。
- 每份只记录 `urn:cp6-space-golden-cad:*`、Source SHA-256、真实字节数、格式、布局和授权/脱敏证明。
- `license=ApprovedCustomerDerived` 且 `authorizedForGoldenEvaluation=true`。
- `candidateSetSha256` 对按 `sampleRef` 排序后的 `<sampleRef>:<lowercase sourceSha256>` LF 文本计算 SHA-256，防止候选集被静默替换。
- 这里证明候选输入就绪；10/5/5 冻结、双标注、Provider 质量和 Holdout 仍由 WP7 门禁完成。

### PROVIDER_APPROVALS_AND_ISOLATED_WORKER

- 至少两条不同 Provider Key 的候选链，不能用同一 Provider 的不同版本冒充主备候选；统一使用 `ICadConverter`。
- 授权、安全、数据区域、保留/删除审批必须全部通过并有独立证明。
- 原始 CAD 默认仅进入受控隔离 Worker；Worker 必须环境哈希封存、隔离、凭据只存 Secret 引用、原始 CAD 临时保留且网络默认拒绝。
- 若 `dataBoundary=ApprovedCloud`，还必须分别保存租户、客户和安全审批；不得从本地链静默跨到未批准云链。
- 本输入不决定 Primary/Backup；最终排序仍由同黄金集、同 Worker 的资格评分和 WP3 Site 认证决定。

### TWO_PILOT_SITES_AND_WMS_WINDOWS

- 恰好一个 Greenfield 和一个 Retrofit，不透明 Site 引用互不相同。
- 每仓登记业务 Owner、实施 Owner、WMS Owner、CP6 WMS 窗口与选择证明；WMS 窗口必须覆盖完整计划 Pilot。
- 计划 Pilot 窗口至少连续 14 个日历日；这里只证明 Site 和窗口已锁定，真实逐日运行仍由 WP8 验收。

## 4. 校验命令

```powershell
./tools/Test-SpaceGaKickoffEvidence.ps1 `
  -ManifestPath <开工 Manifest 路径> `
  -InputId CORE_TEAM_ALLOCATION `
  -ExpectedOwnerName '<实名 Owner>'

./tools/Test-SpaceGaKickoffEvidence.ps1 `
  -ManifestPath <最终完整开工 Manifest 路径>

./tools/Test-SpaceGaEvidence.ps1
```

正式模式拒绝模板、`tools/test-fixtures`、`:test:` URN、仓库内原始 DWG/DXF、绝对/越界路径、未来接受时间、占位人名和哈希不一致。测试专用的 `-AllowTestFixtures` 只能由自动化自测使用。

## 5. 状态边界

- 专项校验通过只证明开工输入结构和内容达标，不把任何 WP Gate 自动改为 `Accepted`。
- 五类输入全部 `Complete` 仍不等于 GA；WP0–WP8、真实环境证据、双仓运行和五方 `Signed` 缺一不可。
- 外部输入延期时顺延目标日期，不减少人员、CAD、Provider、Site 或签字门槛。
