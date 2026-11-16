# 当前 Main 字段与用途核销入口

W678-01 建立，W679-01 补齐个人表单/附件核销；接续 [W677 整卡复核](../../evidence/W677-01/DELIVERY.md)。本目录是当前实现的导航与核销记录，不是授权配置、第二套任务台账或全部入口已通过的证明。WP02-03 状态仍以 `roadmap.json` 为准。

[resources.jsonl](resources.jsonl) 保存封存目录内每个资源的完整受控字段域、scope、四用途结论、候选源码和路由引用。[routes.jsonl](routes.jsonl) 保存实际 MVC descriptor 的路由、方法及直接声明的属性。`permission_attribute_route_ids` 只连接明确的菜单属性；Controller 通过服务调用 Owner 时可能没有该属性，空列表不意味着未授权。`lexical_source_candidates` 是字面量/Operations 常量检索的首个位置，包含定义和引用，不是调用图。

`fields: []` 表示明确注册了空字段域；`fields: null` 表示只有范围注册，不能互换。未注册受控字段不等于整个 DTO 已批准或无需动作、对象和租户权限。当前目录 81 个资源、114 个受控字段、2,103 个 MVC descriptor 都是清单规模，不是验收通过数量。

## 已核对的消费边界

| review_group | query / write | export / download 与剩余范围 |
| --- | --- | --- |
| order | `OrderReadOwnerV1`、`OrderWriteOwnerV1`、`OrderPriceCorrectionOwnerV1` 已消费当前字段源；Amount 依赖 UnitPrice。W678 补齐 credit-check 的余额/判断/消息派生披露，以及 consigned-check 的数量判断对象范围。 | 列表 CSV 与原报表分别使用 Export、Download，当前独立动作和字段决定见 [W34](../../evidence/WP02-03/order-read-owner/DELIVERY.md)。只覆盖该报告及 W678 的明确入口；产品主数据查入、其他追踪/未出货入口不能据此自动核销。 |
| inquiry | 当前人工受理、撤回同意的 `InquiryIntakePermissionSourceV1` / `InquiryConsentPermissionSourceV1` 有 Write 映射；来源完整性沿已采用版本。 | `InquiryPrivacyPermissionSourceV1` 明确区分 Export/Download；`InquiryServicePermissionSourceV1` 有服务身份 Export 映射。跨仓消费者仍引用原询盘验收及 [W645](../../evidence/W645-01/DELIVERY.md)，本轮没有重新运行 CRM。其他用途保留待核对。 |
| authoring | `FlowAuthoringOwnerV2`、`FormAuthoringOwnerV2`、`ApprovalBindingOwnerV2` 有各自 Read/Write 映射。 | 个人表单有独立消费边界，见下一行；没有从“名称相近”推导 Export/Download 通过。 |
| personal-form | `PersonalFormOwnerV2` 使用当前 PostgreSQL 权限、封存的 Title/DataJson 域及已发布动态字段策略；读取投影、局部保存、原结果回读和最终 WF_AUTH 门均已追踪。收件箱的标题搜索、总数及代理读取采用同一当前可见集。 | 已核对 Controller 无表单导出入口；附件 v2 通过 FORM/DRAFT/RETURNED/TASK/HISTORY 的实际上下文及当前 upload 字段读取权下载，收件箱/查询下载另核授权成员集。它是已采用的上下文读取合同，不是独立 Download 用途映射。调用链与实际回归见 [W679](../../evidence/W679-01/DELIVERY.md)。 |
| operations | `OperationsOwnerV1.AuthorityCoreAsync` 对整个注册域建立 Read/Write 决定；服务端动作与范围先验证，真实消费在各 `OperationsAccessV1.*` / Owner。 | 通用决定没有 Export/Download 映射。本清单只记录共享机制存在，不把所有派生 DTO、写回读或独立导出当作已逐项验证。优先核对有受控字段的实际输出路径。 |
| report-center | 当前 `owner-v1/stocks` / `owner-v1/transactions` 通过 ReportCenter Owner。旧 PostgreSQL 分析返回 `OWNER_READ_VERSION_REQUIRED`。 | CSV 当前动作门返回 `REPORT_SNAPSHOT_ADOPTION_UNPROVEN`，离线 `ExportCsv` 返回 `REPORT_EXPORT_CONTEXT_REQUIRED`，均未输出字节；不是“缺少导出字段检查却已放行”。新增快照能力仍属于未来业务采用。 |
| file-download | `file-content` 与 `plm-controlled-document` 当前采用 Operations 的 Read/Write、父对象与当前安全状态。 | [文件 v1 已采用合同](../../file-content-v1.md) 明确使用 query/add/edit；`DownloadAsync` / `BoundDownload` 沿当前 query。归为已采用 v1 读取模型，不能声称已有独立 Download 决定；是否需下一版用途模型由 WP02-03 与对应文件/文档 Owner 合同共同核销，不在盘点时自动增权或关闭合法下载。 |
| empty-field-domain | 当前封存域为空，本字段核销无可列项；仍需实际动作/对象权限。 | `mes-plan-achievement` 的 `ExportCsvAsync` 明确要求 export，再核 query；不能因为没有字段就说没有导出门，也不外推到其他空域。 |
| scope-only / consumer-review | CRM account/lead/opportunity、pur-pr 只有 scope；新资源默认保留实际消费者核对。 | 不把未注册域、未匹配属性或尚未核对的用途登记成产品缺陷或已通过。 |

共享源见 [CurrentFieldPermissionSourceReaderV1](../../../../CP6.Core/Services/Sys/CurrentFieldPermissionSourceReaderV1.cs)、[OperationsOwnerV1](../../../../CP6.Core/Services/Integration/OperationsOwnerV1.cs)。报表明确拒绝见 [ReportCenterService.Owner](../../../../CP6.Core/Services/Wms/ReportCenterService.Owner.cs)；空字段域独立导出见 [PlanAchievementService.Owner](../../../../CP6.Core/Services/Mes/PlanAchievementService.Owner.cs)。

## 继续核销的顺序

1. 继续 Operations 中有受控字段的实际 DTO、派生依赖、写回读及 Owner 决定；个人表单/附件本轮明确范围复用 W679。
2. 文件/受控文档 v1 和表单附件 v2 保留各自已采用读取合同；独立用途模型继续按原 WP02-03 与对应 Owner 核销，不能把版本不同当成权限豁免或自动关闭合法下载。静态检索结果本身不判定安全或缺陷。
3. 发现可重现的披露、越权或错误派生结果后，在 GitHub Issues 登记并定向修复。没有变更的已通过场景复用原证据，不因扩大导航清单重跑全部 PGT。

PGT-017 共同提交门、当前源单语句读取、MIN 后默认继续复用。Space 的专用字段/grant 组合仍由 WP13-04 在采用阶段验收。完整资源核销没有完成前，WP02-03 不登记 DONE。

## 再生成与证据

`eng/wp02-03/field-coverage/MetadataProbe` 引用指定本地 Main 程序集，观察 `HostBuilt` 后导出封存目录和 MVC descriptor，并在 `builder.Build()` 内中止；没有启动 HTTP、数据库初始化或后台任务。必须提供空的私有 content root，工具设置隔离的无效数据库端口，不加载既有环境配置。它仅用于已核对启动顺序的当前 Main，不是任意程序的安全沙箱。

```powershell
dotnet build eng/wp02-03/field-coverage/MetadataProbe/MetadataProbe.csproj --artifacts-path <private-build> -p:PackageRoot=<main-package>
dotnet <private-build>/bin/MetadataProbe/debug/MetadataProbe.dll <main-package> <empty-private-root> <metadata-output>
uv run --offline --python 3.13 python -B eng/wp02-03/field-coverage/index_coverage.py --metadata <metadata-output> --output docs/development/authorization/current-main
```

生成器中的分类对应上表的已知边界，不自动发现所有调用或认定未知路径通过。新资源默认进入待核对组。元数据来源见 [W678 交付](../../evidence/W678-01/DELIVERY.md)；W679 没有产品、目录或路由变更，复用同一采集输入，仅更新个人表单核销分类。[W679 交付](../../evidence/W679-01/DELIVERY.md) 记录本轮源码追踪、旧扫描修正与实际验证。
