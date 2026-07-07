# ERP 迁移批次7 报告——ProductMaster 向导 + product steps token化

分支: `feat/ui-migrate-erp` | 样式基准: 特殊页/token 化(向导/编辑 FORM 页——el-steps/el-form/可编辑明细表保留,视觉走全局 override + token)

## 盘点表(6 文件)

| # | 文件 | 形态 | 硬编码/el-tag 发现 | 处置 | 字段/校验/步骤/i18n |
|---|------|------|-------------------|------|----------------------|
| 1 | ProductMasterView.vue | 向导 shell(header opLabel + 状态徽章群 + 操作种别 radio(5态) + el-steps(5) + 按 CD 检索 + footer) | scoped `.product-cd{color:#409eff}`;opLabel `el-tag`(effect=dark,size=large,5态含 primary);4× 状态 `el-tag`(info-plain 自動採番待ち / warning 承認待ち / success 承認済 / info mc転送済) | `#409eff`→`var(--cp-brand)`;opLabel el-tag **保留**(唯一豁免,primary/dark);4× 状态 el-tag→**CpTag**(info/warn/ok/info) | 全保留:opModel/onOpChange(5×N 矩阵含 New reset·Copy 拉取·Delete/View/Edit 无号提示)、loadByCd、onNewClick、onPrev/onNext(Step2/3/4 validate 门控 + Step5 进入 runWipCheck)、runAllValidations(E10007 部材空·W20011 工程空·W20016 ロット空·Step2 必填·连产品比率合计·材料必填·ロット昇順)、onSave(create/update+乐观锁 handleConflict 409+runWipCheck+notifyOpener)、onDelete(remove+rowVersion+确认)、onReset(standalone window.close/isDirty)、onBeforeUnmount reset、onMounted URL 参数驱动(op/cd/quotationNo+document.title)、5 步 el-steps、所有 t() 词条 |
| 2 | product/Step1TargetSelect.vue | 向导 step(检索表单 + 部材一覧 el-table + 工具栏 el-button-group + wipTag + 2 Master Popup) | 内联 `<el-icon color="#67c23a">`(WF Check)、`<el-icon color="#409eff">`(連携 Link);`el-tag` wipCheckResult(success/warning/danger)、行内状态 `el-tag`(statusTagType info/warning/success) | `#67c23a`→`var(--cp-ok)`;`#409eff`→`var(--cp-brand)`;wip el-tag→**CpTag**(`wipTone`: ok/warn/danger)、状态 el-tag→**CpTag**(`statusTone`: ok/warn/info,分支逻辑逐一镜像) | 全保留:openCustomer/ProductPicker+isCustomer 类型守卫+onPickCustomer/Product(セット品顧客对齐)、行选择 onCurrentRowChange、onRemoveSelected、onMoveSelected(up/down 保选中)、onClearForm(确认)、onOpenEstimateDetail(window.open)、statusLabel/statusTone(原 statusTagType 改名换 Tone)、wipTone/wipLabel(原 wipTagType 改名换 Tone)、onLoadFromQuotation、store.addMember/removeMember/moveMember、全部列/prop |
| 3 | product/Step2BasicInfo.vue | 向导 step(見積計算書引入 + 8 区块 el-form:顧客案件/品名品番/原紙構成/寸法/売価運賃/FSC容リ/戦略×10/備考 + Master Popup) | 内联 `<span style="color:#909399">`(引入提示) | `#909399`→`var(--cp-muted)` | 全保留:onLoadFromEstimateCalc(合并保护 strategicDivs)、onPickCustomer、strategicSel bool[]⇄index[] computed、isReadOnlyByOrder tooltip 分支、defineExpose validate(得意先/顧客品名1/親子区分/売価区分/セット比率)、全部字段/required/maxlength/precision |
| 4 | product/Step3ProcessInfo.vue | 向导 step(工程明細 el-table(~20列含 popover 仕様×10/製造順×8) + 連産品 el-dialog) | 内联 `<span style="color:#909399">`(トムソン系提示);连产品 `el-tag`(coRatioOk success/danger) | `#909399`→`var(--cp-muted)`;连产品比率 el-tag→**CpTag**(`:tone="coRatioOk ? 'ok' : 'danger'"`) | 全保留:isCoProductable(0600/0601/0602)、specsSummary/priosSummary、addProcess(sortOrder+10)、removeProcess、reSort、連産品 dialog(currentCoList/coRatioSum/coRatioOk/openCoProductDialog/addCoProduct/removeCoProduct)、defineExpose validate(比率合计=1.0)、全部列/precision/controls |
| 5 | product/Step4MaterialSetting.vue | 向导 step(材料明細 el-table + 工具栏) | 内联 `<span style="color:#909399">`(材料区分提示) | `#909399`→`var(--cp-muted)` | 全保留:processOptions computed(工程CD 下拉源)、addMaterial(sortOrder+10)、removeMaterial、reSort、defineExpose validate(工程CD/材料CD/材料区分必填)、全部列/select 选项 |
| 6 | product/Step5LotPriceOther.vue | 向导 step(ロット別単価 el-table(昇順) + その他情報 el-form + 仕掛チェック el-alert/el-empty) | 内联 `<span style="color:#909399">`(価格提示) | `#909399`→`var(--cp-muted)` | 全保留:alertType/alertTitle(level 0-3)、addLotPrice(detailNo+1)、removeLotPrice、defineExpose validate(ロット数量昇順)、el-alert(仕掛番号)/el-empty 空态、全部字段/date-picker |

## 迁移摘要
- 6 文件全部改动,均为**纯 CSS/内联样式 token 化**,零逻辑改动。
- `#409eff`(旧 element primary 蓝,现全局 `--el-color-primary: var(--cp-brand)`)→ `var(--cp-brand)`:2 处(ProductMasterView `.product-cd`、Step1 連携 Link 图标)。
- `#67c23a`(element success 绿)→ `var(--cp-ok)`:1 处(Step1 WF Check 图标)。`el-icon color` prop 接受 CSS 变量,内联 `style="color: var(...)"` 解析正常。
- `#909399`(element `--el-text-color-secondary`)→ `var(--cp-muted)`:4 处(Step2/3/4/5 各 1 处提示 span)。
- **el-tag→CpTag:7 处无损转换**(评审修正 fix commit):ProductMasterView 4× 状态徽章(info-plain/warning/success/info → tone info/warn/ok/info)、Step1 wip tag(wipTone: ok/warn/danger)+行内状态 tag(statusTone: ok/warn/info)、Step3 连产品比率 tag(coRatioOk ? ok : danger)。分支逻辑逐一镜像,文案/条件不变。**唯一豁免 = opLabel**(effect=dark + 5态含 primary,CpTag Tone 无 primary 对应)。初版误引批次6先例整体保留——批次6报告原文为"无 info/success/warning 无损候选",豁免仅限 primary/dark,已修正。
- 无 sticky-footer 阴影(footer 为普通 el-card,无 box-shadow),故本批未用 `--cp-shadow-up`。
- 未改后端/API/路由/i18n 机制;无 `:key` 新增;未动模板组件本体;无模板逃生舱新增。

## 验证证据
- `npm run type-check`: 0 error(vue-tsc --build)。
- `npm run test`: 46 files / **316 passed**(基准保持)。
- 真栈走查(admin 已登录, dev 5173 + VITE_API_TARGET 9991, `POST /api/auth/login` 200):
  - `/product` 向导 Step1(新規):header opLabel "新規" 渲染为 brand 深色 pill、"自動採番待ち" info-plain tag、5 步 el-steps(brand teal 高亮)、部材一覧 el-table 空态 "No Data"、工具栏 el-button-group 可点。
  - Step1→次へ 进 Step2;Step2 必填校验门控(空值时 "次へ" 被 validate 阻止——校验逻辑保留);填 得意先CD/顧客品名1(セット比率默认 1.0)后正常前进。
  - Step2 引入提示 span 计算色 = `rgb(140,163,171)`(=`--cp-muted`,token 生效)。
  - Step3/Step4/Step5 各自提示 span 计算色均 = `rgb(140,163,171)`(=`--cp-muted`)。
  - Step5:仕掛チェック 未実行 el-empty 空态渲染正确;ロット別単価 el-table 空态 "No Data"。
  - console 仅既存无关噪声(intlify flatten / Vue Router next() deprecation / SignalR CSRF 403 通知连接 / el-pagination `small` deprecation),**无本批新错误**(纯 CSS 变更不产生 JS 错误)。walk 期间偶发 1 次背景 401(与 CSS 变更无关,纯样式改动不触发 401)。
  - 截图:`shots/erp-product-step1.png` ~ `erp-product-step5.png`(5 张)。

## 新增模板缺口(ERP批次7 复盘)
无。token + 全局 override 覆盖本批全部需求,无需 slot 逃生舱或模板改动。

## Concerns
- **opLabel el-tag 保留(ProductMasterView line 7,唯一豁免)**:`effect="dark"` size=large,`opTagType` 5态含 `primary`(CpTag Tone 无 primary 对应色),大号深色强调 badge,转 CpTag 破坏视觉+丢语义。批次4/5/6 opLabel 先例。色值走 element-overrides token,无硬编码。
- 其余 7 处 el-tag 已按评审意见转 CpTag(见迁移摘要);初版报告"整体保留"引用批次6先例不成立(批次6为"无无损候选",非"候选可保留"),此处更正。
- CpTag 无 size prop(统一 pill 视觉),原 `size="small"` 差异被设计系统统一吸收;wip tag 的 `margin-left:12px` 内联布局样式保留。
- 真栈补充验证(fix commit):`/product` shell "自動採番待ち" 渲染 `.cp-tag.t-info`(color=rgb(78,128,238)=--cp-info,bg=--cp-info-bg);Step1 行状态 "未作成" `.cp-tag.t-info` 正确;console 无新错误;截图 `shots/erp-product-step1-cptag.png`。wip tag(需已存在製品 CD 的 wipCheckResult)与 Step3 比率 tag(需 0600 系工程行开弹窗)在 QA 数据不建单前提下未真栈触达——tone 分支逐一镜像原 tagType 逻辑,类型经 vue-tsc 校验。
- `#409eff` 映射同批次6:该值为旧 element primary 蓝,现全局 `--el-color-primary: var(--cp-brand)`,故统一映射 `var(--cp-brand)`(还原"跟随 primary"原意,非改语义)。
