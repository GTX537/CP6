# S01-12 合同证据与消费者接入

本目录汇总 S01-02..11 的既有合同验证，核对冻结输入、实测来源、当前消费字节和消费者缺口。它不执行业务验收，不把三仓主线合并、测试数量相加或类型库存在解释成 Owner 已采用。任务完成与否以 COORD 更新的 [roadmap](../../roadmap.json) 和远端 Main 回执为准。

固定输入为 `CP6-S01-CONTRACT-INPUTS/1.0.0`：33 个 sources、16 个 profiles、122 个固定向量、19 个唯一修订 case。profiles SHA-256 为 `2c221a04333cd5e0beba8d12a7aecbb08cbc80caa5faa74b22313045ce420e5e`；vectors 为 `b9a63a1512da2d2374e95d49d9f69c8636d06465f88a47862dc3604605d5be01`。原 input、expected、verification_level 和源 case 引用保留，原件没有原生 case ID 时继续为 null。

| 仓库 | 本次核对的远端 Main |
|---|---|
| Main | `47c9b12c796620effe41c62d16992ccd629ee949` |
| CRM | `1e4eea61c31d7547c3b3495d392eefbed99b7339` |
| Platform | `9259a45d39bae7e98ca804a74778a588c1ad4619` |

Main 准入资料基线 `a1824fec3d4824d45e27693f2e715b311ee5f665` 到本次基线只有协调文档变更。入口核对了 30 个来源 pin 的 Git 原始字节及 10 个最终交付候选的祖先关系，详见 [entry.json](entry.json)。祖先关系只证明提交包含；实测输入复用还要核对源码、测试、资源、配置、依赖及产物身份，不能由祖先关系代替。

## 阅读与复现

- [manifest.json](manifest.json)：来源、最终及历史运行、原执行身份、复用依据与限制。
- [vectors.json](vectors.json)：122 个 ID 的原输入/预期、profile/operation、验证层级、适用表面及实际结果定位。
- [revision-coverage.json](revision-coverage.json)：19 个原修订 case 的合同子断言及完整业务缺口；原 AC 指针不变。
- [consumers.json](consumers.json)：51 个固定源码 pin、9 项库能力、9 组实际消费者、六组下游交接和 56 个真实 WP 任务引用。这是有界源码盘点，不宣称穷尽所有动态调用及写者。
- [validation.json](validation.json)、[review.md](review.md)、[DELIVERY.md](DELIVERY.md)：本次比较检查、任务级审查及协调交付；旧运行没有改称本次运行。

在具备三个仓库固定 Git 对象的本地环境执行。Python 路径可按本机替换；参数指向含所需提交的仓库，不要求其工作分支切到对应版本。比较器从固定 Git 提交读取原件，不联网、不调用数据库、不构建产品，也不修改输入 expected。

```powershell
python -X utf8 eng/contracts/s01-12/verify_adoption.py --main-repo . --crm-repo ../CP6.CRM --platform-repo ./CP6.Platform --output adoption-check.json
python -X utf8 eng/contracts/s01-12/test_verify_adoption.py --main-repo . --crm-repo ../CP6.CRM --platform-repo ./CP6.Platform --output comparator-tests.json
python -X utf8 docs/development/tools/manage_roadmap.py
```

默认比较已提交的汇总和固定证据；`--write-manifests` 是显式再生三个机器清单的入口，只用于更新本任务产物后审查差异。不要对共享台账运行 `--write`。本任务实施和负例检查命令、实际退出码、发现/通过/失败/跳过数及源码哈希见验证记录。比较 PASS 表示证据连接与声明一致，不能补齐报告明示的证据缺口。

实际执行为初稿30项比较器正反检查通过，SPEC发现 NCR-FIELD 证据错接后保留失败回归并完成2项定向检查；没有把两次结果说成一次32项运行。最终完整证据比较通过，检查828个来源引用。修复将五字段的元数据与动态shape区分，122向量字节及其他18项修订保持不变，详见 [修复证据](implementation/SPEC-FIELD-FIX.md)。

## 逐向量比较的含义

| 组 | 固定数 | 实际适用表面 |
|---|---:|---|
| FIN-CREDIT 三符号域 | 25 | Main C#、TS、PG 既有证据 |
| FIN-INV Decimal18/Qty12 | 9 | Main C#、TS、PG 既有证据 |
| ATP StableId R1 | 22 | Main C#、TS、有界标量 Schema、PG 既有证据 |
| strict JSON / NullOnly / 结构相等 | 21 | Main C#、CRM C#/TS、Platform C# 各自实际范围 |
| NCR Main/CP4 | 20 | Main C#，FIELD_SUBASSERTION_ONLY |
| IPQC | 6 | Main TS，TYPE_BINDING_ONLY |
| LEAD OwnerId | 5 | CRM C#/TS required 字段投影 |
| INQ native locator | 4 | CRM C#/TS、Platform C#；不单独证明终态 revision |
| ERP registry / original bytes | 10 | Main C#/TS、Platform C# 各自路由及原字节层 |

只有 CREDIT25 + INV9 + ATP22 共 56 个词法向量适合三表面对照。QA、路由和字段绑定不伪装成三语言相等。不存在的表面标 NOT_APPLICABLE 并解释；适用但没有可定位实际证据的表面保留 EVIDENCE_GAP/NOT_RUN。旧测试的 PASS 只说明原断言通过；没有输出的实际标量不从 expected 填造。S01-05 仅保留套件级日志时，必须结合原测试源码和完整 ID 发现范围说明观测粒度。

矩阵中有352个适用的历史断言、502个不适用单元；这不是352个新测试。19个修订 case 为18个部分覆盖、1个未覆盖，完整 case 均保持 NOT_RUN。

PG 的词法拒绝可能发生在参数绑定前，不是每条都执行了数据库写入。系数/尺度的数值归一化、数据库保留尺度及 original wire 是不同断言；例如 `-0.000` 数值等于零，不授权改写原串。56 个向量不能证明完整 Rational/MAVG、币种、单位、政策尺度或业务效果。逐 ID 的 PG 来源采用后续57项回归中的56个固定断言，210项原运行保留其自身源码和范围；期间测试辅助文件的变化不能隐去。

## 原验证的复用边界

下表是原任务实际范围的历史结果，本次没有重新执行。不同套件有重叠，不能求和作为当前共同主线的组合测试。

| 任务 | 最终候选 / PR | 原结果 |
|---|---|---|
| S01-02 | `3890b4fdbda47c3762b9f51ea75b8c156540c69f` / Main184 | C#77、固定21 |
| S01-03 | `b24f76445ffdc9b4df0de77eb394a6aa47726a0c` / Main188 | C#205、固定25 |
| S01-04 | `bfa00a4c055bfcf4f6b7b7fe696f7a819af720c8` / Main190 | C#132、固定9 |
| S01-05 | `b1f5206d3a0ca1af6513c4984700823c81dea210` / Main185 | TS202、固定34、typecheck |
| S01-06 | `f406e4e515a7ce82d77db5307e467e7bcdbc25e4` / Main191 | C#60、TS81、固定22、typecheck |
| S01-07 | `d0a3ae93e23aeb48128b0519dbf585e64c508ccf` / Main197 | C#657、TS130、NCR20/IPQC6、typecheck |
| S01-08 | `912f6afa1d64cc0777721c03b5b39c0db91d60da` / CRM90 | 修正后 C#186、Web142（82任务+60既有）、importer6、固定30+UI49、typecheck |
| S01-09 | `753696c8333325d9faa5d80ea429ab8b9480e71c` / Platform58 | C#110、固定35；未发布 NuGet |
| S01-10 | `a304f9e728775109cfada83312dbeb23b2da4a98` / Main198 | PG210、后续重叠57、生命周期1、固定56 |
| S01-11 | `6dbc1a00cec1349c2ff7fdc2fe633d64ce2cf82d` / Main199 | 修复后 TS428+typecheck；C#417、设计5按原输入复用；固定10 |

S01-07 缺少原 C# DLL 哈希，保留 `ORIGINAL_DLL_SHA256_MISSING`；现在重建不能补造旧产物。NCR 与 IPQC 的表面也不相同。S01-11 最终结果采用 Issue196 修复后的 TS428 和 12 项 Buffer 所有权回归，旧 TS416/PR195 候选与失败证据仍保留历史身份。C#417 和设计5没有因后续提交而重跑。

CRM 使用 `CRM-INQ-TERMINAL-PROJECTION/20261013-R1`：native locator `740b840c697b60ebf51ceb0bd9a1898238814a49e36c2afe1d819c473b7df349`，overlay `bce20c5a3bc93826993be6c2ba7c9a440fffd467f746245e5665707d8eedf549`，effective `fd9a9444eac23225ecbf8ab91856dc7df2c0e4da53d9b765cbfccdeb2223ba2d`。90/99 公开 expiry/artifact/proof required-null，99 双时钟、30 三时钟。原 profile 的 ADOPTION_PENDING 不回写。Platform 只采用 native locator，不能冒领 effective decoder。封闭 payload 不增加版本字段，也不自动回退到 latest。

PG 正式映射为 `CP6-POSTGRESQL-EXACT-MAPPINGS/1.0.0` / ADOPTED_BY_COORD，文件 SHA `96ef3c9f22919e790f750773c2a4bdc675b7df5f145f7b9bfffeda6d19c1ec16`，消费语义 SHA `7bb24e1e36610996a46d13cace0a58e8032a3eb369e4dda235af5ec3c58e8752` 与原提案一致。20 个数值存储尺度扩大到 0..18（17 原生链、3 ValueLine 派生），其余46不变；Money.scale/trace.scale 仍为0..8、calculationScale18。PG18.6/Npgsql8.0.8 原运行仍是 PROPOSED 输入实测，不改称正式映射新跑，也不把210+57称为267个不同测试。

ATP 采用 `ATP-STABLEID-LEXICAL/20261011-R1`，完整保留 PLAN-IF-004/005。FIN-CREDIT、FIN-INV 与 ERP native Money 的合法域分别成立。ERP BYTES 只证明原字节保存，`NativeSchemaValidated=false`；F09、`EC_C06_200_StoredTerminal`、`EC_C06_202_StoredAdmission`、`WholeC06PublicRequest` 通用根未开放。真实接线前仍需审查 O(n²) 区间重叠检查的容量。

## 消费者交接

完整文件、源哈希、任务标题与台账状态快照在 [consumers.json](consumers.json)。S02/S03 等是切片名，后续实施责任引用现有 WP 任务，不新增虚构任务 ID，也不因这份交接授予开工窗口。

| 切片 | 可用能力 | 主要接入缺口与现有责任任务 |
|---|---|---|
| S02 | strict/null/equality、准确原件路由和 owned bytes | 同事务 Inbox/Outbox/Command/Audit、可信租户、当前权限、未知 commit 与原结果；WP02-02/03/07/08/09/10/11/27/28/29 |
| S03 | CRM 终态 R1、INQ/LEAD 库、49 UI 合同样例 | 真实 HTTP raw decode、IssuedAttempt/H01 原子图、五字段回执、proof/session/ETag/下载、实际终态页面与 PG；WP03-01/02/03/04/12/15，WP02-09/29 |
| S05 | CREDIT 三符号域、ERP current/frozen/重放 | 真实 CreditNote 写者、Owner/Store/issuer/current AR/GL/member/finite、独立 Draft/Confirm/Release 门、F09/C06；WP04-14/16/17/18/28/31/37，WP11-05/06/07/10/32 |
| S07 | ATP R1、标量 Schema、五列 PG fixture | 真实供给账、完整 ATP Owner、净供需去重、锁序、PR/WO 转单恢复；WP06-04/05/06/07/10/11，WP04-15 |
| S08 | NCR C#、IPQC TS、精确库存 profile | QA-POL/MES-EXEC/MES-OUTPUT/Main Quality/PLM 当前源头与权限；domain+Command+Audit；收货/质量/库存效果独立；WP09-01/02/05/06/07/08/09/10/11/12 |
| S09 | 精确算术、正式 PG 映射 | 所有 ORM/rawSQL/bulk/import 写者、财务权威和政策尺度、完整 MAVG/Rational、原子并发、逐租户迁移/恢复及历史损失核查；WP11 财务任务、WP02-02/10、WP06-06，准确清单见 JSON |

现有 `CreditControlService` 仍接收 decimal 并 round2；`StockFinBridge` 仍 round2 后调用凭证引擎；`ISupplyService` 仍返回 decimal。`CreditNoteService` 是查询入口，真正的一个旧写者是 `ErpBridgeHook.OnReturnConfirmedAsync`：它创建 CreditNote、写 Qty、没有赋 Amount，并保存旧模型；Qty/Amount 列仍是有限 decimal。新合同库没有替换它们，也不授权从已舍入值恢复丢失位数。

QA 仍走旧 Inspection/Defect DTO、服务和 HTTP 入口。CRM 的 `PublicInquiry.readPublic` 仍 `response.json()` 后做类型断言，既有 `LeadCodec` 是存储 codec，不是新 `LeadContractDecoder`。这些源码事实及四条限定目录/符号的搜索原始结果都有固定提交，不能扩大成“全库没有其他调用者”的结论。

后续数据库只面向 PostgreSQL。schema 不代替权限隔离；保留 TenantId、跨租户约束、可信映射、连接池/事务上下文及逐租户恢复。具体13张源表、42个授信字段、五个ATP列的进一步采用义务见 [S01-10 Owner 清单](../S01-10/OWNER-ADOPTION.md)。本任务没有数据库资源窗口，没有迁移、发包或部署。

## 状态边界

本次只新增证据比较与汇总；[read-only roadmap 检查](roadmap-check.json)通过。共享台账、生成进度页及四份项目状态文件由 COORD 在交付窗口统一同步。S01 12/12 只在本任务实际交付后表示切片任务完成；业务保持 **0/125**，247 完整运行场景 **NOT_RUN**，41 历史场景 **REFERENCE_ONLY**，原件盘点 **123/125**，ITER/REL 缺件与 WP05 **PROPOSED** 不变。

不运行、重跑或间接触发 GitHub Actions。分支初查没有任务运行需要取消；没有触发配置变更。每次推送/PR 前检查准确候选和新抓取的受保护 Main 工作流、事件过滤与本地依赖，保存记录；只推任务分支，不推标签。正常保护合并和远端 Main 核对由 COORD 完成。
