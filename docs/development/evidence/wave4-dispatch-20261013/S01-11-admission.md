# S01-11 准入核对：ERP-CREDIT current-v1 / frozen-R8

结论：可由 COORD 登记领取并按任务卡实施；未发现必需输入缺失。主线本地 HEAD/main/origin/main 均为 `e6ccc89b3e62c7ed2c3d4739757b6ae9f4490c47`；远端结论沿用 COORD 本轮确认。本报告只读检查，编译、测试、数据库、业务验收均 **NOT_RUN**。PostgreSQL 是唯一新增数据库目标。

前置 S01-02 已 DONE；实际叶子项目为 `eng/contracts/exact-values-tests/CP6.ExactValues.Tests.csproj`，target `net8.0`，引用 `CP6.Entity`。原派工的裸测试类名应配完整 project/filter 命令。

冻结包 `CP6-S01-CONTRACT-INPUTS/1.0.0`；机器报告保存完整 Git selector 路径、JSON pointer、Git blob/原 SHA/组合 SHA、补丁集合、准确 vector 对象和 revision case ID。source archive 只为没有组合派生的原生 Schema/冻结原件只读取证，未执行任何归档程序。

独占新增写路径：
- `CP6.Entity/Contracts/ErpCredit/`
- `cp6.web/src/contracts/erp-credit/`
- `eng/contracts/exact-values-tests/ErpCreditCodecTests.cs`
- `eng/contracts/s01-11/`
- `docs/development/evidence/S01-11/`

共用 csproj、package/lock/config、Wire 辅助类、`eng/contracts/s01/`、状态文件和工作流只读。现有服务/域模型/控制器仅核对；需要共享修改时先交 COORD 串行安排。新测试在自己文件中固定 hash/case ID，读取任务专属相对路径；共用叶子 `Inputs/dispatch.json` 仍是 wave2，不能用作本轮输入。

必需资产：

| Source ID | 选择 | 原 SHA-256 | 组合 SHA-256 |
|---|---|---|---|
| `composed-00` | composed | `7bc5b79a7dbc21f05b3d6ba33deb942226170113eb07ca3e1f711f2620830f28` | `ae952991bc09bff71e975f6c73806f66bb55a2b7ecd9913cec232963c3408eb4` |
| `composed-01` | composed | `2c1e10d8c3774f439651b993613b3cbc5456a2ebacd15c48b124f361f247db91` | `a7c7df954442b6a885031c66fb949e1d474cfe50d111d6a8c2b09ad96cf71138` |
| `composed-02` | composed | `4be3772c15e03d7f9d472815e55c1ab04d3dd7ae4b4245aa4bcb1ce3aae2fac7` | `880b6b33137a1db94acd53ead49cd4acd69e7a755fe842863ec539cffe6d8365` |
| `composed-03` | composed | `8b9eb3a9053f6b1cbace34bba66f420db678d744c002cc6634d5072006f44731` | `def871d7c0ef2383d7c439d8964ee88341b4249c8a2fedaf340373dfc8fc8d07` |
| `composed-04` | composed | `5c9d29e12fbd0426924c1edc09ba31a045deef890b31c047f4031ccd5b91f8a2` | `f861a57b4c6f68737fac44718e95185865ee610947bddb4ff523f177b683655f` |
| `composed-05` | composed | `ffee617f5e9117b9b2971d8938ce2cd43fb21b26b6b2bae6ec703872afb5062b` | `1f1e7846abfda9b23a334462393400939b26f32c021302f765e989c4c59d423d` |
| `composed-06` | composed | `d12052b9e2cc207c0dfef582b0b0b0309f58c9a261892dd5a1762be0eb955f87` | `f62431c873c950f7ee7a24bfc8e4a677ad4b06ff81dea1a09868e6cd2c86d7e5` |
| `composed-07` | composed | `932077285645b8238d9b1d85eee2f4b3ac7ff85d06e1086545a0e707caf5c8a4` | `bbdb87830d31661d3df9b36671e397164d758ae9cfc5eaa90972252e0edf8f4e` |
| `composed-08` | composed | `f7b99a4cf4a6ce61101a74f0ab1085ed2a5eace64616106f0cac72ac60e8467c` | `cfd89dc7cd7fe191192e175e8e222975539a414b2ad918f9a05b3d8f203a883c` |
| `composed-09` | composed | `771b25e2f89f7bb210d2c3ebf2eda1f61c8da80c2a1f82168437b8860d04e3b5` | `14617160cf7d914d2d2f29d1c56686c3e8f64a63bf5366d4017b49926d8de3fe` |
| `composed-10` | composed / original | `c5a95450610038cf329fd08961eb5d6f37194b19f30dadf88fd42fe4ee378cdc` | `64e7bdda097765041f464eede58a09b242c9e9e3971128d407816597f3a89309` |
| `composed-11` | composed / original | `319e5002c3312b27c89fe17c042b7e73ae87451c40d4baffb425f9c68d2cfe97` | `9fe0b5d24233359490a3405739d394118561f77061af0df884889ccfaad720b2` |
| `composed-12` | composed / original | `533967724512459f157c2218b437b58945b0cb1004073ae52904d80c5bfa6228` | `a3581e44235bf8cae1041c267c949138929679fdc1655f16ede3b3d0ff71986d` |
| `erp-registry-current` | composed | `c69762861155ed95af608a9d4867e900f8287eccdad3c38bd9c4c6b50f2ee857` | `564706ce0247d84dc8056002d540f17eccdcab7c5b5e4b123c09121ad407fbda` |
| `erp-registry-frozen` | original | `62effa1e0542784a5fa936d11961465db4da522ed4d28fc9c9bb5351cf40b6f1` | `—` |

准确包向量 10 条：`VERSION-001`, `VERSION-002`, `VERSION-003`, `VERSION-004`, `VERSION-005`, `VERSION-006`, `VERSION-007`, `VERSION-008`, `BYTES-001`, `BYTES-002`。

修订验收仅覆盖合同子断言：
- `DR-COM-CREDIT-CURRENT-BODY-MONEY-FIELD::AC-W5-COM-07-R`
- `DR-COM-CREDIT-FROZEN-R8-CODEC-CURRENT-ADAPTATION::AC-W5-COM-06-R`
- `DR-COM-CREDIT-OPENAPI-FROZEN-REPLAY-MAPPING::AC-W5-COM-08-R`

三文件 codec bundle 的另外两份原生 Schema 必需：WMS-RMA `b324a9b62d888f268a514de4266ecff70783e80d30c39eb5b82e7fffaf62af3a`；FIN-AR `8a7864b91e2bd19f9102abcb9eea756d7f6df33ad84e7b852f24a9bd669c56ca`，已现场核 SHA。

WMS Money 是封闭 `{value,currency}`；SignedDecimal 最多 30 位整数 / 8 位小数，FIN-AR D8 最多 20 位整数 / 8 位小数。原生 `-0`、`1.0` 等合法输入必须保持，不套用 FIN-CREDIT canonical 规则。

实施约束与缺口：
- Separate current-v1 and frozen-R8 namespace, TypeRegistry SHA and schema bundle; exact route only, no latest/fallback.
- composed-00..09 contain dependent revised fixture identities: retain body digest, byteLength, handle, Store/receipt metadata chains together. No issuer or Owner proof invented.
- current-v1 three-file codec bundle = exact WMS-RMA + exact FIN-AR + registry 564706ce...; frozen-R8 same two native schemas + registry 62effa1e....
- Current Money strictly {value,currency}; amount alias and extra properties reject. Native decimal strings, allowed negative zero/trailing zeros and separate WMS/FIN-AR limits preserved.
- Current result semantics: APPLIED requires resultRef; REJECTED forbids resultRef; NO_CHANGE may carry proven resultRef. COMPLETED/NO_CHANGE transport alone does not establish FOUND/release. Frozen-R8 retains its original validation identity.
- Frozen command/receipt original bytes remain immutable. C01/C02 replay HTTP200 returns original saved COMMITTED body from HTTP201; never rename to COMMITTED_REPLAY, re-redact/rebuild or reapply writes.
- Insufficient Owner/current mapping proof remains READ_ONLY_UNPROVEN/MANUAL_RECONCILIATION. BYTES vectors are explicitly constructed unit inputs, not legitimate Owner command or replay proof.
- Actual target is net8.0. Pinned generated current C# calls JsonElement.DeepEquals; local net8 reference XML has no such member, while net10 does. Adopt in task-owned wrapper via CP6.Entity.Contracts.Wire.JsonSemanticEquality.AreEqual and StrictJson; no SDK/dependency upgrades or shared Wire edits. Preserve derivation map.
- Current registry EC_C06_200_StoredTerminal, EC_C06_202_StoredAdmission and WholeC06PublicRequest reference F09-LOCAL-TYPES. Existing generated codec SchemaIdentity allows exactly three files and its published factories do not expose these C06 roots. Do not advertise arbitrary registry root decoding; if such root is required, stop that expansion until exact F09 closure and route are pinned by COORD.
- 10 frozen vectors cover registry routes/original-byte preservation only; add native Money full-object positives/negatives, exact schema pin, current/frozen semantic distinction and guarded mapping tests in task directory.

准备交付时执行（本轮未运行）：
```powershell
dotnet test eng/contracts/exact-values-tests/CP6.ExactValues.Tests.csproj --filter FullyQualifiedName~ErpCreditCodecTests
npm --prefix cp6.web test -- src/contracts/erp-credit/creditCodec.test.ts
```

验证边界：只证明实现覆盖到的合同/codec；源静态 PASS、构造向量、DTO 编译和本地单元测试均不等于真实 Owner 已接线。QA 业务、贷项/退货/应收效果、存储迁移和生产验收继续 NOT_RUN。原始完整义务直接按 S01 任务计划及各 profile 的 original_acceptance_refs 读取。

本轮无 Git 写操作、构建、测试、数据库、网络发布、Actions 触发变更或取消项。
