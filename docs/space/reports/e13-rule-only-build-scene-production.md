# E13 纯规则 BuildScene 生产执行链接线报告

- 状态：工程切片已完成验证；外部 Provider 路径继续失败关闭
- 日期：2026-08-08
- 起始集成基线：`4d9bc3f6`
- 功能分支：`codex/space-rule-only-build-scene`
- 功能提交：`36cc0241`
- 目标分支：`integration/space-v1-20260730`

## 1. 交付结论

生产 Processing Worker 不再把所有 `BuildScene` Job 交给不可用占位执行器。新的
`SpaceBuildSceneJobStepExecutor` 完整实现既有 12 步作业合同，在 `RuleOnly` 恢复模式下消费私有、
哈希校验通过的 CAD `PreviewSet`，运行确定性规则融合，持久化只读 Generation Proposal 与统一 Issue，
最后把 Run 推进到 `AwaitingReview`。它不调用 Provider、不预留或记录 AI 用量、不写 Draft；Draft 仍只能
经 E13-S09 人工决策和 E13-S10 原子 Apply 修改。

Provider-backed / `SamePolicy` 模式继续稳定返回 `SPACE_AI_PROVIDER_UNAVAILABLE`，并把 Run 投影为 Failed。
本切片没有注册外部 Provider、端点、Secret、网络访问或 High Accept，也没有把开发 Local/Mock Provider
冒充生产外部能力。

## 2. 冻结输入与安全边界

每次执行都重新核验：

- Worker 为内部主体，Tenant、Actor、Job、Attempt、Subject 和当前租约一致；
- Run、Job payload、Draft ModelVersion、ContentRevision、CAD Source、Source SHA 和 Target Floor 一致；
- CAD 原始 Source 文件仍为 Clean Source；
- 选择最新成功 CAD Parse 的 Clean Artifact，重新读取私有对象并校验文件大小、SHA-256、PreviewSet
  自身哈希、Tenant/Version/Source/Parse Job/Floor/Source SHA 血缘；
- 只接受 `Disabled + RuleOnly + null ProviderConfigVersionId`。其他策略不进入规则路径，也不会绕过配额或
  外发门禁。

规则模式从已经校验的 Semantic Preview 生成 provider-compatible 但 local-only 的稳定特征快照。
SourceKey 绑定 `Source SHA + SourceRef`，相同 CAD 在后续 Run 保持稳定；Run correlation 仍按 Run 隔离。
该快照仅用于复用现有融合验证器，明确禁止发送 Provider。

## 3. 12 步执行与可恢复性

执行链覆盖：Pinned Inputs、Locked Facts、Policy、Local Feature Snapshot、Rule-only Invocation、Output
Validation、Rule Fusion、Deterministic Geometry、Proposal Validation、Persistence、Zero Usage 和
Await Review。

- 融合 ProposalSet 以规范 JSON 和自身 SHA 写入 Job checkpoint；后续步骤只消费同 Attempt 的成功或复用
  checkpoint，并再次深验 ProposalSet。
- Run 状态按 `Queued → Preparing → Inferring → Validating → AwaitingReview` 单调推进，标记
  `RULE_ONLY`，即使前置 checkpoint 在重试中复用，首个实际执行步骤也会补齐合法状态转换。
- Proposal 与 Issue 落库使用 Serializable 事务；数据库已提交而 checkpoint 尚未完成时，重放会逐字段
  校验既有数据并返回 reused，不重复创建。
- Input、Security、Resource 的终止失败和最终 Bug/Transient 尝试会同步投影 Run Failed；可自动重试的
  中间 Bug/Transient 不会提前破坏 Run。

## 4. 人工锁自动接线

同 SourceHash、已确认、`SameSourceIdentity` 的 E13-S10 locked facts 现在进入生产 BuildScene 融合：

- 通过源 Proposal 的唯一 SourceRef 把旧 Run SourceKey 重映射到当前稳定 SourceKey；
- `name`、Zone/Rack/Door/Dock/Equipment 类型属性、Aisle direction、Wall/Column 类型和
  Zone/Aisle/Wall 父关系均按 `HumanLocked > DeterministicRule > AI > TemplateDefault` 参与融合；
- 锁定关系的旧目标 SourceKey 先解析为旧 Proposal SourceRef，再映射到当前 Run；缺失、歧义、跨 Preview
  或非字符串事实整体失败关闭，不静默丢锁。

不同 SourceHash 的几何建议继承仍未开放，继续要求确定性几何匹配和显式人工确认。

## 5. Proposal 与 Draft 边界

持久化适配器把确定性毫米几何、字段证据、来源、置信度和关系转换为现有
`SpaceGenerationProposal` 合同；统一 Issue 与 Proposal/Run/Job 绑定。元素缺少可应用尺寸、Aisle/Rack
缺少 Zone 父关系时增加 Blocking Issue，不能接受后在 Apply 阶段才暴露；Rack 没有权威
RackGenerationProfile 时继续沿用 `SPACE_RACK_PROFILE_REQUIRED`，不发明默认货架层级。

本切片不创建初始 Generation Run API。当前生产可达入口是 E13-S11 的 Failed/Stale Run
`RuleOnly` recovery；首次生成 Run 的创建/幂等入口仍是下一项内部缺口。生产外部 Provider、权威
RackGenerationProfile 读取和无人工锁时的确定性父关系推导也没有在本切片中伪造完成。

## 6. 验证证据

| 门禁 | 结果 |
|---|---|
| Rule-only 特征/融合聚焦 | 21/21 passed |
| BuildScene 端到端与 Provider 失败关闭 | 2/2 passed |
| 默认处理器注册 | passed，生产默认执行器已替换占位实现 |
| Space Unit 全量 | 484/484 passed |
| 默认 Space Integration | 277 passed / 94 SQL-gated skipped / 0 failed |
| CP6.Tests | 2811 passed / 17 environment-gated skipped / 0 failed |
| 完整 solution Release（含 Desktop/Android AOT） | 0 warning / 0 error |
| C# whitespace / `git diff --check` | passed |

端到端测试使用真实 Semantic Preview/Diagnostic/PreviewSet 哈希链、私有 Artifact、BuildScene Job 和
same-source modified Proposal/Decision/LockedFact，逐步执行全部 12 步，验证人工名称锁进入最终 Proposal、
重复 Persist 幂等、Run 到达 AwaitingReview、AI Usage 为零且 Zone/Aisle/Rack/Element Draft 表均无写入。
独立用例验证 Provider-backed 模式在 Policy 步失败关闭、零 Proposal、零 Usage。

## 7. 剩余边界

- 首次 Generation Run 创建服务/API、权限、审计与幂等合同尚未实现；
- 外部 Provider 的生产适配器、网络/Secret/区域/数据驻留、真实配额和故障证据仍未提供；
- 不同 SourceHash 锁定建议只能在确定性几何匹配并由人工确认后继承；
- RackGenerationProfile 权威持久化/读取、无锁定父关系的确定性推导和真实 CAD/黄金集验收仍独立存在；
- E13-S14 正式黄金集、S15 影子/试点、S18/S19 发布证据没有因本切片解除。

因此，本报告证明纯规则 recovery 的“排队到可审阅提案”生产执行闭环，不证明首次生成 UX、外部 AI
端到端、正式 CAD 或 GA 签收。
