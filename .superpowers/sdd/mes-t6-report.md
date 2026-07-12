# M-MES T6 実施報告：測試補網（報工状態機 / PlanningBoard 排産改期）

- 分支：`feat/m-mes-crosscutting`　基線：1749 緑
- 結果：焦点 9 用例 全緑 → 全量 **1758 passed / 5 skipped**（＝1749 + 9、基線不跌）
- 生産改動：**ゼロ**（新規テスト 2 ファイルのみ）
  - `CP6.Tests/Mes/ProductionResultStateMachineTests.cs`（状態機 5 用例）
  - `CP6.Tests/Mes/PlanningBoardServiceTests.cs`（排産改期 4 用例）

---

## 1. 既有覆盖盘点（重複回避の根拠）

grep（ProductionResult/WorkOrder/PlanningBoard/Backflush/justCompleted/CostCollect）で既存テストを実読：

| 対象 | 既存テスト | 本 T6 との棲み分け |
|---|---|---|
| 反冲（OUT/ISSUE）＋ActualQty 回写＋負在庫＋冪等＋部分失敗重放 | `CP6.Tests/Mes/BackflushTests.cs`（5 用例） | **除外**（F1 波 C.1 で錨定済）。状態機テストは backflush=null で注入せず |
| 完工→反冲→成本归集→结转（料工費 WIP/FG/VAR 凭証・金額・冪等・2 闸） | `CP6.Tests/Fin/WorkOrderCompleteCostFlowTests.cs`（6 用例） | **除外**（F1 波 C.2 で錨定済）。同ファイルは `NoOpWmsBridgeHook` を使用＝入庫フック発火契約は未検証 |
| 工時派生（機時合併/人工累加/覆盖/中断控除） | `CP6.Tests/ProductionResultHourTests.cs`（4 用例） | **除外**（A2 で錨定済）。本 T6 は状態遷移・時間戳のみ |
| 完成品入庫の在庫側真値（`InboundService.CreateFinishedGoodsFromWorkOrderAsync` → W01 完成品在庫・冪等） | `CP6.Tests/WmsErpClosedLoopTests.cs`（line 134-137） | **在庫側は錨定済**。本 T6 は「状態機が末工程完了で入庫フックを正しい良品累計で一度だけ発火するか」の**状態機側缺口のみ**を spy で補完 |
| 報工状態機（開始/中断/再開/完了・非法/合法流転・トリガ発火） | **なし（審計 #7＝ゼロ）** | ← 本 T6 で新規 |
| PlanningBoardService（Reschedule/AutoArrange） | **なし（審計 #7＝ゼロ）** | ← 本 T6 で新規 |

結論：**反冲・成本归集・工時派生・入庫在庫真値は全て既存で錨定済**。本 T6 は状態機の遷移／ガード／トリガ発火契約と、排産改期の落库真値・並び替えロジックに限定し、既存とゼロ重複。

---

## 2. 報工状態機 用例（`ProductionResultStateMachineTests.cs`）

`ProductionResultService.WriteAsync` は明示トランザクションを使うため、DB は `InMemoryEventId.TransactionIgnoredWarning` を抑止した `NewDb()` を使用（F1 テストと同姿勢）。backflush/finBridge は null 注入（F1 で錨定済のため）。

### 用例① Complete_WhenNotStarted_Rejected_NothingPersisted（非法流転）
- 入力：WO_A（Status=2 発行済）＋ 工程 OP1（ProcessStatus=0 未着手）に対し `CompleteAsync(good=5)`
- 手算：case4 の先頭 `proc.ProcessStatus != 1` で mutation 前に throw。tx 未 commit → 何も落库しない。

| 断言 | 手算期望 |
|---|---|
| 例外 message | `ME-MSG-042` |
| ProductionResult 件数 | 0 |
| proc.ProcessStatus | 0（不変） |
| wo.Status | 2（不変） |

### 用例② Start_WhenAlreadyStarted_Rejected（非法流転：重複開始）
- 入力：WO_B ＋ OP1（ProcessStatus=1 着手中）に `StartAsync`
- 手算：case1 先頭 `proc.ProcessStatus != 0` で throw。

| 断言 | 手算期望 |
|---|---|
| 例外 message | `ME-MSG-042` |
| ProductionResult 件数 | 0 |
| proc.ProcessStatus | 1（不変） |

### 用例③ FullLifecycle_Start_Suspend_Resume_Complete（合法全流転＋時間戳）
唯一工程 OP1（初期 status0）に Start→Suspend(理由R01)→Resume→Complete(good=8) を順次実行。

| ステップ | proc.ProcessStatus | wo.Status | 時間戳 / 数量 | PR type |
|---|---|---|---|---|
| Start | 0→**1** | →**3** InProgress | proc.ActualStartTime=NOW / wo.ActualStartDate=NOW | 1 |
| Suspend | 1→**3** | →**5** Interrupted | — | 2 |
| Resume | 3→**1** | →**3** InProgress | — | 3 |
| Complete(8) | 1→**2** | →**4** Completed（全工程完了） | proc.ActualEndTime=NOW / proc.GoodQty=8 / wo.ActualEndDate=NOW / wo.CompletedQty=8 | 4 |

- PR 合計 **4 件**、type 昇順 = `[1,2,3,4]`。全て断言済（時間戳は `>= t0` 手算下界で検証、套套回避）。

### 用例④ Complete_LastProcess_FiresInboundHookOnce_WithAccumulatedGoodQty（全工程完了トリガ入庫）
2 工程 OP1/OP2（両者 status1 着手中）、`RecordingWmsBridge` spy 注入。

| 操作 | allDone? | wo.Status | wo.CompletedQty | フック発火 |
|---|---|---|---|---|
| Complete(OP1, 10) | OP2 が status1 → **false** | 3 のまま | 0→**10** | 0 回 |
| Complete(OP2, 7) | OP1=2 & OP2=当該 → **true** | →**4** | 10→**17** | **1 回**：`OnProductionCompletedAsync("WO_D", 17, tester)` |

- 断言：中間工程では spy.Calls=0・wo.Status=3；末工程で spy.Calls=**1**・LastWorkOrderNo="WO_D"・LastGoodQty=**17**（良品累計 10+7）・wo.ActualEndDate 非 null。
- **状態機側缺口の補完**：既存 F1／端到端テストは `NoOpWmsBridgeHook` を使うため、この「末工程だけ・累計良品数で・一度だけ」発火契約は本 T6 が初めて錨定。入庫先の在庫真値は `WmsErpClosedLoopTests` が別途錨定済（重複回避）。

### 用例⑤ Guards_SuspendNeedsReason_CompleteNeedsPositiveGood（境界ガード）
- WO_E/OP1(status1) に `SuspendAsync`（理由なし）→ 手算：case2 で proc 変更前に理由空チェック → `ME-MSG-024`。proc.ProcessStatus=1 不変。
- WO_F/OP1(status1) に `CompleteAsync(good=0)` → 手算：case4 で proc 変更前に `GoodQty<=0` チェック → `ME-MSG-012`。proc.ProcessStatus=1 不変。
- 両 WO とも PR 0 件。

---

## 3. PlanningBoard 排産/改期 用例（`PlanningBoardServiceTests.cs`）

`RescheduleAsync`/`AutoArrangeAsync` は明示トランザクション不使用のため素の InMemory（`TestHelper.CreateInMemoryContext`）で十分。

### 用例① Reschedule_UnstartedProcess_PersistsNewTimesAndMachine（改期落库真値）
- 入力：OP1（status0、旧 08:00-10:00、M0）を `Reschedule(2026-08-01 09:00 → 17:00, M9)`
- 手算：Id 一致 → status0 通過 → start<end 通過 → 3 値上書き。

| 断言 | 手算期望 |
|---|---|
| PlanStartTime | 2026-08-01 09:00 |
| PlanEndTime | 2026-08-01 17:00 |
| MachineCd | M9 |
| Modifier | planner |

### 用例② Reschedule_Guards_StartAfterEnd_Started_NotFound（改期3ガード）

| 入力 | 手算期望 message |
|---|---|
| status0 工程に start(8/2) > end(8/1) | `ME-MSG-003` |
| status1（着手中）工程に valid 改期 | `ME-MSG-042`（発行後ドラッグ不可） |
| 不存在 Id | `ME-MSG-043` |

### 用例③ Reschedule_PastDate_And_MachineOverlap_AreAccepted_NoConflictGuard（境界＝現状仕様 pin）
- (a) status0 工程を **過去日付 2020-01-01 08:00-10:00** に改期 → 手算：過去日付ガード無し（`start<end` のみ検証）→ **受理**・落库。
- (b) 同一号機 M1 上で既存工程 A(2026-07-15 08:00-10:00) と**完全重複**する 08:00-10:00 に改期 → 手算：号機衝突検知ロジック無し → **受理**・落库。A も 08:00-10:00 を占有し続ける（重複許容）。
- **これは缺陷ではなく現状仕様の記録**：サーバは単にドラッグ位置を永続化し、過去/衝突は UI・計画側の責務（§2.3 コメント準拠）。自動衝突回避が要件化された場合の缺口として concerns に記載。

### 用例④ AutoArrange_OrdersByPriorityDeliverySort_PacksPerMachine_SkipsStarted（自動配置真値）
BaseDate=2026-07-15（baseTime=08:00）、DefaultHoursPerJob=2。

対象工程（status0）：

| 工程 | 所属WO(優先度/納期) | 号機 | Sort | LeadTime | 導出 hours |
|---|---|---|---|---|---|
| P_A | WO_HI(3 / 7-20) | M1 | 1 | null | default **2** |
| P_C | WO_HI(3 / 7-20) | M2 | 2 | 1 日 | (int)max(1, 1×8)=**8** |
| P_B | WO_LO(1 / 7-18) | M1 | 1 | null | default **2** |
| P_D | WO_HI(3) | M1 | 3 | — | **対象外**（status1 着手済） |

- 並び（優先度DESC→納期ASC→Sort）＝`[P_A, P_C, P_B]`（WO_HI 群が先、群内 Sort、最後に WO_LO）。
- 号機別カーソル配置（初期 08:00）：

| 工程 | 号機 | 開始 | 終了 | 号機 ptr 更新 |
|---|---|---|---|---|
| P_A | M1 | 08:00 | 10:00 | M1→10:00 |
| P_C | M2 | 08:00 | 16:00（8h） | M2→16:00 |
| P_B | M1 | 10:00（P_A の後に連続） | 12:00 | M1→12:00 |

| 断言 | 手算期望 |
|---|---|
| changed | 3 |
| P_A | 08:00-10:00 |
| P_C | 08:00-16:00 |
| P_B | 10:00-12:00 |
| P_D | PlanStart/End とも null（不変） |

全て一致。服務出力＝手算。

---

## 4. Self-review

- 断言は全て手算固定値（状態値・時間戳・数量・並び位置・changed 件数・例外 message）。套套逻辑（サービス出力を再計算して同値比較）なし。時間戳は `>= t0` 手算下界で検証。
- 既存カバレッジとゼロ重複（§1 の盘点根拠）。反冲・成本・工時・入庫在庫真値は全て除外。
- 状態機：非法流転（②未開始完了/②重複開始）＋合法全流転（③）＋全工程完了トリガ入庫（④）＋境界ガード（⑤）を網羅。
- PlanningBoard：改期落库真値（①）＋3ガード（②）＋境界現状仕様 pin（③）＋自動配置並び替え真値（④）。
- 生産コード改動ゼロ。焦点 9/9 緑、全量 1758 緑（基線 1749 不跌）。

## 5. Concerns（缺陷は修正せず記票）

1. **[軽微・仕様記録] `RescheduleAsync` に過去日付ガード・号機衝突検知が無い**（用例③で pin 済）。現状は「サーバはドラッグ位置を永続化するだけ、衝突/過去は UI・計画側の責務」という設計。もし将来「同一号機の時間重複を自動回避／過去日付を拒否」が要件化されれば、ここが缺口になる。現時点では **by-design と判断**（§2.3 コメント準拠）、缺陷として修正せず記票のみ。
2. **[観察] `AutoArrange` は既存の他工程の計画時間を無視して号機カーソルを baseTime から再構築する**。status0 の対象工程のみを並べ直すため、同一号機に既に status0 でない（着手中等）工程が別時間帯を占有していても、その占有を考慮せず 08:00 から詰める。用例④では対象外工程を検証済だが、「着手中工程の実占有と重ならないか」は保証しない。これも現状は計画者の目視前提の設計と解釈。缺陷判定は保留、記票のみ。
