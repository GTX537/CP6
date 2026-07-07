# WMS 迁移批次1 报告

分支：`feat/ui-migrate-wms`　样板：`OutboundOrderListView.vue`
页面（6）：WmsPlaceholder / InboundOrderList / Expiry / CrossDock / WarehouseList / StockTakeList

## 形态分类

| 页面 | 分类 | 目标模板 |
|---|---|---|
| WmsPlaceholderView | 非表格特殊页（且**无路由孤儿代码**，全 WMS 路由已指向真实视图） | 仅 token 化 scoped style |
| InboundOrderListView | 查询列表页 | CpPageShell + CpListPage |
| ExpiryView | 查询列表页（勾选 + 批量廃棄 + 概览指标） | CpPageShell + CpListPage（selectable/toolbar/col slot） |
| CrossDockView | 查询列表页 + 新建弹窗 + 行操作 | + CpFormDialog（default slot） |
| WarehouseListView | 查询列表页 + 新建/編集弹窗 + 删除 | + CpFormDialog（default slot） |
| StockTakeListView | 查询列表页 + 計画作成弹窗（成功后跳详情） | + CpFormDialog（default slot） |

## 迁移前盘点（一项不丢）

**WmsPlaceholderView**：无 API/列/搜索/操作/权限指令；scoped style 5 处硬编码色值（#303133/#909399/#606266/#f4f7fa/#409eff）+ 22px/700/4px → 全部 token 化（--cp-ink/--cp-muted/--cp-text/--cp-line-soft/--cp-info/--cp-fs-2xl/800/--cp-r-sm）。i18n t() 全保留。**字号映射披露**：subtitle 原 14px → `var(--cp-fs-lg)`=**14.5px**（tokens.css 无 14px 档：--cp-fs-base=13px、--cp-fs-md=13.5px、--cp-fs-lg=14.5px，取最近档，+0.5px 为有意的 token 归一化）；title 22px → `--cp-fs-2xl`=21px 同理（-1px）。

**InboundOrderList**：API `inboundOrderApi.search`；列 8（单号 mono / 状態 tag / 種別 纯 map / 仕入先名 overflow / 発注書NO / 予定入荷日 date / 倉庫 / 作成日 date）+ 操作（開く/入庫）；搜索 6 项（单号/仕入先CD/倉庫/状態/予定入荷 从·至）；头部 新規；statusTagOf→statusTone；无批量；权限指令无。

**Expiry**：API `expiryApi.expiring(days,wh)` + `expiryApi.dispose`；列 9（製品/ロット/倉庫/ロケ/物理在庫 qty/賞味期限 date/残日数 色/単価 ¥/損失額 ¥）+ 勾选列；搜索 2（残日数 number/倉庫）；概览指标（合計/超期件数/損失合計）；批量 一括廃棄（confirm+prompt）；无权限指令。

**CrossDock**：API `crossDockApi.search/create/execute/cancel`；列 10（单号 mono/状態 tag/製品/数量 qty/仕入先/客先/fromDock/toDock/一時ロケ/実行日時 datetime）+ 操作（実行/取消 仅 status=0）；搜索 3（单号/製品/状態）；新建弹窗 11 字段（含 input-number/textarea/placeholder/maxlength）；无权限指令。

**Warehouse**：API `warehouseApi.search/create/update/delete`；列 7（倉庫CD/倉庫名/種別 tag/拠点CD/責任者/マイナス許可 slot/住所 overflow）+ 操作（編集/削除）；搜索 3（倉庫CD/種別/拠点CD）；新建/編集弹窗 8 字段（含 switch/select/編集时CD禁用）；typeTagOf→typeTone；无权限指令。

**StockTake**：API `stockTakeApi.search/createPlan`；列 9（单号 mono/種別 纯 map/状態 tag/倉庫/ロケ prefix/製品/予定日 date/実施日 slot '—'/完了日 slot '—'）+ 操作（開く）；搜索 4（单号/種別/状態/倉庫）；計画作成弹窗 7 字段；成功后 router.push 至明细；无权限指令。

## 每页迁移摘要

- 码值列：状態/種別/倉庫種別 → `kind:'tag'` + `map`（label 走 t() computed，tone 用共享 Tone；EP type→Tone 保色映射 info→muted·primary→info·warning→warn·success→ok·danger→danger）。無 tag 视觉的 種別（Inbound/StockTake）用无 kind 的纯 map。
- 日期列 `kind:'date'`（=slice(0,10)）；带 '—' 空态的 実施/完了日 与 datetime 実行日時 用 col slot 保原样。
- 数量/金额/彩色残日数 用 col slot（formatQty/¥formatMoney/dayClass）。
- 行操作/条件操作/概览 pill/批量按钮 用 col-_action / toolbar slot。
- 弹窗：CrossDock/Warehouse/StockTake 用 CpFormDialog **default slot**（fields 声明表达不了 input-number/switch/maxlength/placeholder/编辑禁用）；必填改由 el-form `rules` 校验（等价原手工 onSave 检查）。
- scoped style 目标归零：Placeholder 全 token；其余仅残留纯布局（.tb-spacer 撑开、.cp-dash/.overdue/.soon 语义色用 --cp-* token，无硬编码）。

## 批次验证证据

- `npm run type-check`：**0 error**。
- `npm run test`：**46 files / 294 tests 全绿**。
- 真栈走查（dev 5173→9991，`POST /api/auth/login` 200，admin/123456，gstack browse）：
  - InboundOrderList：1 行；标题+计数 pill、検索/クリア、開く/入庫、新規 全可点；截图 `shots/wms-InboundOrderList.png`。
  - WarehouseList：4 行；種別 tag 保色（原料/半製品/完成品/不良品）、編集 弹窗（倉庫CD 禁用）；截图 `shots/wms-WarehouseList.png`。
  - Expiry：空态（无到期库存）；一括廃棄(0) 按钮禁用、勾选列、概览 pill v-if 隐藏正确；截图 `shots/wms-Expiry.png`。
  - CrossDock：空态；新建弹窗「クロスドック新規」11 字段打开；截图 `shots/wms-CrossDock.png`。
  - StockTakeList：空态；計画弹窗「スナップショット作成」7 字段打开；截图 `shots/wms-StockTakeList.png`。
  - WmsPlaceholderView：**无任何路由指向**（全 WMS 路由已接真实视图），无法真栈走查；token 化经 type-check/test 覆盖。
  - console：仅既有 intlify object-flatten warning、Vue Router next() 弃用 warning、以及 SignalR 通知 hub 的 401/403(CSRF) 报错——均为**全局既有问题**（NotificationBell 通知连接），与本批 WMS 页面无关，非新增。

## 新增模板缺口：4（#9~#12，详见 `docs/superpowers/plans/2026-07-04-ui-restyle.md`）

- #9 CpFilterBar daterange 无 value-format（返回 Date）→ fetch 内本地格式化。
- #10 CpFilterBar 无 number 型 → Expiry 残日数用 text 代偿。
- #11 CpListPage 强制分页，无法关闭 → Expiry 跨页批选降为当页（数据完整）。
- #12 **CpListPage 无命令式 reload/外部刷新触发**（Important，影响 Expiry/CrossDock/Warehouse）→ `:key` 重挂载代偿，副作用=filters/page 重置。最值得回填的契约扩展。

## Concerns

1. **#12 是真痛点**：3 页的 in-place 变更后刷新目前靠重挂载，会丢用户搜索/翻页上下文（Warehouse 编辑后最明显）。建议下批前给 CpListPage 补 `defineExpose({reload})` 或 `refreshKey` watch。
2. **WmsPlaceholderView 是孤儿死代码**（无路由引用）：已按批次清单 token 化，但可考虑后续删除或复用为模板；本批仅保守 token 化。
3. 空态文案「暂无数据」、filter「展开更多/收起」仍为中文默认（CpEmpty/CpFilterBar 组件默认），沿用 Task 11 试点约定（follow-up #6），非本批引入。

## 修正记录（评审 round 1）

1. **分页切片补齐（Important）**：`WarehouseListView.vue` / `StockTakeListView.vue` fetchList 原样返回全量数组（pager >20 行时静默失效）→ 与其余三页一致改为 `all.slice((page-1)*size, (page-1)*size+size)`，`total: all.length` 不变。
2. **CrossDock qty 必填星号还原（Minor）**：form-item 加 `prop="qty"` + rules 补 `qty: required(change)`——原页有 required 视觉星号无校验，按 Warehouse 先例校验强化（qty=0 视为有值可过 required，与原页允许 0 一致）。
3. **缺口 #13 补记**：FilterField 无单日期 `date` 类型（InboundOrderList 从/至 → daterange，单侧开区间能力丢失）已录入 ui-restyle.md §模板缺口。
4. **报告披露补充**：WmsPlaceholderView subtitle 14px→--cp-fs-lg=14.5px、title 22px→--cp-fs-2xl=21px 的 token 最近档归一化已在盘点段披露。

**修正验证**：type-check 0 error；test 46 files / 294/294 全绿；真栈复查 Warehouse——rows=4 / pager "Total 4" / 单页(pages=1) / 计数 pill=4，console 无页面相关新错误（仅既有 SignalR hub CSRF 噪声）。
