# WMS 迁移批次2 报告

分支：`feat/ui-migrate-wms`　样板：`OutboundOrderListView.vue`
页面（5）：LotTrace / Replenish / InboundReceipt / Slotting / ProductionInbound

## 形态分类

| 页面 | 分类 | 目标模板 |
|---|---|---|
| ReplenishView | 查询列表页 + 新建/バッチ弹窗 + 行操作 | CpPageShell + CpListPage + 2×CpFormDialog（default slot） |
| SlottingView | 一覧/明细双态同组件 + 分析弹窗 | CpPageShell + CpListPage（v-show 常挂）+ CpDetailPanel + CpFormDialog |
| LotTraceView | 非表格特殊页（追溯/时间轴/影响表） | token 化 + CpTag 基础件替换（保留 el-timeline/el-descriptions/el-table） |
| ProductionInboundView | 非表格特殊页（扫描录入 kiosk + 履历表） | token 化 + CpTag |
| InboundReceiptView | 全页可编辑录入页（明细行内编辑） | 审计——原即合规（el-card overrides + var()），维持原结构 |

## 迁移前盘点（一项不丢）

**Replenish**：API `replenishApi.search/create/generateBatch/execute/cancel`；列 11（補充NO mono / 状態 tag / 優先度 tag / トリガ tag / 製品 / 補充元 / 補充先 / ロット / 数量 num·formatQty / 実行日時 datetime·'—' / 操作）+ 行操作（実行/取消 仅 status=0）；搜索 4（補充NO/製品/倉庫/状態）；头部 新規 + バッチ生成；新建弹窗 7 字段（優先度 select/製品/倉庫/補充元/補充先/ロット/数量 number）；バッチ弹窗（倉庫/最小数量 number/hint alert）；无 v-permission；i18n 全保留。

**Slotting**：API `slottingApi.search(wh,status)/get/analyze/approve/cancel`；一覧列 8（方案NO mono / 状態 tag / 倉庫 / 分析日数 num / サンプル件数 num / 推奨件数 num / 分析時刻 datetime·'—' / 承認者 / 操作 開く）；搜索 2（倉庫/状態）；分析弹窗（倉庫/分析対象日数 number/hint）；明细=基本情報 6 项 + 推薦テーブル（ABCランク tag / 要移動 tag）+ 操作条（承認 status=1 / 取消 status∉{2,9}）；无 v-permission。

**LotTrace**：API `lotTraceApi.forward/backward/summary/recall`；搜索（製品/ロット/方向 radio）；追溯実行 + 在庫サマリ；サマリ el-descriptions（物理/引当/ロケ数/賞味期限/回収 tag）+ 回収设置/解除；影响表（顧客 or 仕入先）；時系列 el-timeline（txnType tag + 増減色）；无 v-permission。

**ProductionInbound**：API `inboundReceiptApi.confirm/search`；扫描録入（WO/製品/ロット·自動採番/数量/良品-不良品 radio→倉庫自動切換 W03/W04/ロケ/賞味期限/備考）；確定 + 直近履歴表；source pill；无 v-permission。

**InboundReceipt**：API `inboundReceiptApi.confirm` + `inboundOrderApi.get`；参照読込（発注参照）+ 明细行内编辑（製品/名/ロット/数量/単位/ロケ/単価/賞味期限）+ 追加/削除行；確定受領；onMounted 依 query.inboundNo 自动 loadOrder；无 v-permission。原页零禁用硬编码。

## 每页迁移摘要

- **码值列**：状態/優先度/トリガ/ABCランク → `kind:'tag'` + `map`（label 走 t() computed，tone 用共享 Tone；EP type→Tone 保色 info→muted·primary→info·warning→warn·success→ok·danger→danger）。
- **数量列** `kind:'num'` + `map:(v)=>({label:formatQty(v)})`（声明式，免插槽）；**datetime·'—'** 用 `map` 自定义格式（`kind:'date'` 仅 slice(0,10) 不够）；操作列走 `col-_action` 具名插槽。
- **弹窗**：Replenish 新建/バッチ、Slotting 分析 均用 CpFormDialog **default slot**（fields 声明表达不了 select/input-number/maxlength/placeholder/alert）；必填改 el-form `rules` 校验（等价原视觉 required 星号，qty=0 视为有值可过）。
- **Slotting 双态**：CpListPage 用 `v-show` 常挂（切明细不卸载，保留筛选/页码上下文）；明细基本情報改 CpDetailPanel（状態 CpTag 放 CpSectionHeader `#extra`，见缺口 #14）；推薦静态子表保留 el-table（非 fetch 驱动）。
- **特殊页 token 化**：LotTrace `#909399→--cp-muted`、`#606266→--cp-text`；ProductionInbound `#909399→--cp-muted`、`#f0f9ff→--cp-brand-bg`；status/source el-tag → CpTag（保留 el-timeline `:type` 用 EP type 作圆点色）。
- **InboundReceipt**：审计原页——scoped 仅 `--el-bg-color/--el-border-color-lighter`（EP 令牌，设计系统映射），无禁用硬编码、无状态 pill，维持原结构（特殊页「不强套模板」）。
- scoped style 目标归零：Replenish 仅 `.cp-dash`（muted 色）；Slotting 仅布局卡壳（全 --cp-* 令牌）；余页仅残留布局/EP 令牌。

## 批次验证证据

- `npm run type-check`：**0 error**。
- `npm run test`：**46 files / 304 tests 全绿**（基线 304，未回退）。
- 真栈走查（dev 5173→9991，`POST /api/auth/login` 200，admin/123456，gstack browse，日本語）：
  - **Replenish**：空态；标题「補充指示」+ 计数 pill 0、4 搜索字段 + クリア/検索、新規/バッチ生成 头部动作、CpEmpty「暂无数据」、pager「Total 0」；新規弹窗 7 字段（必填星号）打开 OK；截图 `shots/wms-Replenish.png` / `wms-Replenish-createdlg.png`。
  - **Slotting**：空态；分析実行 头部动作、2 搜索字段；分析弹窗（倉庫* / 分析対象日数 90 / info alert）打开 OK；截图 `shots/wms-Slotting.png` / `wms-Slotting-analyzedlg.png`。
  - **LotTrace**：搜索区（製品/ロット/方向 radio）；追溯 P001/LOT001（不存在 lot）返回 サマリ + 空 affected/nodes，渲染 2×CpTag 计数徽标（CpTag 替换生效）；截图 `shots/wms-LotTrace.png` / `wms-LotTrace-result.png`。
  - **InboundReceipt**：直接录入态渲染（9 按钮 / 9 输入 / 明细卡 + 追加行）；截图 `shots/wms-InboundReceipt.png`。
  - **ProductionInbound**：扫描録入表 + 履历表；source pill = CpTag(ok)、卡头 `background: rgba(20,184,196,.08)`（= --cp-brand-bg，原 #f0f9ff 已 token 化）；截图 `shots/wms-ProductionInbound.png`。
  - **console**：仅既有全局噪声——intlify object-flatten warning、Vue Router `next()` 弃用 warning、OA 通知 hub 401/SignalR negotiate 403(CSRF)、element-plus 库内 `[el-pagination] small` 弃用 warning（EP size-changer 自带，非本批引入、CpListPage 本体未改）。唯一 404 = 我用不存在的 P001/LOT001 测追溯 `lot-trace/summary`（后端 404，页面 try/catch 已优雅处理），非页面 bug。**无本批新增错误**。

## 新增模板缺口：1（#14，详见 `docs/superpowers/plans/2026-07-04-ui-restyle.md`）

- #14 **CpDetailPanel 的 tag 值无 tone 映射**（Minor）→ Slotting 明细状態 CpTag 放 CpSectionHeader `#extra` 代偿，未丢 tone。建议 items 增 `tone?`/`map`，与 ListColumn.map 对齐。

## Concerns

1. **无阻塞项**；未发现模板 BUG（`[el-pagination] small` 为 EP 库内 deprecation warning，非 CpListPage 缺陷，未改模板本体）。
2. **InboundReceipt 几乎零改动**：原页已合规（特殊录入页「只做 token 化」，本身无禁用硬编码），按盘点如实审计维持原结构；若后续要统一页头到 CpSectionHeader（去 font-weight:600），需接受 el-card 结构重排，本批保守不做。
3. 空态「暂无数据」、filter「展开更多/收起」仍组件内中文默认——沿用 follow-up #6，非本批引入。
