# WP02-03 第十轮：字段权限确定性计算

2026-10-16；执行对话 A；按[执行包](../../../dispatch/20261016-wave10/WP02-03.md)交付限定纯计算组件。
Main 基线 `8823c31421c1346d2750faee8da16d016ff5fc56`；分支 `codex/wp02-03-field-decisions-20261016`。
Handoff 分配 `573934e2a264ee3cff4a01e8519f0d5a123a6ebc`；START `4f6b90f`。候选固定提交及草稿 PR 在本轮 Handoff DELIVERY 登记，合并与共享状态同步由 COORD 串行执行。

## 结果与采用边界

新增 Core 内部 `FieldPermissionInputV1`、`FieldPermissionEvaluatorV1`、`FieldPermissionDecisionV1` 等本地类型，没有发布 HTTP/wire schema 或改动既有公共接口。每次输入只覆盖同一 tenant/subject/resource；期望和来源的 policy/registry/mapping/source-permission 版本须完整且精确相等，角色版本还须与实际存行相等。逻辑算法版本采用原设计的 `MainStoredFieldMinThenMaterializeV1`，不改称已发布协议。

可信调用者提供完整 registry 域、当前有效角色及本资源完整实际存行；先按字段聚合真实存行 MIN，聚合完成后才给缺项物化1。结果保留原 Access、`EXPLICIT_AGGREGATE` / `REGISTERED_DEFAULT`、实际来源行/角色版本、映射和逐用途原因。A缺+B3=3、A缺+B2=2、A2+B3=2、显式A1+B3=1；不同角色合法值不误判冲突。同一存行ID或同一角色/资源/字段的冲突值拒绝，完全相同行重复不改变结果。

| 用途 | 字段必要条件 | 进一步限制 |
|---|---|---|
| Read | Access 1或2 | 该用途的映射、action、对象及适用限制 |
| Write | Access 1 | 独立写action/对象与限制；不凭可读取得可写 |
| Export | Access 1或2 | 独立导出action/对象与限制；不借读取许可 |
| Download | Access 1或2 | 独立下载action/对象与限制；不借先前导出成功 |

每个字段/用途分别输入 action、对象、online、projection、PII、purpose、business 的已计算交集。action/对象必须明确允许；其他限制必须明确允许、拒绝或不适用。缺项、未知值和非法枚举均不能取得许可。调用方必须事先按完整 grant clause 计算这些输入，不能拼接不同 grant 的许可；本组件不重新解释 Space masking，也不恢复匿名化数据。

映射明确每用途 API 路径、Owner签认事实及所需同用途源字段依赖；一个源字段可对应多个路径，所有依赖取交。缺映射、未签认、未知/空依赖及循环使相关用途不可判定；隐藏/只读或受限依赖不能绕过。迭代解析依赖避免递归栈溢出。冲突映射/限制或无效来源输入关闭该资源决定，不返回可误用的默认字段表。

集合在输入构造时复制，嵌套映射依赖也复制；输出集合只读，来源元素为不可变记录。重复计算、输入顺序变化和构造后修改原集合不改变权限结论。输入捕获过程本身须由调用方保证一致，不支持边修改源集合边捕获。

**如何消费：** `IsAvailable` 仅表示来源结构已通过校验，不是全局批准。Owner 必须检查相应字段的 `Uses[用途].Allowed` 或 `CanRead/CanWrite/CanExport/CanDownload`；原 Access 本身不是最终许可。用途状态是 Allowed / Denied / Unavailable；已知拒绝优先，但同时保留未知原因。全局无效结果含错误且字段表为空，不能据此重建默认1。

## 准确来源与未接入范围

依据 [SYS-IAM-01 §5](../../../../design-handbook/modules/SYS-IAM-01.md)及原 v0.2 §5.1–5.3，原文 SHA-256 已实际核对为 `d84f5873e46c4c98185677b9e8695b48bea4fed0a20b811c8433fa0fbbb90840`；本次实读相关算法、版本、字段映射和legacy边界。另对照 remediation `07-shared-consumer-mappings.md` 的 §权限和字段计算，未把完整Space组合适配冒充本组件已实现。

现有 PermissionAggregator/FieldRegistry/FieldPermService/CurrentPermissionContext/SensitiveFieldPolicy、EF、认证入口、项目与依赖配置均未改。旧null/0掩码、只读原值恢复及旧测试保留；本组件没有被注册到任何真实授权链。

组件无法认证调用方自报的完整registry、Owner签认、有效角色、当前版本或各限制事实。独立字段注册/映射发布、真实认证、撤权fence、缓存清理、最终提交权、每页/导出/下载重验和业务消费者接线留待后续。完整 WP02-02 前置及 WP02-03 全任务状态保留；本局部可消费算法交付不等于真实授权业务通过。

## 本地验证与失败记录

环境：本机 .NET SDK 8.0.424，锁定恢复沿用现有正式包缓存；无项目/锁文件变更。实际命令（在本工作树根目录）：

```powershell
& 'C:/Users/tt/.dotnet/dotnet.exe' restore CP6.Tests/CP6.Tests.csproj --locked-mode --packages D:/CP6-local-test/WP02-02/adopt-a1/n -p:NuGetAudit=false --verbosity quiet
& 'C:/Users/tt/.dotnet/dotnet.exe' test CP6.Tests/CP6.Tests.csproj --no-restore --filter FullyQualifiedName~FieldPermissionDecisionV1Tests --logger 'console;verbosity=minimal' -p:NuGetAudit=false -v quiet
```

最终定向测试 **14通过、0失败、0跳过**，测试执行62ms（不含构建时间）。原项目引用的必要编译通过；只执行此新组件测试，没有数据库、实际宿主、全仓业务或远程测试。新增代码无编译警告；输出包含原有 CS1998（WmsFeatureFlagsController、CacheServiceTests、CrmOidcServiceTokenTests）和 xUnit2017（PermissionsRelationalTests）警告，未顺手修改。

验证覆盖存行MIN/默认顺序、合法显式1、来源/版本/身份/Access错误、各用途交集、不可用与拒绝、未映射/冲突路径/派生依赖、不可变快照与重复/乱序计算。原权限链没有变化，其既有行为测试未重跑，不称已重新验收。

开发失败记录保留在本对话原工具输出，本报告归并实际结果：初始计算桩返回 `NOT_IMPLEMENTED`，11项中10失败、1个拒绝边界通过；实现后11项通过。补充空依赖回归时14项中1失败、13通过，失败为依赖查询中的 `ArgumentNullException (key)`；改为先识别空依赖、保留 `UNKNOWN_DEPENDENCY` 拒绝诊断后，最终14项全部通过。没有将失败运行改称成功或用候选数据冒充业务验收。

## 审查与交付状态

一次合并 SPEC/QUALITY 的独立代码审查已完成（`wp02_03_review`）：完整阅读三个新增代码文件并对照执行包与 SYS-IAM-01 §5，无阻断项，可按限定纯计算组件范围交付。审查确认默认顺序、来源/身份/版本拒绝、四用途与依赖交集、输入不可变和旧链只读；未另外运行测试。执行者已检查完整差异、空白及报告必要链接。代码与报告仅占本窗口允许路径。

候选/受保护 main 的工作流及依赖与此前分析内容未变：普通工作流仅手动，唯一push入口只匹配 `v*.*.*` tag；本任务只推分支、建草稿PR，不推tag。接手时任务分支无Actions，Handoff私有且Actions禁用。没有触发/取消Actions或修改触发配置，没有发包、迁库、数据库或部署操作。
