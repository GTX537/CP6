# 当前 Main 字段与用途核销入口

W678-01 从整卡复核接续建立此索引。W679 至 W681 核对个人表单、库存金额和关联名称来源，W682 补齐业务伙伴档案及 CSV 权限，W683 核对库存、入库、收货和出库单价，W684 补齐出库开票的原生订单行计价，W685 核对工序费率及财务历史读取。WP02-03 状态以 `roadmap.json` 为准。[W677 整卡复核](../../evidence/W677-01/DELIVERY.md)

[resources.jsonl](resources.jsonl) 保存封存目录内每个资源的完整受控字段域、scope、四用途结论、候选源码和路由引用。[routes.jsonl](routes.jsonl) 保存实际 MVC descriptor 的路由、方法及直接声明的属性。`permission_attribute_route_ids` 只连接明确的菜单属性；Controller 通过服务调用 Owner 时可能没有该属性，空列表不意味着未授权。`lexical_source_candidates` 是字面量/Operations 常量检索的首个位置，包含定义和引用，不是调用图。

`fields: []` 表示明确注册了空字段域；`fields: null` 表示只有范围注册，不能互换。未注册受控字段不等于整个 DTO 已批准或无需动作、对象和租户权限。当前目录 82 个资源、114 个受控字段、2,103 个 MVC descriptor 都是清单规模，不是验收通过数量。

## 已核对的消费边界

| review_group | query / write | export / download 与剩余范围 |
| --- | --- | --- |
| order | `OrderReadOwnerV1`、`OrderWriteOwnerV1`、`OrderPriceCorrectionOwnerV1` 已消费当前字段源；Amount 依赖 UnitPrice。W678 补齐 credit-check 的余额/判断/消息派生披露，以及 consigned-check 的数量判断对象范围。 | 列表 CSV 与原报表分别使用 Export、Download，当前独立动作和字段决定见 [W34](../../evidence/WP02-03/order-read-owner/DELIVERY.md)。只覆盖该报告及 W678 的明确入口；产品主数据查入、其他追踪/未出货入口不能据此自动核销。 |
| inquiry | 当前人工受理、撤回同意的 `InquiryIntakePermissionSourceV1` / `InquiryConsentPermissionSourceV1` 有 Write 映射；来源完整性沿已采用版本。 | `InquiryPrivacyPermissionSourceV1` 明确区分 Export/Download；`InquiryServicePermissionSourceV1` 有服务身份 Export 映射。跨仓消费者仍引用原询盘验收及 [W645](../../evidence/W645-01/DELIVERY.md)，本轮没有重新运行 CRM。其他用途保留待核对。 |
| authoring | `FlowAuthoringOwnerV2`、`FormAuthoringOwnerV2`、`ApprovalBindingOwnerV2` 有各自 Read/Write 映射。 | 个人表单有独立消费边界，见下一行；没有从“名称相近”推导 Export/Download 通过。 |
| personal-form | `PersonalFormOwnerV2` 使用当前 PostgreSQL 权限、封存的 Title/DataJson 域及已发布动态字段策略；读取投影、局部保存、原结果回读和最终 WF_AUTH 门均已追踪。收件箱的标题搜索、总数及代理读取采用同一当前可见集。 | 已核对 Controller 无表单导出入口；附件 v2 通过 FORM/DRAFT/RETURNED/TASK/HISTORY 的实际上下文及当前 upload 字段读取权下载，收件箱/查询下载另核授权成员集。它是已采用的上下文读取合同，不是独立 Download 用途映射。调用链与实际回归见 [W679](../../evidence/W679-01/DELIVERY.md)。 |
| operations | `OperationsOwnerV1.AuthorityCoreAsync` 对整个注册域建立 Read/Write 决定，实际输出由各 `OperationsAccessV1.*` / Owner 消费。 | 业务伙伴 CSV 有明确的 Export 映射。其他资源的独立用途依各自消费入口核对。 |
| inventory-observation | WMS 看板金额要求自身 Amount 与来源 stock.UnitPrice；效期、呆滞库存参考值与来源单价相交，隐藏/缺价不伪造零值；VMI 历史金额、费率按已采用合同分别要求动作与字段权。实际范围、聚合前过滤及最终源权限见 [W680](../../evidence/W680-01/DELIVERY.md)。 | 已核对的四类 Controller 没有导出/下载入口；看板、呆滞库存只有读取，旧效期处置与 VMI 计算/确认在 PostgreSQL 明确拒绝。此分类仅核销这些消费者的注册字段，不代表正式处置、计费/Finance、其他主数据来源资源或全部业务卡已采用。 |
| report-center | 当前 `owner-v1/stocks` / `owner-v1/transactions` 通过 ReportCenter Owner。旧 PostgreSQL 分析返回 `OWNER_READ_VERSION_REQUIRED`。 | CSV 当前动作门返回 `REPORT_SNAPSHOT_ADOPTION_UNPROVEN`，离线 `ExportCsv` 返回 `REPORT_EXPORT_CONTEXT_REQUIRED`，均未输出字节；不是“缺少导出字段检查却已放行”。新增快照能力仍属于未来业务采用。 |
| file-download | `file-content` 与 `plm-controlled-document` 当前采用 Operations 的 Read/Write、父对象与当前安全状态。 | [文件 v1 已采用合同](../../file-content-v1.md) 明确使用 query/add/edit；`DownloadAsync` / `BoundDownload` 沿当前 query。归为已采用 v1 读取模型，不能声称已有独立 Download 决定；是否需下一版用途模型由 WP02-03 与对应文件/文档 Owner 合同共同核销，不在盘点时自动增权或关闭合法下载。 |
| empty-field-domain | 当前封存域为空，本字段核销无可列项；仍需实际动作/对象权限。 | `mes-plan-achievement` 的 `ExportCsvAsync` 明确要求 export，再核 query；不能因为没有字段就说没有导出门，也不外推到其他空域。 |
| scope-only / consumer-review | CRM account/lead/opportunity、pur-pr 只有 scope；新资源默认保留实际消费者核对。 | 不把未注册域、未匹配属性或尚未核对的用途登记成产品缺陷或已通过。 |

共享源见 [CurrentFieldPermissionSourceReaderV1](../../../../CP6.Core/Services/Sys/CurrentFieldPermissionSourceReaderV1.cs)、[OperationsOwnerV1](../../../../CP6.Core/Services/Integration/OperationsOwnerV1.cs)。报表明确拒绝见 [ReportCenterService.Owner](../../../../CP6.Core/Services/Wms/ReportCenterService.Owner.cs)；空字段域独立导出见 [PlanAchievementService.Owner](../../../../CP6.Core/Services/Mes/PlanAchievementService.Owner.cs)。

## 继续核销的顺序

W681 核对了仓库/库位当前 Owner 的名称、地址、管理者、备注、条码投影与名称/草稿写入门，以及业务伙伴身份 Owner 的 BpName、历史原件和内部 Hold 投影。仓库/库位名称操作结果只返回标识、效果和版本，私有审计不披露前后名称；草稿读取继续走当前字段投影，尚未采用的条码/管理者来源明确拒绝。出库路由的仓库候选调用同一当前 Owner，不按名称搜索或借候选输出隐藏名。这些限定路径已有行为证据，其他消费者仍保留待核对，三个资源继续留在 operations 组。

看板的 WarehouseName 现在同时要求当前仓库 query、字段读取及 MasterWarehouses 真实范围；库龄客户分组名称要求自身 CustomerName 与业务伙伴 query/BpName/真实范围相交。只有明确无 query 权或字段不可读才隐藏名称，来源故障继续失败；名称来源加入同一事务的最终权限门。隐藏名称不会删除本来可见的库存分组，仍返回仓库码或客户码，金额和数量沿原合同。客户分组的 CustomerNameReadable 反映两端字段/动作交集，不证明每个来源对象均可见。没有把业务码显示关联升级成 UUID 身份或交易资格。见 [W681 交付](../../evidence/W681-01/DELIVERY.md)及 [#1206](https://github.com/GTX537/CP6/issues/1206)。

W682 已修复业务伙伴旧入口权限。现有 `query` 继续授权最小身份查询和范围内查重。完整详情和列表另需 `profile-query`，CSV 再需 `export` 和独立 Export 字段决定。BpName 无权读取时详情和列表返回 null，CSV 名称列为空，名称筛选与排序请求拒绝。保存后只有当前档案查询权才能回读，同事务故障会回滚写入。两项新动作由迁移和种子登记，角色需显式分配。[W682 交付](../../evidence/W682-01/DELIVERY.md) · [#1207](https://github.com/GTX537/CP6/issues/1207)

库存、入库单、收货和出库单的详情读取采用对应当前动作、对象范围及 UnitPrice 决定。出库分配把实际库存单价复制到预留流水和缺价明细前，要求来源库存查询、对应实际库存行及单价读取许可，最终权限门包含来源。四个直接控制器没有独立导出或下载入口。入库计划为收货提供经过核对的来源标识和数量，收货提交价按收货字段写权处理。[W683 交付](../../evidence/W683-01/DELIVERY.md)

PostgreSQL 出库开票在扣减库存前读取授权的当前订单价，按实际出库行绑定的订单及明细 UUID 取价。绑定不可用或未绑定的售价存在歧义时，拒绝请求并保留现有预留。合法意图随发货提交，后续财务执行和重试沿用原意图。[W684 交付](../../evidence/W684-01/DELIVERY.md)

工序费率的当前查询、按日解析和写入由同一费率 Owner 控制。财务历史成本单列表、明细和 FG 单位成本需要已登记的 `fin-cost:view`、当前费率查询及两个费率字段读取权限。限定费率范围按原工作中心、有效日和保存费率过滤整张成本单，避免通过合计推算超范围费率。允许全租户费率查看的财务角色可以读本租户旧快照。角色查看权限由租户管理员显式分配。实时 PostgreSQL 成本归集仍明确拒绝缺少 Finance Owner 的请求。[W685 交付](../../evidence/W685-01/DELIVERY.md) · [#1214](https://github.com/GTX537/CP6/issues/1214)

1. 继续 Operations 的其他派生 DTO、写回读及独立用途。
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

生成器中的分类对应上表已核对的消费入口，新资源默认进入待核对组。W682 从当前 Main 候选程序集在 HostBuilt 重新采集，七个旧业务伙伴入口增加 Owner 结果处理属性，资源字段域和路由集合保持原结构。此前采集及验证范围见 [W678](../../evidence/W678-01/DELIVERY.md)、[W679](../../evidence/W679-01/DELIVERY.md)、[W680](../../evidence/W680-01/DELIVERY.md)、[W681](../../evidence/W681-01/DELIVERY.md)。
