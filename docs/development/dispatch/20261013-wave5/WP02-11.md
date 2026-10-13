# WP02-11 第五轮执行包 — Platform HTTP错误、安全重试和截止时间

沿用对话 D（上一任务 S01-07），仓库 Platform。READY/CONFIRMED；等待真实START。以下窗口落实完整原任务，不改变原验收或发布权限。

## 准确入口

- [原任务](../../task-plans/WP02.md#wp02-11)、[并行约定](../../09-parallel-development.md)、[PostgreSQL方向](../../08-postgresql-baseline.md)、[机器执行包](dispatch.json)。先读 `D:/CP6/AGENTS.md` 及所属仓适用规则；本机归档只读，引用以固定来源hash为准。
- 独立工作树 `D:/CP6.Platform-worktrees/wp02-11-20261013`；分支 `codex/wp02-11-http-resilience-20261013`；创建基线 `9259a45d39bae7e98ca804a74778a588c1ad4619`。
- Main输入 `a1824fec3d4824d45e27693f2e715b311ee5f665`；Platform输入 `9259a45d39bae7e98ca804a74778a588c1ad4619`；CRM输入 `1e4eea61c31d7547c3b3495d392eefbed99b7339`。开工刷新主线并核源码/依赖漂移，不把尚未合并候选当已交付能力。
- 来源准入报告：[admission-platform.md](../../evidence/wave5-dispatch-20261013/admission-platform.md)；[admission-platform.json](../../evidence/wave5-dispatch-20261013/admission-platform.json)

## 独占范围

- `src/CP6.Platform.AspNetCore/HttpV2/`
- `src/CP6.Platform.AspNetCore/Cp6HttpResilienceServiceCollectionExtensions.cs`
- `src/CP6.Platform.AspNetCore/Cp6ProblemDetailsExtensions.cs`
- `tests/CP6.Platform.AspNetCoreTests/HttpV2/`
- `docs/WP02-11-HTTP-V2.md`
- `docs/development/evidence/WP02-11/`

新功能在HttpV2命名空间，通过显式opt-in重载使用；两个既有extensions文件只能增加兼容入口，旧API/profile/default保留。新测试命名空间包含HttpV2；既有测试及共享Contracts/Abstractions/Testing、根项目/锁/依赖配置均只读，复用TimeProvider与当前HTTP resilience依赖，不新增共享helper。

台账、生成视图、四份项目记忆由COORD串行维护。完整任务卡中要求的共享状态更新由COORD在交付时完成；执行对话提供可审查变更建议，不自行改共享状态。本范围以外的公共文件/类型/配置需求须通过Handoff取得具体窗口后再改。

## 实现与验收

- [ ] 新增安全错误正文与traceId映射，按准确端点白名单/数量/字节上限处理errors，避免异常/输入/secret原样输出；正文已开始或连接已断不拼第二份正文。保留既有CRM错误合同和认证流程。
- [ ] 有名client/operation可信绑定才可选择写重试；绑定tenant/action/key/version/digest、原receipt/query/retention及目标/凭据/route epoch证据。外部header、Idempotency-Key格式或bool声明不授durable dedup能力；真实Owner未证明的写profile必须Disabled。
- [ ] monotonic剩余budget及取消贯穿子调用，准确区分attempt/total超时，不按duration猜来源。caller取消不重试，未知来源停止；HttpClient和pipeline总预算有唯一权威。
- [ ] 严格执行来源报告列出的Retry-After单值/整数/三日期、UTC到monotonic锚定、一次jitter、max(S,L)、等待上限与W+A≤R的决策/发送双核。超大合法等待直接停止；提前/迟醒重验，取消零后续attempt。固定边界和正反例逐条运行。
- [ ] 写Unknown保留原key/body/digest/binding并先查询原结果，不能改身份/租户/key再次制造效果；CircuitOpen不把已有Unknown改为NotSent。07同轮候选不是远端Owner已原子去重的证据。
- [ ] 实际本地loopback HTTP验证attempt数、取消和预算，确定性clock覆盖精确边界；原API与既有HTTP回归按影响运行。原42AC逐项标明本任务子断言及宿主责任，不把fixture协议成功写为生产exactly-once。

本任务无数据库窗口，无真实远端写profile启用或生产接线。测试fixture证明库策略，不证明远端业务去重；声明未经证实的能力必须失败关闭。

## 验证与交付

下列是本地验证入口，具体命令随任务私有计划记录。新增测试必须有实际发现数和有效断言；无法运行保留NOT_RUN及原因。源码、输入、依赖和环境不变的成功结果按准确来源复用；新行为和未验证组合才补对应验证。完整任务收尾集中审查一次，保留失败、源码SHA/工作字节、输入hash、真实工具版本、产物hash和未覆盖范围。

```powershell
dotnet test tests/CP6.Platform.AspNetCoreTests/CP6.Platform.AspNetCoreTests.csproj --filter FullyQualifiedName~HttpV2
dotnet test tests/CP6.Platform.AspNetCoreTests/CP6.Platform.AspNetCoreTests.csproj --filter "FullyQualifiedName~HttpResilienceTests|FullyQualifiedName~GatewayContractTests|FullyQualifiedName~AuthenticationContractTests"
dotnet test tests/CP6.Platform.ArchitectureTests/CP6.Platform.ArchitectureTests.csproj
```

1. 在本人Handoff资料分支追加START，记录实际工作树/分支、当前基线、来源hash与接受的窗口。已CONFIRMED窗口允许按卡执行；COORD实际读到START后才登记IN_PROGRESS，不回填虚构领取。
2. 不修改原归档、历史接受、原输入expected；125业务目标、247运行NOT_RUN、41历史REFERENCE_ONLY、ITER/REL缺件和WP05 PROPOSED不能因局部通过自动改变。
3. 每次push/PR前检查候选及受保护main的全部事件、过滤和工作流依赖；不触发/重跑Actions。Platform的RFC3161窄路径PR事件仍存在，必须确认没有命中；不要编辑相应workflow或其ReleaseTests门禁文件。只推本人分支，不推tag。
4. 候选DELIVERY附完整SHA/PR、完整diff、原/新验证范围和未完成边界。COORD复核、串行正常合并及远端main包含核对后才登记DONE；不得把本地库验证称为宿主或生产采用。不发布正式包、不迁移既有数据、不部署。

Handoff：`GTX537/CP6.AI-Handoff`，本人分支 `codex/dev-handoff-wp02-11`，只追加 `handoffs/CP6-DEV-20261012/tasks/WP02-11/messages/`。首次从已核对的协调分支固定提交创建；读取 `codex/dev-coordination-20261012` 本任务replies。旧窗口已关闭。发布消息不自动唤醒聊天，ACK也不代替任务或业务验收。
