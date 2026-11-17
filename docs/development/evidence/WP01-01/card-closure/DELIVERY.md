# WP01-01：替代验收采纳与 125 目标清单核销

结论：**卡 DONE**。本卡是 DESIGN/COORDINATION；完成有版本依据的采纳核对、逐目标采用判断及完整 WP01 门的如实检查，不提升任何目标或运行场景状态。原 scope/正反验收、375 张基线卡和已接受设计不变。

固定 DEV-1、`codex/wp01-01`，本次基线 Main `2e1d038f1cdf6db29a81e5e540334a3084589712`（[PR #1244](https://github.com/GTX537/CP6/pull/1244)）。直接前置 WP05-01 已由 [PR #180](https://github.com/GTX537/CP6/pull/180) 交付：准确源 `876a5344fc1e48ba092f896f22dd1a1f160ac191`、合并 `38a476dd2e78262060ebbc9123a74b3437ec3867` 均在当前 origin/main。M≤4h、150% 强制拆卡点 6h。

## 原出口与准确采纳

原卡要求接收 WP05 有版本替代验收及旧缺件覆盖/不能覆盖声明，修订台账规则和 125 目标精确采用判断；正向为明确采纳后登记实际来源并按完整门核对，反向拒绝仅改 NOT_INVENTORIED 或把后继清单伪装成原 30/48 项。

[用户采纳记录](../../authorized-adoption-release-20261015/authorization.json)于 2026-10-15 已明确采用 `WP01-01-ACCEPTANCE-BASIS-1.2.0` / `CP6-WP05-01-ACCEPTANCE-BASIS/1.2.0`。本次核对台账选择与该记录完全相同，[manifest](../adoption-v1.2/manifest.json) 的原始 2570 字节及 SHA-256 `ad811cc0af66c77b4953b0ec2b9fe6393f50e9823225e17bfc3f67ea914e3883` 一致；`load_basis` 实际验证固定资产、历史 Git/归档来源及结构覆盖。

新依据只适用于 PLM-ITER-01 的 401 项和 PLM-REL-01 的 178 项，共 579 项；承接 577 项并追加 DC-01/DC-02，18 条结构断言及原 Owner/来源解释保持。已恢复目录中的 ITER 99 项、REL 122 项是可定位的当前资料，不是失踪历史 30/48 项。历史成员身份及原件哈希继续为 null，缺件为 NOT_INVENTORIED / UNRECOVERABLE，逐项等价仍 UNPROVABLE。冻结候选里的历史 ADOPTION_PENDING/actual_adoption=false 不被改写；用户后续采纳记录是独立事实。

## 125 目标的逐项采用判断

下表逐目标核对 assessment、原目录状态、目标 JSON Pointer、每个 case 的定义指针和 source_case_id；目录行数不冒充通过数量。`原目录` 行继续采用该目录全部原 AC、必需修订场景、实际运行与远端交付门；新依据对这些 123 个目标不适用。两个 `替代依据` 行仅可走已接受的新依据路线，不能覆盖其原缺件，也不能覆盖其他目标的合同或运行门。所有目标/原 AC 状态保持本次基线。

| 目标 | 固定目录与精确定位 | 已登记项 | 本次采用判断 |
|---|---|---:|---|
| CRM-INQ-01 | [commercial-finance.json](../../../assessments/20261011/catalogs/commercial-finance.json) `/targets/0` | 60 | 不适用替代；保留全部原 AC 及修订门 |
| CRM-LEAD-01 | [commercial-finance.json](../../../assessments/20261011/catalogs/commercial-finance.json) `/targets/1` | 60 | 不适用替代；保留全部原 AC 及修订门 |
| CRM-ACT-01 | [commercial-finance.json](../../../assessments/20261011/catalogs/commercial-finance.json) `/targets/2` | 78 | 不适用替代；保留全部原 AC 及修订门 |
| CRM-ACC-01 | [commercial-finance.json](../../../assessments/20261011/catalogs/commercial-finance.json) `/targets/3` | 47 | 不适用替代；保留全部原 AC 及修订门 |
| CRM-CON-01 | [commercial-finance.json](../../../assessments/20261011/catalogs/commercial-finance.json) `/targets/4` | 75 | 不适用替代；保留全部原 AC 及修订门 |
| CRM-OPP-01 | [commercial-finance.json](../../../assessments/20261011/catalogs/commercial-finance.json) `/targets/5` | 54 | 不适用替代；保留全部原 AC 及修订门 |
| CRM-HO-01 | [commercial-finance.json](../../../assessments/20261011/catalogs/commercial-finance.json) `/targets/6` | 64 | 不适用替代；保留全部原 AC 及修订门 |
| ERP-BP-01 | [commercial-finance.json](../../../assessments/20261011/catalogs/commercial-finance.json) `/targets/7` | 124 | 不适用替代；保留全部原 AC 及修订门 |
| ERP-EST-01 | [commercial-finance.json](../../../assessments/20261011/catalogs/commercial-finance.json) `/targets/8` | 170 | 不适用替代；保留全部原 AC 及修订门 |
| ERP-QTN-01 | [commercial-finance.json](../../../assessments/20261011/catalogs/commercial-finance.json) `/targets/9` | 218 | 不适用替代；保留全部原 AC 及修订门 |
| INT-COM-01 | [foundation.json](../../../assessments/20261011/catalogs/foundation.json) `/targets/0` | 106 | 不适用替代；保留全部原 AC 及修订门 |
| ENG-PROD-01 | [engineering-oa.json](../../../assessments/20261011/catalogs/engineering-oa.json) `/targets/0` | 9 | 不适用替代；保留全部原 AC 及修订门 |
| ENG-REV-01 | [engineering-oa.json](../../../assessments/20261011/catalogs/engineering-oa.json) `/targets/1` | 114 | 不适用替代；保留全部原 AC 及修订门 |
| ENG-BOM-01 | [engineering-oa.json](../../../assessments/20261011/catalogs/engineering-oa.json) `/targets/2` | 115 | 不适用替代；保留全部原 AC 及修订门 |
| ENG-ROU-01 | [engineering-oa.json](../../../assessments/20261011/catalogs/engineering-oa.json) `/targets/3` | 102 | 不适用替代；保留全部原 AC 及修订门 |
| PLM-REQ-01 | [engineering-oa.json](../../../assessments/20261011/catalogs/engineering-oa.json) `/targets/4` | 70 | 不适用替代；保留全部原 AC 及修订门 |
| PLM-ITER-01 | [engineering-oa.json](../../../assessments/20261011/catalogs/engineering-oa.json) `/targets/5` | 99 | 采用新依据 401 项；旧缺件保留；机器/运行门未过 |
| PLM-DOC-01 | [engineering-oa.json](../../../assessments/20261011/catalogs/engineering-oa.json) `/targets/6` | 30 | 不适用替代；保留全部原 AC 及修订门 |
| PLM-VER-01 | [engineering-oa.json](../../../assessments/20261011/catalogs/engineering-oa.json) `/targets/7` | 144 | 不适用替代；保留全部原 AC 及修订门 |
| PLM-CHG-01 | [engineering-oa.json](../../../assessments/20261011/catalogs/engineering-oa.json) `/targets/8` | 92 | 不适用替代；保留全部原 AC 及修订门 |
| PLM-DEV-01 | [engineering-oa.json](../../../assessments/20261011/catalogs/engineering-oa.json) `/targets/9` | 108 | 不适用替代；保留全部原 AC 及修订门 |
| PLM-REL-01 | [engineering-oa.json](../../../assessments/20261011/catalogs/engineering-oa.json) `/targets/10` | 122 | 采用新依据 178 项；旧缺件保留；机器/运行门未过 |
| PLM-FBK-01 | [engineering-oa.json](../../../assessments/20261011/catalogs/engineering-oa.json) `/targets/11` | 52 | 不适用替代；保留全部原 AC 及修订门 |
| PLM-LIFE-01 | [engineering-oa.json](../../../assessments/20261011/catalogs/engineering-oa.json) `/targets/12` | 76 | 不适用替代；保留全部原 AC 及修订门 |
| ERP-ORD-01 | [commercial-finance.json](../../../assessments/20261011/catalogs/commercial-finance.json) `/targets/10` | 236 | 不适用替代；保留全部原 AC 及修订门 |
| ERP-FUL-01 | [commercial-finance.json](../../../assessments/20261011/catalogs/commercial-finance.json) `/targets/11` | 68 | 不适用替代；保留全部原 AC 及修订门 |
| PLAN-DEM-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/0` | 236 | 不适用替代；保留全部原 AC 及修订门 |
| PLAN-ATP-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/1` | 341 | 不适用替代；保留全部原 AC 及修订门 |
| FIN-CREDIT-01 | [commercial-finance.json](../../../assessments/20261011/catalogs/commercial-finance.json) `/targets/12` | 104 | 不适用替代；保留全部原 AC 及修订门 |
| PLAN-POL-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/2` | 136 | 不适用替代；保留全部原 AC 及修订门 |
| PLAN-SUP-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/3` | 136 | 不适用替代；保留全部原 AC 及修订门 |
| PLAN-MRP-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/4` | 140 | 不适用替代；保留全部原 AC 及修订门 |
| PLAN-PO-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/5` | 124 | 不适用替代；保留全部原 AC 及修订门 |
| PLAN-EXC-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/6` | 425 | 不适用替代；保留全部原 AC 及修订门 |
| PUR-PR-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/7` | 87 | 不适用替代；保留全部原 AC 及修订门 |
| PUR-RFQ-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/8` | 295 | 不适用替代；保留全部原 AC 及修订门 |
| PUR-PO-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/9` | 242 | 不适用替代；保留全部原 AC 及修订门 |
| PUR-GR-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/10` | 199 | 不适用替代；保留全部原 AC 及修订门 |
| PUR-MATCH-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/11` | 192 | 不适用替代；保留全部原 AC 及修订门 |
| PUR-RETURN-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/12` | 642 | 不适用替代；保留全部原 AC 及修订门 |
| MES-RES-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/14` | 205 | 不适用替代；保留全部原 AC 及修订门 |
| MES-WO-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/15` | 121 | 不适用替代；保留全部原 AC 及修订门 |
| MES-MAT-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/16` | 160 | 不适用替代；保留全部原 AC 及修订门 |
| MES-EXEC-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/17` | 229 | 不适用替代；保留全部原 AC 及修订门 |
| MES-OUTPUT-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/18` | 314 | 不适用替代；保留全部原 AC 及修订门 |
| FIN-COST-01 | [commercial-finance.json](../../../assessments/20261011/catalogs/commercial-finance.json) `/targets/13` | 89 | 不适用替代；保留全部原 AC 及修订门 |
| QA-POL-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/21` | 213 | 不适用替代；保留全部原 AC 及修订门 |
| QA-IQC-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/22` | 128 | 不适用替代；保留全部原 AC 及修订门 |
| QA-IPQC-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/23` | 205 | 不适用替代；保留全部原 AC 及修订门 |
| QA-NCR-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/24` | 335 | 不适用替代；保留全部原 AC 及修订门 |
| WMS-MASTER-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/25` | 236 | 不适用替代；保留全部原 AC 及修订门 |
| WMS-STOCK-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/26` | 157 | 不适用替代；保留全部原 AC 及修订门 |
| WMS-TRANSFER-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/27` | 867 | 不适用替代；保留全部原 AC 及修订门 |
| WMS-RSV-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/28` | 109 | 不适用替代；保留全部原 AC 及修订门 |
| WMS-IN-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/29` | 209 | 不适用替代；保留全部原 AC 及修订门 |
| WMS-COUNT-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/30` | 25 | 不适用替代；保留全部原 AC 及修订门 |
| WMS-TRACE-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/31` | 28 | 不适用替代；保留全部原 AC 及修订门 |
| WMS-DISP-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/32` | 27 | 不适用替代；保留全部原 AC 及修订门 |
| WMS-MOBILE-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/33` | 32 | 不适用替代；保留全部原 AC 及修订门 |
| WMS-CTRL-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/34` | 28 | 不适用替代；保留全部原 AC 及修订门 |
| ERP-CREDIT-01 | [commercial-finance.json](../../../assessments/20261011/catalogs/commercial-finance.json) `/targets/14` | 94 | 不适用替代；保留全部原 AC 及修订门 |
| WMS-OUT-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/35` | 842 | 不适用替代；保留全部原 AC 及修订门 |
| WMS-DEL-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/36` | 220 | 不适用替代；保留全部原 AC 及修订门 |
| WMS-RMA-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/37` | 103 | 不适用替代；保留全部原 AC 及修订门 |
| FIN-MASTER-01 | [commercial-finance.json](../../../assessments/20261011/catalogs/commercial-finance.json) `/targets/15` | 395 | 不适用替代；保留全部原 AC 及修订门 |
| FIN-GL-01 | [commercial-finance.json](../../../assessments/20261011/catalogs/commercial-finance.json) `/targets/16` | 79 | 不适用替代；保留全部原 AC 及修订门 |
| FIN-CLOSE-01 | [commercial-finance.json](../../../assessments/20261011/catalogs/commercial-finance.json) `/targets/17` | 28 | 不适用替代；保留全部原 AC 及修订门 |
| FIN-REPORT-01 | [commercial-finance.json](../../../assessments/20261011/catalogs/commercial-finance.json) `/targets/18` | 26 | 不适用替代；保留全部原 AC 及修订门 |
| FIN-AP-01 | [commercial-finance.json](../../../assessments/20261011/catalogs/commercial-finance.json) `/targets/19` | 87 | 不适用替代；保留全部原 AC 及修订门 |
| FIN-AR-01 | [commercial-finance.json](../../../assessments/20261011/catalogs/commercial-finance.json) `/targets/20` | 79 | 不适用替代；保留全部原 AC 及修订门 |
| FIN-INV-01 | [commercial-finance.json](../../../assessments/20261011/catalogs/commercial-finance.json) `/targets/21` | 492 | 不适用替代；保留全部原 AC 及修订门 |
| FIN-FX-01 | [commercial-finance.json](../../../assessments/20261011/catalogs/commercial-finance.json) `/targets/22` | 28 | 不适用替代；保留全部原 AC 及修订门 |
| OA-DEF-01 | [engineering-oa.json](../../../assessments/20261011/catalogs/engineering-oa.json) `/targets/13` | 27 | 不适用替代；保留全部原 AC 及修订门 |
| OA-FORM-01 | [engineering-oa.json](../../../assessments/20261011/catalogs/engineering-oa.json) `/targets/14` | 35 | 不适用替代；保留全部原 AC 及修订门 |
| OA-TASK-01 | [engineering-oa.json](../../../assessments/20261011/catalogs/engineering-oa.json) `/targets/15` | 46 | 不适用替代；保留全部原 AC 及修订门 |
| OA-APP-01 | [engineering-oa.json](../../../assessments/20261011/catalogs/engineering-oa.json) `/targets/16` | 88 | 不适用替代；保留全部原 AC 及修订门 |
| SYS-WORKSPACE-01 | [foundation.json](../../../assessments/20261011/catalogs/foundation.json) `/targets/1` | 20 | 不适用替代；保留全部原 AC 及修订门 |
| SYS-LANG-01 | [foundation.json](../../../assessments/20261011/catalogs/foundation.json) `/targets/2` | 20 | 不适用替代；保留全部原 AC 及修订门 |
| SYS-AUD-01 | [foundation.json](../../../assessments/20261011/catalogs/foundation.json) `/targets/3` | 21 | 不适用替代；保留全部原 AC 及修订门 |
| SYS-PRIV-01 | [foundation.json](../../../assessments/20261011/catalogs/foundation.json) `/targets/4` | 22 | 不适用替代；保留全部原 AC 及修订门 |
| SYS-FILE-01 | [foundation.json](../../../assessments/20261011/catalogs/foundation.json) `/targets/5` | 30 | 不适用替代；保留全部原 AC 及修订门 |
| SYS-NUM-01 | [foundation.json](../../../assessments/20261011/catalogs/foundation.json) `/targets/6` | 16 | 不适用替代；保留全部原 AC 及修订门 |
| SYS-CLIENT-01 | [foundation.json](../../../assessments/20261011/catalogs/foundation.json) `/targets/7` | 22 | 不适用替代；保留全部原 AC 及修订门 |
| SYS-JOB-01 | [foundation.json](../../../assessments/20261011/catalogs/foundation.json) `/targets/8` | 25 | 不适用替代；保留全部原 AC 及修订门 |
| SYS-TENANT-01 | [foundation.json](../../../assessments/20261011/catalogs/foundation.json) `/targets/9` | 23 | 不适用替代；保留全部原 AC 及修订门 |
| SYS-IAM-01 | [foundation.json](../../../assessments/20261011/catalogs/foundation.json) `/targets/10` | 40 | 不适用替代；保留全部原 AC 及修订门 |
| INT-ID-01 | [foundation.json](../../../assessments/20261011/catalogs/foundation.json) `/targets/11` | 35 | 不适用替代；保留全部原 AC 及修订门 |
| INT-OPS-01 | [foundation.json](../../../assessments/20261011/catalogs/foundation.json) `/targets/12` | 35 | 不适用替代；保留全部原 AC 及修订门 |
| PLT-CTX-01 | [foundation.json](../../../assessments/20261011/catalogs/foundation.json) `/targets/13` | 28 | 不适用替代；保留全部原 AC 及修订门 |
| PLT-HTTP-01 | [foundation.json](../../../assessments/20261011/catalogs/foundation.json) `/targets/14` | 42 | 不适用替代；保留全部原 AC 及修订门 |
| PLT-MSG-01 | [foundation.json](../../../assessments/20261011/catalogs/foundation.json) `/targets/15` | 40 | 不适用替代；保留全部原 AC 及修订门 |
| PLT-DEL-01 | [foundation.json](../../../assessments/20261011/catalogs/foundation.json) `/targets/16` | 60 | 不适用替代；保留全部原 AC 及修订门 |
| PLT-OBS-01 | [foundation.json](../../../assessments/20261011/catalogs/foundation.json) `/targets/17` | 32 | 不适用替代；保留全部原 AC 及修订门 |
| PLT-RELEASE-01 | [foundation.json](../../../assessments/20261011/catalogs/foundation.json) `/targets/18` | 32 | 不适用替代；保留全部原 AC 及修订门 |
| PLT-TEST-01 | [foundation.json](../../../assessments/20261011/catalogs/foundation.json) `/targets/19` | 24 | 不适用替代；保留全部原 AC 及修订门 |
| ENG-TOOL-01 | [engineering-oa.json](../../../assessments/20261011/catalogs/engineering-oa.json) `/targets/17` | 38 | 不适用替代；保留全部原 AC 及修订门 |
| ERP-FSC-01 | [commercial-finance.json](../../../assessments/20261011/catalogs/commercial-finance.json) `/targets/23` | 24 | 不适用替代；保留全部原 AC 及修订门 |
| ERP-PRICE-01 | [commercial-finance.json](../../../assessments/20261011/catalogs/commercial-finance.json) `/targets/24` | 28 | 不适用替代；保留全部原 AC 及修订门 |
| PUR-SUB-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/13` | 48 | 不适用替代；保留全部原 AC 及修订门 |
| MES-SCHED-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/19` | 36 | 不适用替代；保留全部原 AC 及修订门 |
| MES-PERF-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/20` | 48 | 不适用替代；保留全部原 AC 及修订门 |
| WMS-KIT-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/38` | 17 | 不适用替代；保留全部原 AC 及修订门 |
| WMS-SLOT-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/39` | 18 | 不适用替代；保留全部原 AC 及修订门 |
| WMS-REP-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/40` | 21 | 不适用替代；保留全部原 AC 及修订门 |
| WMS-XDOCK-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/41` | 20 | 不适用替代；保留全部原 AC 及修订门 |
| WMS-ROLL-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/42` | 25 | 不适用替代；保留全部原 AC 及修订门 |
| WMS-REM-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/43` | 26 | 不适用替代；保留全部原 AC 及修订门 |
| WMS-TOOL-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/44` | 14 | 不适用替代；保留全部原 AC 及修订门 |
| WMS-INK-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/45` | 16 | 不适用替代；保留全部原 AC 及修订门 |
| WMS-VMI-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/46` | 18 | 不适用替代；保留全部原 AC 及修订门 |
| WMS-SAMPLE-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/47` | 19 | 不适用替代；保留全部原 AC 及修订门 |
| WMS-AUTO-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/48` | 20 | 不适用替代；保留全部原 AC 及修订门 |
| WMS-ANALYTICS-01 | [operations.json](../../../assessments/20261011/catalogs/operations.json) `/targets/49` | 20 | 不适用替代；保留全部原 AC 及修订门 |
| FIN-BUD-01 | [commercial-finance.json](../../../assessments/20261011/catalogs/commercial-finance.json) `/targets/25` | 24 | 不适用替代；保留全部原 AC 及修订门 |
| FIN-BANK-01 | [commercial-finance.json](../../../assessments/20261011/catalogs/commercial-finance.json) `/targets/26` | 28 | 不适用替代；保留全部原 AC 及修订门 |
| FIN-ASSET-01 | [commercial-finance.json](../../../assessments/20261011/catalogs/commercial-finance.json) `/targets/27` | 32 | 不适用替代；保留全部原 AC 及修订门 |
| OA-CONN-01 | [engineering-oa.json](../../../assessments/20261011/catalogs/engineering-oa.json) `/targets/18` | 32 | 不适用替代；保留全部原 AC 及修订门 |
| SPACE-MASTER-01 | [foundation.json](../../../assessments/20261011/catalogs/foundation.json) `/targets/20` | 8 | 不适用替代；保留全部原 AC 及修订门 |
| SPACE-DES-01 | [foundation.json](../../../assessments/20261011/catalogs/foundation.json) `/targets/21` | 9 | 不适用替代；保留全部原 AC 及修订门 |
| SPACE-IMPORT-01 | [foundation.json](../../../assessments/20261011/catalogs/foundation.json) `/targets/22` | 8 | 不适用替代；保留全部原 AC 及修订门 |
| SPACE-PUB-01 | [foundation.json](../../../assessments/20261011/catalogs/foundation.json) `/targets/23` | 27 | 不适用替代；保留全部原 AC 及修订门 |
| SPACE-PLAN-01 | [foundation.json](../../../assessments/20261011/catalogs/foundation.json) `/targets/24` | 7 | 不适用替代；保留全部原 AC 及修订门 |
| SPACE-OPS-01 | [foundation.json](../../../assessments/20261011/catalogs/foundation.json) `/targets/25` | 12 | 不适用替代；保留全部原 AC 及修订门 |
| SPACE-EXT-01 | [foundation.json](../../../assessments/20261011/catalogs/foundation.json) `/targets/26` | 6 | 不适用替代；保留全部原 AC 及修订门 |
| SPACE-AI-01 | [foundation.json](../../../assessments/20261011/catalogs/foundation.json) `/targets/27` | 10 | 不适用替代；保留全部原 AC 及修订门 |

## 完整门与明确不覆盖的范围

| 判断层次 | 本次实测/核对结论 | 后续责任 |
|---|---|---|
| 用户对准确版本的采纳 | ADOPTED_BY_USER；无需重复作相同决定 | 本卡已核销 |
| 125 目标差异、原目录定位与适用性 | 125/125 有精确判断；123 COMPLETE，2 个历史缺件保留 | 本卡已核销，现有差异仍由各主体卡实施 |
| 独立机器采纳与目录闭合 | `inventory_coverage_valid=true`，`basis_adoption_valid=false`，错误为 `INDEPENDENT_VERIFIER_UNCONFIGURED` | 独立维护者按现有 [OPERATOR](../adoption-v1.2/OPERATOR.md) 配置 authority；最终完成门 WP14-10 仍须核对，不创建替代签名或弱化验证器 |
| ITER/REL 实际业务 | 两个 `target_runtime_valid=false`，未提交运行证据 | 已登记的 WP05 主体实现/消费者卡及 WP14-07/10；按相同契约可用标明的模拟业务数据实跑，不能用工具夹具当验收 |
| WP01 工作包完整门 | 差异结论已有，机器目录闭合尚未成立；工作包保持 ASSESSING | 本卡 DONE 与整个工作包完成分别记录，不以关卡提升工作包 |
| 原询盘与修订场景 | CRM-INQ-01 原 60 AC PASS 保持；247 必需修订场景仍 245 NOT_RUN、2 PASS | 真实目标门和严格前置保持，0/125 目标 DONE |

本次未改采纳验证器、任务原定义、目标原 AC、冻结资产或业务代码。原工具已将用户采纳、authority、execution 和实际业务分层；本次沿用，不新增权限/证明包装。其他卡的实施责任只在本报告和阶段计划中定位，不由本卡实施。

## 验证、审查与交付

本次实际运行 `wp01-close-audit.py`：125 目标逐项定位、原 case 定义对应、版本/manifest 原字节、579 项划分、准确来源祖先以及阶段 A 严格依赖/最长链计算全部通过。第一次辅助脚本因重复解析相同目录耗时而中止，修正读取缓存后完成；该过程没有修改业务数据或仓库输入。记录在 `D:/CP6-test-artifacts/WP01-01-card/source-audit.json` 和 `audit-first-attempt.txt`。

实际运行 `manage_roadmap.py --write --report ... --basis-report ...`，检查当前结构及生成页；独立机器门如上保持拒绝。工具实现未改，复用 [WP02-13](../../WP02-13/card-closure/DELIVERY.md) 的 85 项相关工具回归及 [原信任阶段](../trust-stages/DELIVERY.md) 的真实范围，不称本卡重跑。当前卡不涉及产品行为、租户/权限/金额/并发/迁移输入改变，无新 PostgreSQL、宿主或浏览器产品执行。

集中审查完整 diff：只含本卡报告、台账/生成页、四份项目记忆及阶段 0 计划收尾。核对原 375 卡字段、所有依赖及全部目标/原 AC/修订状态不变；阶段 0 的其他未满足前置卡保持 PLANNED。完整证据核对及精确缺口见[执行计划](../../../planning/20261117-card-execution.md)。阶段 A 可开工卡下游最长链重新计算为 WP02-05b，替代阶段 0 中尚未完成计算时的 WP02-13b 暂定下一卡。

候选和远端主线工作流树保持 `3355235636fd45116f5445fca32fc65e49f13003`：普通工作流手动入口，唯一 push 入口为版本 tag；本次无 tag，不触发/取消 Actions，不改触发或部署。正式推送前和合并前核对引用。

本次开工清理了阻挡规范任务分支的四个已合并旧 WP01-01 分支（PR #205/#209/#217/#225），逐个核对准确 tip、远端采用、无树占用/活动依赖后条件删除；旧交付证据仍保留。正常 PR 合并并核对远端后，官方 finish 定向删除 `codex/wp01-01` 本地/远端分支、固定 DEV-1 保持干净 detached 并释放席位；实际结果由本次 PR 和 `D:/CP6-test-artifacts/WP01-01-card/finish.json` 记录。无产品进程需要停止。

本卡有效区间 2026-11-17T13:13:51.898374+00:00 至 2026-11-17T13:18:25.811429+00:00：0.076h；另计两次登记间串行核对/集成/收尾 0.181h，计划累计 1.575h，关闭 8 卡、0 目标、4 次拆卡。当前卡无剩余出口；阶段 0 和总预算均未到限，未到 24h 检查点。
