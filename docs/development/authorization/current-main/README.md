# 当前 Main 字段与用途核销入口

W678-01 从整卡复核接续建立此索引。W679 至 W681 核对个人表单、库存金额和关联名称来源，W682 补齐业务伙伴档案及 CSV 权限，W683 核对库存、入库、收货和出库单价，W684 补齐出库开票的原生订单行计价，W685 核对工序费率及财务历史读取，W686 接入手工实时归集，W687 接入 MES 完工后的授权归集和结转，W688 补齐重启恢复，W689 固定现行计算政策和费率的用版，W690 接通账套、成本中心版本、独立费率复核发布和归集用版，W691 接通专业资格登记、工序中心复核与后继发布，W692 接通工序中心同事务采用及原发布回读，W693 接通标准依据批准及实际归集，W694 接通独立标准单据与命令回查，W695 接通制造成本维护。WP02-03 状态以 `roadmap.json` 为准。[W677 整卡复核](../../evidence/W677-01/DELIVERY.md)

[resources.jsonl](resources.jsonl) 保存封存目录内每个资源的完整受控字段域、scope、四用途结论、候选源码和路由引用。[routes.jsonl](routes.jsonl) 保存实际 MVC descriptor 的路由、方法及直接声明的属性。`permission_attribute_route_ids` 只连接明确的菜单属性；Controller 通过服务调用 Owner 时可能没有该属性，空列表不意味着未授权。`lexical_source_candidates` 是字面量/Operations 常量检索的首个位置，包含定义和引用，不是调用图。

`fields: []` 表示明确注册了空字段域；`fields: null` 表示只有范围注册，不能互换。未注册受控字段不等于整个 DTO 已批准或无需动作、对象和租户权限。当前目录 82 个资源、114 个受控字段、2,128 个 MVC descriptor 都是清单规模，不是验收通过数量。

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

工序费率的当前查询、按日解析和写入由同一费率 Owner 控制。财务历史成本单列表、明细和 FG 单位成本需要已登记的 `fin-cost:view`、当前费率查询及两个费率字段读取权限。限定费率范围按原工作中心、有效日和保存费率过滤整张成本单，避免通过合计推算超范围费率。允许全租户费率查看的财务角色可以读本租户旧快照。角色查看权限由租户管理员显式分配。[W685 交付](../../evidence/W685-01/DELIVERY.md) · [#1214](https://github.com/GTX537/CP6/issues/1214)

PostgreSQL 手工归集使用 `fin-cost:collect` 与工单、实际产品、费率来源权限。Finance Owner 按基准日选择应采用的费率，核对该记录的字段与范围，逐行按 PostgreSQL 数值语义计价，再保存成本单与采番。重归集沿用成本单身份并更新未结转明细。[W686 交付](../../evidence/W686-01/DELIVERY.md)

MES 完工后，材料反冲成功才进入 Finance Owner。系统核对原完工事实、当前会话、`fin-cost:collect`、`fin-cost:settle` 和适用的来源权限。归集、结转、采番、凭证和财务成功事件同事务提交。已知财务失败保留原完工与反冲结果及绑定原完工的错误事件，财务人员处理错误后使用授权的手工入口归集和结转。后台把到期的旧式 MES 财务事件转为终态，保留原事件身份及尝试次数。[W687 交付](../../evidence/W687-01/DELIVERY.md)

新完工事务同步写入以完工 UUID 标识的待处理记录。材料反冲与该材料的恢复进度同事务提交。重启时 Worker 按租户读取原完工，重新检查原访问签发、当前权限和来源范围。原会话失效或授权拒绝时保留已完成的制造结果，待处理记录转终态，可由财务人员通过授权入口处理。归集和结转与原事件的终态同事务提交，多实例检查原事件后执行。 [W688 交付](../../evidence/W688-01/DELIVERY.md)

费率按日解析和归集共用同一选版规则。同一工作中心先确定应生效的最新费率，再检查该记录的对象和字段权限。归集追加不可变版本，保存现行计算政策版本、费率及工作中心身份、行版本、有效区间和汇总金额。已采用费率的历史读取以精确来源及当前访问资格为准。结转引用保存的归集版本，关联实际凭证。R2 专业政策、费率发布和标准依据采用沿 WP11-39 及其直接前置推进。 [W689 交付](../../evidence/W689-01/DELIVERY.md)

账套和成本中心候选使用 `fin-cost:view`、当前租户及操作者账套成员关系，写请求核对操作者与精确成本中心版本凭据。制订权限与源 `mes-process-cost-rate` 写权相交。独立复核及发布要求各自动作、现行费率读权及有效专业资格。工单绑定账套后按批准会计日选择该账套内实际工序的发布版，当前来源无权时拒绝归集。绑定账套的当前成本读取仍受账套成员及费率来源限制。账套级过账及开票接入沿后续任务推进。 [W690 交付](../../evidence/W690-01/DELIVERY.md)

财务专业资格签发、工序成本中心登记、复核、发布各有当前动作权并核账套。资格必须由其他实际操作者签发，成本中心复核独立于制订。费率和中心专业写入使用本人会话。精确工序变动后的中心停止新消费，提交准确后继及费率可恢复归集。 [W691 交付](../../evidence/W691-01/DELIVERY.md) · [#1224](https://github.com/GTX537/CP6/issues/1224)

费率发布和成本归集在同一 PostgreSQL 事务内采用精确工序中心版本。采用逐人工或制造费用行分批，全部批次及消费者结果共同提交。原费率发布重放继续核对当前账套、发布资格和来源读权，已完成的采用记录随原结果保留。 [W692 交付](../../evidence/W692-01/DELIVERY.md) · [#1226](https://github.com/GTX537/CP6/issues/1226)

标准成本依据的登记、独立复核、启用、撤销、读取及采用各有当前动作权。实际归集核对账套、整单范围、当前政策和批准版本的历史费率权限。历史比较读取原归集版本，账套成员资格撤销后拒绝读取。 [W693 交付](../../evidence/W693-01/DELIVERY.md)

独立标准成本单据创建、预览和冻结核对账套、归集及标准采用动作。回查和历史版本要求当前读权及原来源范围。提交未知使用原命令回查，同键精确重放保留原回执。 [W694 交付](../../evidence/W694-01/DELIVERY.md)

制造成本维护按账套、工单、产品、费率字段权限和原制订人范围读取选择和版本。标准命令回查使用同一身份及账套，费率发布沿用原预览。 [W695 交付](../../evidence/W695-01/DELIVERY.md)

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
