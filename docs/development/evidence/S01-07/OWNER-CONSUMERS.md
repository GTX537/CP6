# QA Owner 与后续消费者接线

本记录是只读源码/原设计摘要。当前源码固定为 `e20740e37e20bc0210e0b32c5670c2bce962002a`，逐文件 Git 字节和工作字节 hash 见 [owner-consumers.json](owner-consumers.json)。没有调用真实 Owner、修改既有写入口或访问数据库。

原设计的真实责任边界来自已接受 QA-IPQC Recovery-R4 Systemic-R4：Main Quality 负责专业检验、Measurement 和 Quality decision；MES-EXEC 负责工序事实；MES-OUTPUT 负责产出事实；QA-POL 负责 policy/plan；PLM 保持工程评价与受控文档责任。原始 [ACCEPTANCE](../../../../eng/contracts/s01-07/inputs/protocol/ACCEPTANCE.md) 的正式设计接受并不证明运行接线。PLM-VER、PLM-DOC 的 RequiredFenced，以及 MES-EXEC proposed 协议身份保持。

| 边界 | 原生身份/范围 | 后续写入前需提供的证据 |
|---|---|---|
| QA-POL | QapRef / QapEvidence 使用原 Owner、Id、Version、Digest；QapUom 仅 Id、Version | 原 policy/plan、current heads、有效期/权限、完整 physical population 与选样依据；不能借 MES Owner 补字段 |
| MES-EXEC | ExecQaSource、ExecScope 含 portionId；Evidence 为 ref+digest | 原工单/工序/manifest、scope 与 current source head；适配器与 same-UoW adoption，保留 proposed 未证明范围 |
| MES-OUTPUT | OutQualitySource、OutRange、receiptSubject，unitRef 含 Owner | output 原坐标、receipt mapping、current successor、唯一 publication/schema/registration |
| Main Quality | Observation/Review/ResultSet/Decision | inspector/reviewer 分离、current eligibility、历史修订链、范围完整、权限与 Tenant/E 绑定、domain+Command+Audit 原子写入 |
| NCR Main / CP4 | 各自 Evidence/OwnerRef/OwnerBusinessRef/Scope，七个 required NullOnly | 原 source QA/currentness、Owner head、原审计/恢复事实；不能以 DTO 或假定的统一 Ref 替换真实 Owner |
| PLM | RawEvidenceHandoff 及 RequiredFenced engineering/document refs | 原始记录/hash、工程评价和受控文档读取协议、独立 Owner adoption |

当前实际消费者：

| 文件/入口 | 现场源码事实 | 后续接线任务边界 |
|---|---|---|
| [QualityInspectionService](../../../../CP6.Core/Services/Mes/QualityInspectionService.cs) | 使用 QualityInspectionDto、CP6Context、IMesSequenceService 和可选 IStockQcService；创建检验和项目，旧自动判定/不良记录链仍在此 | 引入新原生合同需要独立写入窗口、真实 Owner/currentness 与事务验证，本次未替换 |
| [DefectRecordService](../../../../CP6.Core/Services/Mes/DefectRecordService.cs) | 使用 DefectRecordDto、CP6Context、序列与 notifier，保存旧不良记录 | NCR 原生 source/decision/recovery 写入接线尚未验收 |
| [QualityInspectionController](../../../../CP6.WebApi/Controllers/Mes/QualityInspectionController.cs) | `/api/mes/inspections`，既有 Authorize 与 add/edit 权限，FromBody 仍为旧 DTO | HTTP strict raw-byte decoder、原生错误映射及权限回归未接入 |
| [DefectRecordController](../../../../CP6.WebApi/Controllers/Mes/DefectRecordController.cs) | `/api/mes/defects`，既有 Authorize 与 add/edit/del 权限，FromBody 仍为旧 DTO | 同上；本任务不改权限或旧业务行为 |
| [Program.cs](../../../../CP6.WebApi/Program.cs) | 原有两个 scoped service 注册继续有效 | 新 Owner/适配器注册没有实施 |
| [Web MES API](../../../../cp6.web/src/api/mes/mes.ts)、检验列表/录入视图 | 继续调用 inspections/defects 旧接口并使用旧 DTO | `decodeIpqc` 是可消费的合同库入口，未自动替换 Web HTTP 流程 |

后续数据库唯一目标 PostgreSQL；租户 E/TenantId、可信 schema 映射、权限、版本和事务原子性不能由本次纯类型结果推断。S01-12 可使用此合同子集与固定测试结果，但全部真实 QA 业务 AC、Owner 写入、库存/退货/贷项效果、数据库迁移和生产验收继续 NOT_RUN。
