# ERP 迁移批次4 报告——BusinessPartnerView + bp/ 10 tabs

分支: `feat/ui-migrate-erp` | 样式基准: 特殊页/token 化(master-data 编辑页, el-form 字段, 视觉来自全局 override)

## 盘点表(11 文件)

| # | 文件 | 形态 | 硬编码/el-tag 发现 | 处置 | 字段/校验/i18n |
|---|------|------|-------------------|------|----------------|
| 1 | BusinessPartnerView.vue | 编辑页 shell(el-tabs + 操作栏) | 3 个 status pill `el-tag`(status 0/1/9);opLabel `el-tag`(effect=dark,5 态含 primary);scoped style 仅布局 | status pill → `CpTag`(tone info/ok/danger);opLabel el-tag 保留(见 concerns) | 全保留:onLoad/onSave/onDelete/onClear、乐观锁 409、route.query 自动加载、9-FLG 联动 watch、所有 t() 词条(sales.op.*/sales.btn.*/sales.msg.*/sales.err.E10008 及 JP literal keys) |
| 2 | bp/BasicInfoTab.vue | 表单 tab | 无 | **已合规无改动** | 12 FLG checkbox + flgEditable/flgChanged 计算、分类1〜10、販売分析1〜3 全保留 |
| 3 | bp/CustomerTab.vue | 表单 tab | 无 | **已合规无改动** | 得意先基本 + 売上計算19項 + 納品書5項 + 納品計算書10項 全保留 |
| 4 | bp/SupplierTab.vue | 表单 tab | 无 | **已合规无改动** | 発注先パターン + isOutsourcing/paidSupplyFlg/makerFlg 联动禁用 全保留 |
| 5 | bp/DeliveryTab.vue | 表单 tab | 无 | **已合规无改动** | 物流グループ/納入時間 等 9 字段保留 |
| 6 | bp/BillingTab.vue(实为入金先字段集) | 表单 tab | inline `color:#909399`(hint "1〜31,99") | span → `.hint-text` class + `var(--cp-muted)` | 請求締日/送付先 等全保留 |
| 7 | bp/PaymentTab.vue | 表单 tab(el-alert 说明) | 无 | **已合规无改动** | el-alert 说明文案保留 |
| 8 | bp/ApTab.vue | 表单 tab | 无 | **已合规无改动** | paymentScheduleCd required 保留 |
| 9 | bp/ArTab.vue | 表单 tab | 无 | **已合规无改动** | billingCd/billingName required 保留 |
| 10 | bp/ReceiptTab.vue | 表单 tab | inline `color:#909399`(hint "1〜31,99") | span → `.hint-text` + `var(--cp-muted)` | 振込人名/銀行/領収書送付先/集金予定日 全保留 |
| 11 | bp/PaySchTab.vue | 表单 tab | inline `color:#909399`(hint,含 t()) | span → `.hint-text` + `var(--cp-muted)` | 支払締日/税計算/バッチ予定締 及条件 required 全保留 |

## 迁移摘要
- 改动仅 4 文件:1 shell(status pill → CpTag) + 3 tab(inline `#909399` → `--cp-muted`)。
- 其余 7 tab **已合规无改动**(batch-2 InboundReceipt 先例)。
- 未改后端/API/路由/i18n 机制;无 :key;未动模板组件本体。

## 验证证据
- `npm run type-check`: 0 error。
- `npm run test`: 46 files / **316 passed**(基准保持)。
- 真栈走查(admin 登录, `/business-partner/window` 路由直达):shell + 全 9 FLG 开启后 10 tab 全部渲染,逐 tab 点击 console 无新错误(仅 intlify flatten warning + Vue Router next() deprecation,既存无关)。
- 截图:`shots/erp-bp-shell.png`(status pill 绿点 CpTag 可见)、`erp-bp-tab-customer.png`、`erp-bp-tab-supplier.png`、`erp-bp-tab-receipt.png`(tokenized hint "1〜31, 99" muted 灰渲染正确)。

## 新增模板缺口
无。CpTag/token 覆盖本批全部需求。

## Concerns
- BusinessPartnerView opLabel `el-tag`(effect="dark" size="large",opTagType 5 态含 `primary`)**保留未转 CpTag**:CpTag Tone 无 `primary` 对应色,且该 badge 是大号深色强调态,转换会破坏视觉且语义丢失。其色值走 element-overrides token,无硬编码,符合"特殊页 token 化"。判断为合理保留,记录备审。
