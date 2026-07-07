# ERP 迁移批次6 报告——EstimateCalc 向导 + estimate steps token化

分支: `feat/ui-migrate-erp` | 样式基准: 特殊页/token 化(向导/编辑 FORM 页——el-steps/el-form/可编辑明细表保留,视觉走全局 override + token)

## 盘点表(4 文件)

| # | 文件 | 形态 | 硬编码/el-tag 发现 | 处置 | 字段/校验/步骤/i18n |
|---|------|------|-------------------|------|----------------------|
| 1 | EstimateCalcView.vue | 向导 shell(header opLabel + 操作种别 radio + el-steps + 按 No 检索 + footer) | opLabel `el-tag`(effect=dark,5 态含 primary);scoped `.qtn-no{color:#409eff}`;手机 sticky footer `box-shadow:0 -2px 8px rgba(0,0,0,0.08)` | opLabel el-tag **保留**(见 concerns);#409eff→`var(--cp-brand)`;shadow→`var(--cp-shadow-up)` | 全保留:opModel/onOpChange(5×4 矩阵含 Copy 拉取/Delete·View·Edit 无号提示 E10022)、loadByNo、onNewClick、onPrev/onNext(Step1 validate 门控)、onSave(create/update+乐观锁 handleConflict 409)、onDelete(remove+rowVersion+确认框)、onReset(standalone window.close/isDirty 确认)、onBeforeUnmount reset、onMounted URL 参数驱动(op/no + document.title + notifyOpener postMessage)、3 步 el-steps(桌面/手机 simple)、所有 t() 词条 |
| 2 | estimate/Step1BasicInfo.vue | 向导 step(10 区块 el-form:基本/商品分類/受注/材質印刷成型/寸法/最終工程/戦略×10/見積数量×8+パレット×8/提案ロット/備考) | scoped `:deep(.el-divider__text){color:#409eff}` | #409eff→`var(--cp-brand)` | 全保留:rules/validateBusiness、isDisabled/isRequired/isPageReadOnly 字段控制、大→中→小級联过滤+清空 watch、onBladeBlur/onEstimateQtyBlur 联动、markDirty deep watch、defineExpose validate、onMounted 15 路主数据 Promise.all + 担当者预加载、全部字段/prop/required |
| 3 | estimate/Step2Processes.vue | 向导 step(工程明細 el-table:选择/順番/工程 select/作業/WG/製造拠点/規格×3/版No/備考×2/操作 + リサイクル法 A/B/C) | scoped `.title{color:#409eff}`;el-divider vertical(布局) | #409eff→`var(--cp-brand)` | 全保留:onAdd/onRemoveRow/onRemoveSelected/resequence、onSelectionChange、onProcessChange 回填工程名、リサイクル法 A/B/C 弹窗 + onRecycleConfirm 追加工程行、watch rows resequence、onMounted 工程码(M038)、空态 el-empty |
| 4 | estimate/Step3Result.vue | 向导 step(見積区分 + 再計算 + 計算結果 descriptions + 数量別金額 table + 計算ロジック notes) | 内联 `style="color:#999"`;scoped `.title{color:#409eff}`、`.highlight{color:#f56c6c}`、`.notes{color:#666}` | #999→`var(--cp-muted)`;#409eff→`var(--cp-brand)`;#f56c6c→`var(--cp-danger)`;#666→`var(--cp-text)` | 全保留:runCalc(estimateCalcApi.calculate + 回填 estimateSqm/standardUnitPrice/estimateUnitPrice/confirmedUnitPrice + store.calcResult)、fmtNum/fmtMoney(formatCurrency)、confirmedUnitPrice 可编辑、onMounted 自动计算、空态 el-empty |

## 迁移摘要
- 4 文件全部改动,均为**纯 CSS token 化**(色值/阴影硬编码 → token),零逻辑改动。
- `#409eff`(旧 element primary 蓝,现全局 `--el-color-primary: var(--cp-brand)`)→ `var(--cp-brand)`,共 4 处(qtn-no / divider__text / Step2 title / Step3 title)。
- Step3:`#f56c6c`(見積単価强调红)→ `var(--cp-danger)`;`#666`(計算ロジック notes)→ `var(--cp-text)`;内联 `#999`(未計算 li)→ `var(--cp-muted)`。
- 手机 sticky footer 阴影 `0 -2px 8px rgba(0,0,0,0.08)` → `var(--cp-shadow-up)`(批次5 引入的共享 token,派发消息点名复用;此处 footer 模式与 OrderEntry 同款)。
- **el-tag→CpTag:0 处转换**。本批唯一 el-tag 为 opLabel(effect=dark + 5 态含 primary),按批次4/5 先例**保留并记录**;无 info/success/warning 无损候选。
- 未改后端/API/路由/i18n 机制;无 `:key` 新增;未动模板组件本体;无模板逃生舱(slot)新增。

## 验证证据
- `npm run type-check`: 0 error。
- `npm run test`: 46 files / **316 passed**(基准保持)。
- 真栈走查(admin 已登录, dev 5173 + VITE_API_TARGET 9991, `POST /api/auth/login` 200):
  - `/estimate-calc` 向导 Step1:10 区块 el-form 渲染,`.el-divider__text` 计算色 = `rgb(20,184,196)`(=`--cp-brand`,token 生效)。
  - Step1→次へ 触发必填校验(18 项 is-error,阻止前进)——符合原校验逻辑;经 Pinia `estimate.setStep(2/3)` 强制渲染 Step2/Step3 做视觉核验(**不创建真实单据**)。
  - Step2:工程明細 el-table + `.card-header .title` = `rgb(20,184,196)`。
  - Step3:3× `.title` = `rgb(20,184,196)`;`.highlight`(見積単価)= `rgb(229,72,77)`(=`--cp-danger`);`.notes` = `rgb(71,97,107)`(=`--cp-text`);onMounted 自动 runCalc → "計算完了" toast;数量別金額表空态 el-empty(No Data)正确;opLabel `登録` el-tag 渲染为 brand 深色 pill。
  - console 仅既存无关噪声(intlify flatten / Vue Router next() deprecation / SignalR CSRF 403 通知连接 / el-pagination `small` deprecation / router "No match /estimate-calc" 既存路由告警),**无本批新错误**(纯 CSS 变更不产生 JS 错误)。
  - 截图:`shots/erp-estimate-step1.png`、`erp-estimate-step2.png`、`erp-estimate-step3.png`。

## 新增模板缺口(ERP批次6 复盘)
无。token + 全局 override 覆盖本批全部需求,无需 slot 逃生舱或模板改动。

## Concerns
- **opLabel `el-tag` 保留(EstimateCalcView line 7)**:`effect="dark"` size=large/default,`opTagType` 5 态含 `primary`(CpTag Tone 无 primary 对应色);大号深色强调 badge,转 CpTag 破坏视觉且语义丢失。色值走 element-overrides token,无硬编码。批次4/5 opLabel 先例,判为合理保留。
- `#409eff` 判定映射:该值为旧 element primary 蓝,现全局 `--el-color-primary: var(--cp-brand)`(teal),故强调色统一映射 `var(--cp-brand)`,与设计系统一致(非改语义,是还原"跟随 primary"的原意)。
