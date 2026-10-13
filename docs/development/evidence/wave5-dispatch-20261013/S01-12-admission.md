# S01-12 准入建议（只读准备）

建议由 COORD 登记 READY / CONFIRMED，并分配独立 Main 分支/worktree。S01-02..11 全部已交付，候选均为给定当前主线的祖先；没有待交付依赖造成的硬阻塞。本报告不是领取、台账改动或 S01-12 执行。当前台账仍 PLANNED / NOT_CHECKED / assignment=null。

依据为 Main AGENTS、S01 计划第180–189行、09并行规则、冻结输入包、wave2/3/4交付及当前源码。本次核对本地 origin/main 与父协调提供的已确认远端 SHA，读取原远端回执并检查候选祖先关系；没有重新联网核验远端。配套 JSON 保存完整回执路径、历史远端 SHA、准确 Git blob/SHA-256 和向量 ID。

| 仓库 | 本次固定主线 |
|---|---|
| Main | `a1824fec3d4824d45e27693f2e715b311ee5f665` |
| CRM | `1e4eea61c31d7547c3b3495d392eefbed99b7339` |
| Platform | `9259a45d39bae7e98ca804a74778a588c1ad4619` |

## 写入窗口

COORD已准备A窗口：分支 `codex/s01-12-adoption-20261013`、worktree `D:/CP6-worktrees/s01-12-20261013`、基线 `a1824fec3d4824d45e27693f2e715b311ee5f665`。此为协调消息中的准备信息；领取/门状态仍由COORD登记。本窗口不新增PG数据库资源，先复用原证据。

- 执行对话：`docs/development/evidence/s01-adoption/**`；需要时仅在 `eng/contracts/s01-12/**` 新增 compare-only 核验器或有界测试 harness。
- 所有 Main/CRM/Platform 生产源码、已有合同/输入/生成器/测试/证据、依赖配置、EF模型/迁移/共享写入口只读。发现产品缺陷交 COORD 安排对应 Issue/修复窗口。
- COORD 在交付窗口统一编辑 roadmap、生成视图和 `PROJECT_STATE.md`、`05-Completed.md`、`06-Todo.md`、`CHANGELOG-AI.md`；执行者不覆盖这些共享文件。
- 不接数据库、不迁移、不发包、不部署，不运行或重跑 Actions。每次 push/PR 前检查候选与受保护主线的事件过滤及依赖。下一任务分支从届时最新已确认 Main 创建，当前根工作区的未跟踪资料保留。

## 可执行要求

1. 形成 `manifest.json` / `README.md`：三仓源码及交付提交、profile/Schema/原件/完整补丁/组合派生摘要、源执行输入与产物身份、语言/SDK/PG版本、原运行时间/命令/退出码、复用依据和缺口。区分原执行 HEAD、未提交运行字节与后续提交绑定；区分 Git blob 字节和经过换行过滤的工作字节。
2. 形成122条逐ID对照表：按 profile + operation + 原 input/expected + verification_level + 实测 test/result 定位。检查重复/漏项、改变预期、错误 profile 与未解释差异。不存在的语言或 PG 表面写 NOT_APPLICABLE 并解释；适用却无可定位实测则 NOT_RUN / EVIDENCE_GAP。旧 PASS 仅能证明原断言已执行，未捕获的实际标量不能补造。
3. 核对实际合同入口及仍未接的 Owner/writer，给 S02/S03/S05/S07/S08/S09 分别交付“可用能力／准确版本／尚缺 Owner 门”。按新主线固定源码，不能只照抄旧计划入口。
4. 映射冻结包19个唯一修订 case 引用及相关原 AC 指针，只声明实际完整／部分覆盖。类型、codec、标量 Schema 或隔离 PG fixture 通过不能改写整业务 case。原始输入包的 execution 保持 NOT_RUN。
5. 新执行 compare-only 证据/摘要/结构/链接/覆盖检查并一次任务级集中审查。已有相同输入结果直接复用；只有必要比较从原结果不可观察时，才在任务私有目录新增最小 probe/harness，保存新源码、实际日志和产物摘要。不能用一端实现生成另一端预期。
6. 交付 `validation.json`、`review.md`、`DELIVERY.md`，给 COORD 同步状态与正常保护合并。任何公共配置/生成源/现有测试变动先回协调安排窗口，不扩大成隐式生产改造。

共享 Main 组合运行从未执行的范围继续明确记录。只有具体比较缺口要求时才新增小范围合同测试；不能因为 SHA 变化重跑全部旧测试、PG 或 CRM/Platform 套件，也不能将旧结果相加称为共同主线组合实测。若以后确需新 PG 验证，须另由 COORD 预约独占测试资源窗口；本次派工默认只复用已绑定原证据。

## 依赖结果与复用边界

| 任务 | 最终候选 / 正常 PR | 已有结果（本次未重跑） |
|---|---|---|
| S01-02 | `3890b4fdbda47c3762b9f51ea75b8c156540c69f` / Main184 | C#77；21固定向量 |
| S01-03 | `b24f76445ffdc9b4df0de77eb394a6aa47726a0c` / Main188 | C#205；25固定向量 |
| S01-04 | `bfa00a4c055bfcf4f6b7b7fe696f7a819af720c8` / Main190 | C#132；9固定向量 |
| S01-05 | `b1f5206d3a0ca1af6513c4984700823c81dea210` / Main185 | TS202；34固定向量；typecheck |
| S01-06 | `f406e4e515a7ce82d77db5307e467e7bcdbc25e4` / Main191 | C#60、TS81；22固定向量；typecheck |
| S01-07 | `d0a3ae93e23aeb48128b0519dbf585e64c508ccf` / Main197 | C#657、TS130；26固定ID分为NCR20/IPQC6；typecheck |
| S01-08 | `912f6afa1d64cc0777721c03b5b39c0db91d60da` / CRM90 | 修正后C#186、Web142（82任务+60既有）、importer6；typecheck；30固定ID+49UI |
| S01-09 | `753696c8333325d9faa5d80ea429ab8b9480e71c` / Platform58 | C#110；35固定ID；没有发布NuGet |
| S01-10 | `a304f9e728775109cfada83312dbeb23b2da4a98` / Main198 | PG210、后续重叠57、生命周期1；56固定向量 |
| S01-11 | `6dbc1a00cec1349c2ff7fdc2fe633d64ce2cf82d` / Main199 | 修复后TS428+typecheck；C#417和5设计审计复用；10固定ID |

每项都是其实际范围内成功且零跳过的原结果，不构成这里新执行结果。复用前需核对实测源码、测试、资源/配置、依赖和实际运行环境；提交身份变化本身不导致重跑。原失败、旧候选和被替代结果全部保留。

## 向量适用范围

| 组 | 冻结数量 | 可比较的实际表面 |
|---|---:|---|
| FIN-CREDIT 三 profile | 25 | Main C# / TS / S01-10 PG证据 |
| FIN-INV Decimal18 / Qty12 | 9 | Main C# / TS / S01-10 PG证据 |
| ATP R1 | 22 | Main C# / TS / 有界标量Schema / S01-10 PG证据 |
| strict JSON / null / 结构相等 | 21 | Main C#、CRM C#/TS、Platform C#，按实际消费者范围 |
| NCR Main/CP4 | 20 | Main C#；FIELD_SUBASSERTION_ONLY |
| IPQC | 6 | Main TS；TYPE_BINDING_ONLY |
| LEAD OwnerId | 5 | CRM C#/TS 指定 required 字段投影 |
| INQ native locator | 4 | CRM C#/TS、Platform C#；不独立证明终态 revision |
| ERP registry/original bytes | 10 | Main C#/TS、Platform C#各自路由/原字节层 |

合计122，但不是每种语言都执行122。56条词法向量的 PG 拒绝可能在参数绑定前发生，不能笼统写成56次 DB 写入。比较数值 pair 时区分归一化值与存储保留尺度，原 wire 独立比较：FIN-INV `-0.000` 的数值零不等于可改写其原字符串。

包版本 `CP6-S01-CONTRACT-INPUTS/1.0.0`，33 sources、16 profiles。profiles SHA `2c221a04333cd5e0beba8d12a7aecbb08cbc80caa5faa74b22313045ce420e5e`；vectors SHA `b9a63a1512da2d2374e95d49d9f69c8636d06465f88a47862dc3604605d5be01`。原23条数值/单位输入使用 NUM-001..023；没有原生 case ID 的 source_case_id=null 保持。

## 必须保留的版本与证据边界

- **S01-07：** C#24原生入口/92声明/102完整Schema节点与TS203别名/23完整decoder根/5事件路由分别说明。原C#证据缺少 DLL hash，保留 `ORIGINAL_DLL_SHA256_MISSING`；现在重建只能形成新产物，不能补造旧运行身份。NCR20与IPQC6不可互换为双语言全覆盖。
- **CRM：** 明确 `CRM-INQ-TERMINAL-PROJECTION/20261013-R1` + native locator `740b840c697b60ebf51ceb0bd9a1898238814a49e36c2afe1d819c473b7df349` + overlay `bce20c5a3bc93826993be6c2ba7c9a440fffd467f746245e5665707d8eedf549` + effective `fd9a9444eac23225ecbf8ab91856dc7df2c0e4da53d9b765cbfccdeb2223ba2d`。90/99公开expiry/artifact/proof required-null；99双时钟，30三时钟；根+五内嵌分支及49原UI合同样例已测。原profile源文件的 ADOPTION_PENDING 不回写；采用事实引用修正后交付回执。Platform仍为 native locator 路由，不冒领 CRM effective revision 解码。不能在封闭payload加version字段或自动latest回退。
- **S01-11：** 用 Issue196 修复后的TS428，不用416当最终版本。Buffer合法输入在所有权边界复制；12项回归、原步骤复测和typecheck有准确证据。C#417和设计审计5按原输入复用。BYTES只证明原字节保持，明确 NativeSchemaValidated=false；三文件闭包缺F09，`EC_C06_200_StoredTerminal`、`EC_C06_202_StoredAdmission`、`WholeC06PublicRequest` 通用根未开放。O(n²)区间重叠容量风险留给真实接线前专项。
- **S01-10：** 正式 `CP6-POSTGRESQL-EXACT-MAPPINGS/1.0.0` / ADOPTED_BY_COORD，文件SHA `96ef3c9f22919e790f750773c2a4bdc675b7df5f145f7b9bfffeda6d19c1ec16`，与提案消费语义SHA `7bb24e1e36610996a46d13cace0a58e8032a3eb369e4dda235af5ec3c58e8752` 相同。20项存储数值尺度扩到0..18，17原生链+3ValueLine字典派生，余46不变；Money.scale/trace.scale仍0..8、calculationScale18。原PG18.6/Npgsql8.0.8的210与重叠57和生命周期1仍为 PROPOSED 运行，不能称正式映射新跑或267个不同测试。
- **ATP/财务：** ATP后继身份固定 `ATP-STABLEID-LEXICAL/20261011-R1` 并同时保留 PLAN-IF-004/005；FIN-CREDIT三个符号域、FIN-INV任意长度/Decimal18/Qty12与ERP原生Money独立，不合并精度规则。S01-04的Rational/MAVG完整对象/成本效果仍未证明；S01-10只提供有界表示和fixture，不补齐所有Owner语义。
- **Platform：** S01-09是准确native路由与原字节边界，保留现有CloudEvent三元组/manifest校验及P08根tracing重复属性兼容；不等于完整INQ/ERP decoder、Owner准入或Inbox原子性。没有NuGet发布身份；不能把当前Platform其他任务的测试结果算入S01。

## 下游交接

| 切片 | 可用能力 | 尚缺 Owner 门 |
|---|---|---|
| S02 | strict/null/equality、准确包身份、Platform原字节和版本路由 | 真实CRM Owner与Inbox/Outbox/Command/Audit同UoW；当前授权/TenantId/schema可信上下文；未知commit/原结果恢复；不能先独立提交Inbox |
| S03 | CRM终态projection R1及INQ/LEAD合同库、49UI合同样例往返 | 真实HTTP raw decode/IssuedAttempt/H01原子图/公开五字段回执；proof/当前session/ETag/下载；内部历史expiry；真实终态UI与PG持久化 |
| S05 | FIN-CREDIT三符号域、ERP current/frozen codec和原字节、C01/C02内存guard | Owner/Store/issuer/current AR/GL/member/finite证据；真实CreditNote存储；Draft/Confirm/SalesRelease独立门；迟到释放终态；F09及通用C06补齐 |
| S07 | ATP R1 C#/TS/标量Schema、五列PG fixture、完整PLAN-IF-004/005输入 | 全ATP wire/实际Owner与供应writer；租户/授权/锁序；净供需去重；真实PR/WO转单与恢复 |
| S08 | NCR C#原生闭包、IPQC TS根/事件/Evidence/Uom、库存精确profile | QA-POL/MES-EXEC/MES-OUTPUT/MainQuality/PLM当前source-head/权限；RequiredFenced/PROPOSED保留；真实QA/domain+Command+Audit写入；收货/质量/库存效果独立 |
| S09 | 精确算术与正式PG映射；66INV行/13表、42credit字段绑定、5ATP列fixture | 每个Owner/ORM/rawSQL/bulk/import writer；币种/单位/policy scale；AR/INV/GL权威；MAVG/Rational完整语义；财务原子/并发；逐租户前向迁移/恢复和历史损失核查 |

当前只读核对可见 `CreditControlService.CheckCreditAsync(..., decimal)`、`StockFinBridge` 的 round2、`ISupplyService` 的 decimal 标量及旧 CreditNote/Quality/Defect 服务仍在。新库与 fixture 不自动接通这些写者，不补造已丢精度，不改变 Owner/TenantId/授权。QA Owner 和存储 Owner 清单从既有准确报告引用，并对新主线代码补 pin。

## 完成定义与未执行

S01-12交付可复现的证据join、准确版本和六组消费者交接；经局部检查和任务级审查、COORD状态同步、正常保护合并并核对远端Main后才DONE。S01到12/12仅是切片任务交付，业务0/125、247完整运行NOT_RUN、41历史REFERENCE_ONLY、123/125原件盘点、ITER/REL缺件及WP05 PROPOSED保持。

本次仅读取固定源码/证据并生成仓外MD/JSON。没有测试、数据库、Git改动、网络写入、Actions运行/取消或触发配置变化，未实施S01-12。资料路径缺失的读取尝试已改按真实Git路径定位，不是产品测试失败或缺少已交付依赖。
