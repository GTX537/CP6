# WMS 迁移批次5 报告

分支 `feat/ui-migrate-wms`。页面：Remnant(260) / StockQuery(265) / PackingShip(266) / Vmi(267) / MobileTask(272)。

## 形态分类

| 页 | 形态 | 迁移方案 |
|---|---|---|
| RemnantView | 查询列表页 | CpPageShell + CpListPage + 2×CpFormDialog（match 保留 el-dialog） |
| StockQueryView | 查询列表页（服务端分页） | CpPageShell + CpListPage + toolbar 复选（#15）+ QC/履歴 保留 el-dialog |
| PackingShipView | 特殊页（梱包工作台：待ち队列 + 确定表单 + 履歴） | token 化，不套模板 |
| VmiView | 三 tab 多列表页 | el-tabs 外壳保留，每 tab 内嵌 CpListPage + 1×CpFormDialog |
| MobileTaskView | 特殊页（移动 PDA 卡片 UI + 扫码栏 + 统计） | token 化，不套模板 |

## 逐页盘点（一项不许丢）

### RemnantView
- API：remnantApi.search/create/reserve/unreserve/markUsed/dispose/match —— 全保留。
- 列 15：remnantNo(mono)/status(tag+map)/materialType(map)/materialGrade/widthMm(num)/lengthMm(num)/thicknessUm(num)/quantity(map 拼 unitCd)/sourceWorkOrderNo/sourceRollNo/warehouseCd/locationCd/reservedFor/registeredAt/操作。
- 搜索 5：remnantNo/materialType(select)/materialGrade/status(select)/sourceWorkOrderNo。
- 行操作条件化：予約(0)/解除(1)/使用(0|1)/廃棄(≠3) → col-_action slot。
- 头部操作：新規(CpFormDialog)/再利用検索(el-dialog)。原单表滚动无分页 → `:paginated="false"`。
- i18n：wms.remnant.* 全保留；filter-labels 接 search→wms.common.search、reset→wms.common.clear。无 v-permission。

### StockQueryView
- API：stockApi.search(服务端 page/pageSize→{items,total})/history/setQcStatus —— 全保留。
- 列 13：warehouse/location/product/lot/physicalQty(map)/allocatedQty(map)/availableQty(col slot 负数红)/unit/expiryDate(col slot slice+—)/owner(col slot tag)/flag(col slot recall tag)/qc(col slot tag)/操作(履歴+QC設定)。
- 搜索 5：warehouse/location/product/lot/ownerType(select)。hasStockOnly 复选 → toolbar slot（缺口 #15），fetch 闭包读取 + reload()。
- 分页服务端：fetch 透传 page/size。QC(radio+自定义 res.code 处理)/履歴(只读) 保留 el-dialog。i18n 全保留。无 v-permission。

### PackingShipView（特殊页）
- API：outboundOrderApi.search/get/ship + http.get(/wms/shipping/packages) —— 全保留。
- 结构（待ち队列卡 + 商品明细表 + 梱包确定表单 + 履歴表）原样保留；扫描/採番/追跡番号生成等逻辑不动。
- token 化：硬编码色值(#666/#ebeef5/#f5f7fa/#c0c4cc/#fdf6ec/#e6a23c/#606266/#f0f9ff)→ --cp-* token；6px 圆角→--cp-r-sm；0.15s→--cp-t-base。

### VmiView（三 tab 多列表）
- API：vmiApi.customers/details/billings/calculate/confirm —— 全保留。
- Tab1 客户汇总：CpListPage（搜索 customerCd；列 8；操作 詳細 → openDetails 切 tab + detailsRef.reload()）。
- Tab2 明细：CpListPage（无搜索栏，客户标签+更新 放 toolbar slot；列 8；expiryDate col slot 逾期红/橙；fetch 依 selectedCustomer 守卫；tab :disabled 无客户时）。
- Tab3 保管料：CpListPage（搜索 customerCd/yearMonth/confirmed(select true/false)；列 13；billingAmount col slot 加粗、confirmed col slot tag、操作 確定 slot；月次計算 toolbar → CpFormDialog）。
- 三 CpListPage 各持 ref，計算/確定后 billingsRef.reload()。i18n 全保留。无 v-permission。

### MobileTaskView（特殊页）
- API：mobileApi.scan/tasks/start/done/cancel + outboundOrderApi.search + inboundReceiptApi.search —— 全保留。
- 结构（扫码栏 + 5 统计块 + 5 tab 卡片列表 + 扫码结果块）原样保留；start/done(prompt)/cancel 逻辑不动。
- token 化：硬编码色值(#909399/#f5f7fa/#67c23a/#409eff/#e6a23c/#606266/#fff/#ebeef5/#ecf5ff/#303133)→ --cp-* token；8px/6px→--cp-r-sm；0.15s→--cp-t-base。stat-num→--cp-brand。

## 验证证据

- `npm run type-check`：0 error。
- `npm run test`：46 files / 304 passed（基线 304 holds）。
- 真栈走查（dev 5173 + API 9991，admin/123456，login 200）：5 页全部路由直达打开，列表/内容渲染正常，console 无新 error（仅既有 intlify flatten warning、Vue Router 动态注册 "No match found" warning、next() deprecation——均为迁移前既有，非本批引入）。
  - 截图：`.superpowers/sdd/shots/wms-{remnant,stock,packing,vmi,mobile}.png`。
  - Stock：数据 21 行，toolbar 残あり 复选 toggle + 検索 触发 reload 无误。
  - VMI：三 tab 切换正常，客户汇总空态渲染（QA 库无 VMI 客户数据，明细 tab 依设计 disabled），保管料 tab 显示 filter + 月次計算 toolbar。
  - Remnant 空态渲染（QA 库无端材数据）；Packing/Mobile 有数据卡片渲染，token 化观感正常。

## 新增模板缺口

0 个。本批仅复用既有补偿：#15（CpFilterBar 无 boolean → toolbar slot 复选，用于 Stock hasStockOnly）、#16（无 @row-click → action-column 按钮，用于各行操作）。
新形态观察（非缺口，未占号）：el-tabs 内嵌多 CpListPage 时各实例 onMounted 各自取数（VMI 三列表在页面加载即全部 fetch，隐藏 tab 亦然）——可接受，明细 tab 有 selectedCustomer 守卫不发请求。非编辑型弹窗（Remnant 再利用検索、Stock 履歴/QC）按既有约定保留 el-dialog。

Status：DONE。
