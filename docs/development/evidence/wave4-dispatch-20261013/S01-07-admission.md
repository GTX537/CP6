# S01-07 准入核对：原生 Ref/Scope 与 NCR/IPQC 类型

结论：可由 COORD 登记领取并按任务卡实施；未发现必需输入缺失。主线本地 HEAD/main/origin/main 均为 `e6ccc89b3e62c7ed2c3d4739757b6ae9f4490c47`；远端结论沿用 COORD 本轮确认。本报告只读检查，编译、测试、数据库、业务验收均 **NOT_RUN**。PostgreSQL 是唯一新增数据库目标。

前置 S01-02 已 DONE；实际叶子项目为 `eng/contracts/exact-values-tests/CP6.ExactValues.Tests.csproj`，target `net8.0`，引用 `CP6.Entity`。原派工的裸测试类名应配完整 project/filter 命令。

冻结包 `CP6-S01-CONTRACT-INPUTS/1.0.0`；机器报告保存完整 Git selector 路径、JSON pointer、Git blob/原 SHA/组合 SHA、补丁集合、准确 vector 对象和 revision case ID。source archive 只为没有组合派生的原生 Schema/冻结原件只读取证，未执行任何归档程序。

独占新增写路径：
- `CP6.Entity/Contracts/Quality/`
- `cp6.web/src/contracts/quality/`
- `eng/contracts/exact-values-tests/QualityContractTests.cs`
- `eng/contracts/s01-07/`
- `docs/development/evidence/S01-07/`

共用 csproj、package/lock/config、Wire 辅助类、`eng/contracts/s01/`、状态文件和工作流只读。现有服务/域模型/控制器仅核对；需要共享修改时先交 COORD 串行安排。新测试在自己文件中固定 hash/case ID，读取任务专属相对路径；共用叶子 `Inputs/dispatch.json` 仍是 wave2，不能用作本轮输入。

必需资产：

| Source ID | 选择 | 原 SHA-256 | 组合 SHA-256 |
|---|---|---|---|
| `composed-15` | composed | `9f975e897bb9e2d05b95256e442bead2177b1ca3d13e06d01cddd3cf0b685352` | `f6485d343577c154a41802b05a2918da76c3d0dba1bb3f7594ffdadda78e76d4` |
| `composed-16` | composed | `103ff257cd4ca8c7da153e2d638fd8d6a61f5bca542dd1a42d336d8c53dbd941` | `810063cb888d2cf231bf387bb19cc4a72d67a799b04203a2ac35687a1d00b88e` |
| `composed-17` | composed | `4bfc1d1febd24c7fd52130ae1a3c8a1d580d088c71e669ae5b17d8a740a310d2` | `ebab68099342e796a43ee2eb0b60772013f28b16c52f89e456b331aaff208d10` |
| `composed-19` | composed | `859768bc391d71dfcdf0671e66cdb61bb41c40d2c38b80bd539187205a85950a` | `fa551a7f8dbce66ae17b72671f3fbc01406ea51e0e111cc396c4ed683f871b6c` |

准确包向量 26 条：`NCR-NULL-001`, `NCR-NULL-002`, `NCR-NULL-003`, `NCR-NULL-004`, `NCR-NULL-005`, `NCR-NULL-006`, `NCR-NULL-007`, `NCR-NULL-008`, `NCR-NULL-009`, `NCR-NULL-010`, `NCR-NULL-011`, `NCR-NULL-012`, `NCR-NULL-013`, `NCR-NULL-014`, `NCR-NULL-015`, `NCR-NULL-016`, `NCR-NULL-017`, `NCR-NULL-018`, `NCR-NULL-019`, `NCR-NULL-020`, `IPQC-TYPE-001`, `IPQC-TYPE-002`, `IPQC-TYPE-003`, `IPQC-TYPE-004`, `IPQC-TYPE-005`, `IPQC-TYPE-006`。

修订验收仅覆盖合同子断言：
- `DR-NCR-FIELD-01::NCR-FIELD-01-NEG`
- `DR-NCR-FIELD-01::NCR-FIELD-01-POS`
- `DR-NCR-TYPE-01::NCR-TYPE-01-NEG`
- `DR-NCR-TYPE-01::NCR-TYPE-01-POS`
- `DR-OPS-IF-015::OPS-IF-015-NEG`
- `DR-OPS-IF-015::OPS-IF-015-POS`

NCR R12 的 `design/NCR-R6-WIRE.json` 递归文件/fragment 引用闭集共 10 文件，全部现场核 SHA，通过定位，无未解析引用（完整 member/path/hash 在 JSON）。保留 CP4、Owner Addenda、Source QA、IPQC R4、IQC R9、WMS-RMA、Finance R10 和 MES 原生身份；不以统一 Evidence/Scope 代替原合同。

七个 NullOnly 是 Main 四字段 + CP4 三字段，其中包含两个 ObservationInput 字段。五个 nullable 元数据为：MainResult priorHeadRef=false；InitialSourceOptionQuery.sourceKind/keyword/cursor=true；InitialSourceOptionPage.nextCursor=true。它们保持 required，editable=false。

实施约束与缺口：
- Pin exact native Schema/type identities and resolve complete dependency closure of every adopted root; no universal Ref or guessed cross-domain coercion.
- Seven exact required const-null fields: four in NCR Main declarations and three in CP4 (including two ObservationInput fields). Test explicit null roundtrip; reject missing, numbers, object and string null.
- Five nullable metadata corrections retain required and editable=false; valid nullable values accepted, missing rejected.
- QAP Evidence is flat owner/id/version/digest; ExecEvidence and OutEvidence are ref+digest. QapUom=id/version; WMS Uom=code/version; never borrow owner or collapse schemas.
- R4FrozenPayload.registrationRef and FullCount units follow composed-19 precise declared types. Native registry /types covers the QAP scalars/refs; do not invent /types/R4FrozenPayload or /types/R4QapFullCountPlanContent (those TS declarations are separately pinned).
- Expose only declared native payload/schema routes; reject cross-event and wrong version/unit identity using complete native fixtures. Reuse StrictJson duplicate decoded-name boundary.
- 26 frozen vectors are 20 field projections and 6 type bindings; add positive/negative full-root fixtures for exposed roots with independent source expectations. No QA Owner source/currentness/write acceptance.

准备交付时执行（本轮未运行）：
```powershell
dotnet test eng/contracts/exact-values-tests/CP6.ExactValues.Tests.csproj --filter FullyQualifiedName~QualityContractTests
npm --prefix cp6.web test -- src/contracts/quality/ipqc.test.ts
```

验证边界：只证明实现覆盖到的合同/codec；源静态 PASS、构造向量、DTO 编译和本地单元测试均不等于真实 Owner 已接线。QA 业务、贷项/退货/应收效果、存储迁移和生产验收继续 NOT_RUN。原始完整义务直接按 S01 任务计划及各 profile 的 original_acceptance_refs 读取。

本轮无 Git 写操作、构建、测试、数据库、网络发布、Actions 触发变更或取消项。
