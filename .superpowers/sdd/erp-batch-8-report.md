# ERP 迁移批次8 报告——QuotationView token化 + 模块硬编码清扫与 el-tag 收尾

分支: `feat/ui-migrate-erp` | 样式基准: 特殊页/token 化(見積 edit/wizard FORM 页——el-tabs/el-form/計算候補表/印刷明細可编辑表全保留,视觉走全局 override + token)

## Part A —— QuotationView.vue 盘点表(1224 行,模块最大页)

| 维度 | 内容(一项不许丢) |
|------|-------------------|
| API 调用 | quotationApi.getByNo / getCalcCandidates / create / update / remove / copy / confirm / cancelConfirm / issue;masterApi.getBases / getStaffs —— **全保留** |
| 形态 | header(opLabel + No + 状态徽章 + 操作种别 radio 7 态) + 検索区(op≠New) + el-tabs(6 tab:ヘッダー/御見積書+備考15/関連計算書候補表/印刷明細可编辑表/提出用計算書+メモ8/メモ3) + footer 操作按钮群。判定=**非表格特殊页**(向导/编辑 FORM),不套 CpListPage/CpFormDialog 模板,仅 token 化 + CpTag。 |
| 列/字段/校验 | calcCandidates 候補表 11 列、details 印刷明細可编辑表 11 列(el-input/el-input-number 行内编辑)全保留;rules(baseCd/staffCd/customerCd required)保留;备考15/メモ8/メモ3 循环保留。 |
| 交互逻辑 | opModel/onOpChange(New reset·Copy 拉副本·Edit/View/Delete 无号提示)、loadByNo、isLinked/toggleLinked(使用✓↔calcs↔自動 details 增删+renumber)、addDetailRow/removeDetailRow、computeRowAmount/recalcTotalAmount、refreshCalcCandidates(顧客/案件 300ms debounce watch)、onSave(create/update)、onDelete、onConfirm(2 段:进 Confirm 模式→勾选→再提交)、onCancelConfirm、onIssue(帳票 prompt Q/SC/C)、handleConflict(409 乐观锁)、cleanPayload、notifyOpener(postMessage)、onClose(standalone window.close/router.back)、onMounted URL 驱动(op/no+document.title)—— **全保留** |
| 权限 | 无 v-permission 指令(本页无) |
| i18n | 全部 t() 词条保留;无 CpFilterBar(本页非列表页,无 :filter-labels 需求) |

### 硬编码/el-tag 处置(4 处改动,零逻辑改动)

| # | 位置 | 发现 | 处置 |
|---|------|------|------|
| 1 | 模板 line 380 内联 | `color:#909399`(明細空提示)| → `var(--cp-muted)` |
| 2 | scoped `.qtn-no` line 1138 | `color:#409eff`(旧 element primary 蓝)| → `var(--cp-brand)`(还原"跟随 primary"原意,同批次6/7 先例) |
| 3 | scoped `.is-mobile .footer-card` line 1202 | `box-shadow:0 -2px 8px rgba(0,0,0,0.08)`(sticky footer 阴影)| → `var(--cp-shadow-up)`(共享 token) |
| 4 | 模板 line 9 状态徽章 `el-tag` | `statusTagType` effect=plain,返回 success/warning/info(未承認/承認済/見積確定済)| → **CpTag**(新增 `statusTone` computed:ok/warn/info,分支逐一镜像;import CpTag+Tone) |

### el-tag 豁免(documented keep)
- **line 7 opLabel `el-tag`**(effect=dark,size 动态,`opTagType` 7 态含 `primary`/`success`/`danger`)—— **保留**。CpTag Tone 无 primary 对应色,大号深色强调 badge,转换破坏视觉+丢语义。opLabel 模式(批次4/5/6/7 一致先例)。色值走 element-overrides token,无硬编码。

## Part B —— 模块级硬编码清扫 + el-tag census(Task 12 Step 4 安全网)

### 硬编码 grep(清扫后)
```
$ grep -rn "color:#\|background:#\|border.*#[0-9a-fA-F]\|rgba(" cp6.web/src/views/erp --include=*.vue
(空 —— 无残留)

$ grep -rnE "#[0-9a-fA-F]{3,8}" cp6.web/src/views/erp --include=*.vue | grep -v 假阳
仅命中 `#default` slot 名(<template #default>)与 2 处头注释文档行
(OrderPriceCorrectionView 注释 "#e6a23c→--cp-warn"、SheetUnitPriceView 注释 "#606266→--cp-muted"
 —— 均为历史处置记录注释,非实代码)

$ grep -rn "var(--el-" cp6.web/src/views/erp --include=*.vue
(空 —— 无 --cp-* 可替换的 --el-* 残留)
```
批次1-7 如预期已干净;**本批唯一真实硬编码 = QuotationView 3 处(已 token 化,见 Part A)**。无图表系列色(§2.5 无适用),无 `/* cp-chart-color */` 豁免行。

### el-tag census(src/views/erp 全域)
| 文件 | el-tag | 分类 | 处置 |
|------|--------|------|------|
| QuotationView:7 | opLabel effect=dark | primary/dark keep | 保留(见 Part A 豁免) |
| QuotationView:9 | statusTagType plain | 无损(success/warning/info)| **→ CpTag**(本批转换) |
| BusinessPartnerView:6 / EstimateCalcView:7 / OrderEntryView:7 / PlateMoldView:7 / ProductMasterView:7 | opLabel effect=dark | primary/dark keep | 保留(批次3-7 已文档化豁免) |
| order/Step2BasicInfo:4 / order/Step3ProcessInfo:4 | `<el-tag size="small">` 无 type(=element 默认 primary 色)| primary keep | 保留 —— 静态标识 chip(明細 No.-CD),element 默认 primary 渲染;CpTag Tone 无 primary 对应,转 info 为视觉损失。属"primary keep"类别(非 info/success/warning/danger 无损集)。文件相邻已用 CpTag(info/ok)承担状态语义,此 chip 承担 identity 语义,故保留。 |

其余 grep 命中(BusinessPartnerListView:5 / FscChecklistView:4 / OrderPriceCorrectionView:5 / SheetUnitPriceView:4)均为**头注释文档行**,非实 el-tag。
派发消息点名的 MasterReferenceDialog/FlowTimeline/MrpBoardView 均在 `src/components/*` 或 views/mes,**不在 src/views/erp scope**,本批不处理(honest scope 判定)。

## 验证证据

- `npm run type-check`: **0 error**(vue-tsc --build)。
- `npm run test`: 46 files / **316 passed**(基准 316 保持)。
- 真栈走查(admin 已登录, dev 5173 + VITE_API_TARGET 9991, `POST /api/auth/login` 200 userName/password):
  - `/quotation?op=new`:header opLabel "新規" 渲染 brand 深色 pill;新規态 `.qtn-no` v-if 隐藏(form.qtnNo 空);console 仅既存噪声(intlify flatten / Vue Router next() deprecation / SignalR 403);**无本批新错误**。截图 `shots/erp-quotation-new.png`。
  - `/quotation-list`:列表 15 行加载正常(首行 QTN2026060003-01)。
  - `/quotation?op=view&no=QTN2026060003-01`(照会):
    - `.qtn-no` 计算色 = `rgb(20,184,196)` = `#14B8C4` = **`--cp-brand`**(token 生效)✓
    - 状态 CpTag "未承認" = `.cp-tag.t-info`,计算色 `rgb(78,128,238)` = `#4E80EE` = **`--cp-info`**(CpTag 转换正确)✓
    - opLabel "照会" el-tag brand teal 深色 pill(豁免保留渲染正常)✓
    - 6 tab / header/顧客/FSC/発行金額 各 section 渲染完整;console 仅既存噪声(el-pagination `small` deprecation/背景 401),**无本批新错误**。截图 `shots/erp-quotation-view.png`。
  - Spot-check(sweep scope 2 页):`/order?op=new`(含 order Step2/Step3 el-tag primary keeps)—— 无新错误;`/estimate-calc?op=new`(opLabel keep)—— opLabel "登録" 渲染正常。

## 新增模板缺口(ERP批次8 复盘)
无。token + 全局 override + CpTag 覆盖本批全部需求,无 slot 逃生舱/模板改动。

## Concerns
- **opLabel el-tag 保留(QuotationView:7,唯一 QuotationView 豁免)**:effect=dark,`opTagType` 7 态含 primary(CpTag Tone 无 primary),批次4-7 先例。色值走 token,无硬编码。
- **order/Step2BasicInfo:4 + order/Step3ProcessInfo:4 保留 `<el-tag size="small">`**(2 处):无 type = element 默认 primary 色,承担 identity chip 语义(明細 No.-CD);属"primary keep"(非无损 info/success/warning/danger 集),CpTag 无 primary 对应色。相邻已用 CpTag 承担状态语义。若评审倾向统一转 CpTag(tone info),可后续单独处理——本批按"仅转无损 info/success/warning/danger、保留 primary"的批次7 标准判定。
- `#409eff`→`--cp-brand`:该值为旧 element primary 蓝(现全局 `--el-color-primary: var(--cp-brand)`),映射还原"跟随 primary"原意,非改语义(同批次6/7)。
- confirm/cancelConfirm/issue 等业务流(需既存已登録見積+确定状态)在 QA 数据不建单前提下未真栈提交;statusTone 三分支经 view 态(未承認 info)真栈命中 1 支,另 2 支(承認済 warn/見積確定済 ok)逐一镜像原 statusTagType 逻辑并经 vue-tsc 校验。
