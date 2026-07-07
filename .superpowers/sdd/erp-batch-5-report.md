# ERP 迁移批次5 报告——OrderEntry 向导 + order steps + PlateMoldView

分支: `feat/ui-migrate-erp` | 样式基准: 特殊页/token 化(向导/编辑 FORM 页——steps/el-form/可编辑明细表保留,视觉走全局 override + token)

## 盘点表(5 文件)

| # | 文件 | 形态 | 硬编码/el-tag 发现 | 处置 | 字段/校验/步骤/i18n |
|---|------|------|-------------------|------|----------------------|
| 1 | OrderEntryView.vue | 向导 shell(header + el-steps + 检索 + footer) | opLabel `el-tag`(effect=dark,5 态含 primary);3 个状态 badge `el-tag`(info/success/info);scoped `box-shadow: 0 -2px 8px rgba(0,0,0,0.06)`(手机 sticky footer) | opLabel el-tag **保留**(见 concerns);3 badge → `CpTag`(tone info/ok/info);shadow → 新 token `--cp-shadow-up` | 全保留:opModel/setOperationType、onLoad/onNewClick/onPrev/onNext(明細行必填校验)、validateAll(E10022/E10009)、onSave(与信 creditCheck+确认框)、onDelete(软删+乐观锁 rowVersion)、onReset(isDirty 确认)、3 步 el-steps(桌面/手机 simple)、所有 t() 词条 |
| 2 | order/Step1HeaderAndDetails.vue | 向导 step(ヘッダ el-form + 可编辑明細 el-table) | 表格单元 status `el-tag`(:type=statusTagType→info/warning/success) | → `CpTag :tone="statusTone"`(statusTagType 改名 statusTone,返回 Tone: ok/warn/info) | 全保留:orderType 9 选项、customer/product picker(MasterReferenceDialog)、行增删/复制(仅原紙40)/上下移、onLookupMembers 部材引入、onQtyOrPriceChange 金额计算、63 项製品マスタ引入、所有列/校验 |
| 3 | order/Step2BasicInfo.vue | 向导 step(基本/構成/仕入/備考 el-form + 明細切替) | 标识 chip `el-tag`(default/primary);2 个 `el-tag`(info/success) | 标识 chip **保留**(primary-only 先例);info/success → `CpTag`(tone info/ok) | 全保留:isCompositionEditable(20/40/80)、watch orderType→calcIsEditable、全 40+ 字段、明細切替 radio |
| 4 | order/Step3ProcessInfo.vue | 向导 step(工程/工程備考/材料 3 表 + 明細切替) | 标识 chip `el-tag`(default/primary),无其他 | **已合规无改动**(仅 1 个 primary chip,按先例保留) | 全保留:工程/材料引入、行增删、materialTypeDiv/supplyDiv 选项、明細切替 |
| 5 | PlateMoldView.vue | 版型编辑页(header + 5 el-tabs + footer) | opLabel `el-tag`(effect=dark,5 态含 primary);2 个状态 badge `el-tag`(info/success);scoped style 纯布局 | opLabel el-tag **保留**;info/success → `CpTag`(tone info/ok) | 全保留:5 操作模式(登録/改定/訂正/削除[admin]/参照)、onLoadByNo/onLoadByEstimate、onSave(create/revise/update/remove 乐观锁)、onIssueLabel(CSV blob 下载)、onPurchaseOrder、5 tabs(基本/構成/添付/必要物/履歴[非register])、全 required、autoTypeClassFromProcess |

## 迁移摘要
- 改动 5 文件中 4 个 + 1 个 token 文件:OrderEntry(3 badge→CpTag + shadow token)、Step1(表格 status→CpTag + statusTone 助手)、Step2(2 badge→CpTag)、PlateMold(2 badge→CpTag);Step3 **已合规无改动**。
- `tokens.css` 追加 `--cp-shadow-up:0 -2px 8px rgba(16,52,60,.06);`(向上 sticky footer 阴影;沿用 ink 色调,替换原 rgba(0,0,0) 硬编码;OrderEntry 手机 footer 引用)。
- el-tag→CpTag 共 7 处(全部 info/success/warning 无损映射);opLabel el-tag(×2)与 default/primary 标识 chip(×2 Step2/Step3)按批次4 先例**保留并记录**。
- 未改后端/API/路由/i18n 机制;无 `:key` 新增(明細切替 radio 沿用既有 `:key="d.webOrderDetailNo"`,非本批引入);未动模板组件本体。

## 验证证据
- `npm run type-check`: 0 error(Tone 类型从 CpTag.vue 具名导入)。
- `npm run test`: 46 files / **316 passed**(基准保持)。
- 真栈走查(admin 登录, dev 5173 + VITE_API_TARGET 9991, login 200):
  - `/order` 向导:Step1(登録 el-tag + 自動採番待ち CpTag)→ 行追加 → 次へ → Step2(明細 chip el-tag + 参照のみ CpTag)→ 次へ → Step3(工程/材料表),三步 el-steps 完成态渲染正确。
  - `/plate-mold` 版型编辑:header(登録 el-tag + 自動採番待ち CpTag)+ 4 tabs(基本/構成/添付/必要物;履歴 register 模式隐藏,符合 v-if)+ required 星号保留。
  - console 仅既存无关警告(intlify flatten / Vue Router next() deprecation / SignalR CSRF 403 通知连接 / el-pagination small deprecation),**无本批新错误**。
  - 截图:`shots/erp-order-step1.png`、`erp-order-step2.png`、`erp-order-step3.png`、`erp-platemold-edit.png`。

## 新增模板缺口(ERP批次5 复盘)
无。CpTag/Tone + token 覆盖本批全部无损需求。

## Concerns
- **opLabel `el-tag` 保留(OrderEntry line 7 / PlateMold line 7)**:effect="dark" size="large",opTagType 5 态含 `primary`(CpTag Tone 无 primary 对应色);大号深色强调 badge,转换破坏视觉且语义丢失。色值走 element-overrides token,无硬编码。批次4 opLabel 先例,判为合理保留。
- **default/primary 标识 chip 保留(Step2 line 4「明細 No.X - CD」、Step3 line 4 同)**:无 type 的 el-tag 渲染为 primary/brand chip,作明細标识用;CpTag 无 primary tone,无损映射不存在。按派发消息「primary-only badges may stay el-tag」明示先例保留,逐处记录如上。
- `--cp-shadow-up` 为共享 token 新增(全局 tokens.css);当前仅 OrderEntry 引用,但 QuotationView/EstimateCalcView(未迁移)有同款手机 footer 阴影,后续批次可复用。
