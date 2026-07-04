<template>
  <div class="product-master">
    <!-- 顶部：操作种别 + 製品 CD + 步骤条 -->
    <el-card shadow="never" class="header-card">
      <div class="header-row">
        <div class="header-left">
          <el-tag :type="opTagType" size="large" effect="dark">
            {{ opLabel }}
          </el-tag>
          <span v-if="store.productCd" class="product-cd">
            {{ t('製品CD') }}: {{ store.productCd }}
          </span>
          <el-tag v-else-if="store.isNew" type="info" size="small" effect="plain">
            {{ t('自動採番待ち') }}
          </el-tag>
          <el-tag v-if="store.status === 1" type="warning" size="small">{{ t('承認待ち') }}</el-tag>
          <el-tag v-else-if="store.status === 9" type="success" size="small">{{ t('承認済') }}</el-tag>
          <el-tag v-if="store.mcTransferFlg" type="info" size="small">{{ t('mc転送済') }}</el-tag>
        </div>
        <div class="header-right">
          <el-radio-group v-model="opModel" size="small" :disabled="hasNoCd">
            <el-radio-button :value="ProductOperationType.New">{{ t('sales.btn.new') }}</el-radio-button>
            <el-radio-button :value="ProductOperationType.Edit" :disabled="hasNoCd">{{ t('sales.op.edit') }}</el-radio-button>
            <el-radio-button :value="ProductOperationType.Copy" :disabled="hasNoCd">{{ t('sales.op.copy') }}</el-radio-button>
            <el-radio-button :value="ProductOperationType.View" :disabled="hasNoCd">{{ t('sales.op.view') }}</el-radio-button>
            <el-radio-button :value="ProductOperationType.Delete" :disabled="hasNoCd">{{ t('sales.op.delete') }}</el-radio-button>
          </el-radio-group>
        </div>
      </div>

      <el-steps :active="store.currentStep - 1" finish-status="success" class="step-bar">
        <el-step :title="t('sales.section.partsList')" description="Step 1" />
        <el-step :title="t('sales.section.basicInfo')" description="Step 2" />
        <el-step :title="t('sales.section.process')" description="Step 3" />
        <el-step :title="t('sales.section.material')" description="Step 4" />
        <el-step :title="t('sales.section.lotPrice')" description="Step 5" />
      </el-steps>
    </el-card>

    <!-- 查询入力：按 製品 CD 加载 -->
    <el-card shadow="never" class="search-card" v-if="!store.isNew">
      <el-form inline>
        <el-form-item :label="t('sales.term.productCd')">
          <el-input v-model="searchCd" :placeholder="t('例: 0000000010001')" clearable style="width: 220px" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="loadByCd" :loading="loading">{{ t('sales.btn.load') }}</el-button>
          <el-button @click="onNewClick">{{ t('sales.btn.new') }}</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <!-- 步骤内容 -->
    <div class="step-content">
      <Step1TargetSelect v-if="store.currentStep === 1" />
      <Step2BasicInfo v-else-if="store.currentStep === 2" ref="step2Ref" />
      <Step3ProcessInfo v-else-if="store.currentStep === 3" ref="step3Ref" />
      <Step4MaterialSetting v-else-if="store.currentStep === 4" ref="step4Ref" />
      <Step5LotPriceOther v-else ref="step5Ref" />
    </div>

    <!-- 底部操作按钮 -->
    <el-card shadow="never" class="footer-card">
      <div class="btn-row">
        <el-button v-if="store.currentStep > 1" @click="onPrev">{{ t('sales.btn.prev') }}</el-button>
        <el-button
          v-if="store.currentStep < 5"
          type="primary"
          @click="onNext"
        >{{ t('sales.btn.next') }}</el-button>
        <el-button
          v-if="canSave"
          type="success"
          :loading="saving"
          @click="onSave"
        >{{ t('sales.btn.save') }}</el-button>
        <el-button
          v-if="store.isDelete"
          type="danger"
          :loading="saving"
          @click="onDelete"
        >{{ t('sales.btn.delete') }}</el-button>
        <el-button @click="onReset">{{ isStandalone ? t('閉じる') : t('sales.btn.clear') }}</el-button>
      </div>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useProductMasterStore } from '@/stores/productMaster'
import { useProductConflictHandler } from '@/composables/useProductConflictHandler'
import { ProductOperationType } from '@/types/erp/productMaster'
import { productApi } from '@/api/erp/product'
import Step1TargetSelect from './product/Step1TargetSelect.vue'
import Step2BasicInfo from './product/Step2BasicInfo.vue'
import Step3ProcessInfo from './product/Step3ProcessInfo.vue'
import Step4MaterialSetting from './product/Step4MaterialSetting.vue'
import Step5LotPriceOther from './product/Step5LotPriceOther.vue'

const { t } = useI18n()
const store = useProductMasterStore()
const route = useRoute()
const { handle: handleConflict } = useProductConflictHandler()

const isStandalone = computed(() => !!route.meta?.standalone)

const step2Ref = ref<InstanceType<typeof Step2BasicInfo> | null>(null)
const step3Ref = ref<InstanceType<typeof Step3ProcessInfo> | null>(null)
const step4Ref = ref<InstanceType<typeof Step4MaterialSetting> | null>(null)
const step5Ref = ref<InstanceType<typeof Step5LotPriceOther> | null>(null)
void step5Ref // 仅用于持有引用，保留位以便将来扩展

const searchCd = ref('')
const loading = ref(false)
const saving = ref(false)

const opModel = computed({
  get: () => store.operationType,
  set: (v: ProductOperationType) => onOpChange(v),
})

const hasNoCd = computed(() => !store.productCd)
const canSave = computed(() => store.canEdit)

const opLabel = computed(() => {
  switch (store.operationType) {
    case ProductOperationType.New: return t('sales.btn.new')
    case ProductOperationType.Edit: return t('sales.op.edit')
    case ProductOperationType.Copy: return t('sales.op.copy')
    case ProductOperationType.View: return t('sales.op.view')
    case ProductOperationType.Delete: return t('sales.op.delete')
    default: return ''
  }
})
const opTagType = computed<'primary' | 'success' | 'warning' | 'info' | 'danger'>(() => {
  switch (store.operationType) {
    case ProductOperationType.New: return 'primary'
    case ProductOperationType.Edit: return 'warning'
    case ProductOperationType.Copy: return 'success'
    case ProductOperationType.View: return 'info'
    case ProductOperationType.Delete: return 'danger'
    default: return 'info'
  }
})

function notifyOpener(type: 'saved' | 'deleted') {
  try {
    if (window.opener && !window.opener.closed) {
      window.opener.postMessage({ source: 'cp6-product', type }, window.location.origin)
    }
  } catch { /* ignore */ }
}

async function onOpChange(target: ProductOperationType) {
  if (store.isDirty) {
    try {
      await ElMessageBox.confirm(t('当前修改尚未保存，继续切换将丢弃？'), t('確認'), { type: 'warning' })
    } catch { return }
  }
  const from = store.operationType
  store.setOperationType(target)

  if (target === ProductOperationType.New) {
    store.reset()
    store.setOperationType(ProductOperationType.New)
    return
  }

  if (target === ProductOperationType.Copy && store.productCd) {
    try {
      loading.value = true
      const res = await productApi.copy(store.productCd)
      if (res.code === 0) {
        store.loadFromDto(res.data)
        store.setOperationType(ProductOperationType.Copy)
        ElMessage.success(t('流用副本を生成しました'))
      }
    } finally { loading.value = false }
    return
  }

  if (target === ProductOperationType.Delete
    || target === ProductOperationType.View
    || target === ProductOperationType.Edit) {
    if (!store.productCd) {
      ElMessage.info(t('まず製品CDを入力して読み込みしてください'))
      store.setOperationType(from)
    }
  }
}

async function loadByCd() {
  if (!searchCd.value) {
    ElMessage.warning(t('製品CDを入力してください'))
    return
  }
  try {
    loading.value = true
    const res = await productApi.getByCd(searchCd.value, store.isView || store.isDelete)
    if (res.code === 0) {
      store.loadFromDto(res.data)
      ElMessage.success(t('読み込み成功'))
    } else {
      ElMessage.warning(res.message)
    }
  } finally { loading.value = false }
}

function onNewClick() {
  store.reset()
  store.setOperationType(ProductOperationType.New)
  searchCd.value = ''
}

function onPrev() {
  if (store.currentStep > 1) store.setStep((store.currentStep - 1) as 1 | 2 | 3 | 4 | 5)
}

async function onNext() {
  // 各ステップの validate
  if (store.currentStep === 2) {
    const ok = step2Ref.value?.validate?.() ?? true
    if (!ok) return
  } else if (store.currentStep === 3) {
    const ok = step3Ref.value?.validate?.() ?? true
    if (!ok) return
  } else if (store.currentStep === 4) {
    const ok = step4Ref.value?.validate?.() ?? true
    if (!ok) return
  }
  if (store.currentStep < 5) {
    const next = (store.currentStep + 1) as 1 | 2 | 3 | 4 | 5
    store.setStep(next)
    // 進入 Step5 时自动触发仕掛チェック（既存製品の場合のみ）
    if (next === 5 && store.productCd && !store.wipCheckResult) {
      runWipCheck()
    }
  }
}

async function runWipCheck() {
  if (!store.productCd) return
  try {
    const res = await productApi.checkWip(store.productCd)
    if (res.code === 0) {
      store.wipCheckResult = res.data
    }
  } catch { /* ignore */ }
}

async function runAllValidations(): Promise<boolean> {
  // 全ステップを検証（保存時）

  // Msg 7 (E10007): 登録する部材がありません — Step 1 の部材一覧が空はエラー
  if (store.members.length === 0) {
    ElMessage.error(t('登録する部材がありません（Step 1 部材一覧に少なくとも 1 行追加してください）'))
    return false
  }

  // Msg 11 (W20011): 工程・作業がありません — 確認のうえ続行可
  if (store.processes.length === 0) {
    try {
      await ElMessageBox.confirm(
        t('工程・作業がありません。このまま保存しますか？'),
        t('確認'),
        { confirmButtonText: 'OK', cancelButtonText: t('キャンセル'), type: 'warning' },
      )
    } catch { return false }
  }

  // Msg 16 (W20016): ロット別単価がありません — 確認のうえ続行可
  if (store.lotPrices.length === 0) {
    try {
      await ElMessageBox.confirm(
        t('ロット別単価がありません。このまま保存しますか？'),
        t('確認'),
        { confirmButtonText: 'OK', cancelButtonText: t('キャンセル'), type: 'warning' },
      )
    } catch { return false }
  }

  // Step2: 必須項目（現在 Step2 を表示中ならそのコンポーネントを使用、それ以外は同等のロジック）
  const b = store.basicInfo
  if (!b.customerCd || !b.customerCd.trim()) {
    ElMessage.error(t('基本情報: 得意先 CD は必須です（Step 2）'))
    return false
  }
  if (!b.customerItemName1 || !b.customerItemName1.trim()) {
    ElMessage.error(t('基本情報: 顧客品名 1 は必須です（Step 2）'))
    return false
  }
  if (!(b.setRatio > 0)) {
    ElMessage.error(t('基本情報: セット比率は 0 より大きい値を入力してください（Step 2）'))
    return false
  }

  if (store.processes.length > 0) {
    // step3 validate (相互比率合計)
    const groupSums = new Map<string, number>()
    for (const c of store.coProducts) {
      groupSums.set(c.processCd, (groupSums.get(c.processCd) ?? 0) + (c.qtyRatio || 0))
    }
    for (const [pcd, sum] of groupSums) {
      if (Math.abs(sum - 1) > 0.0001) {
        ElMessage.error(t('工程CD {pcd} の連産品比率合計が 1.0 ではありません（現:{sum}）', { pcd, sum: sum.toFixed(4) }))
        return false
      }
    }
  }
  for (const m of store.materials) {
    if (!m.processCd || !m.materialCd || !m.materialTypeDiv) {
      ElMessage.error(t('材料設定: 必須項目（工程CD/材料CD/材料区分）が未入力です'))
      return false
    }
  }
  // ロット昇順
  const qtys = store.lotPrices.map(p => p.lotQty)
  for (let i = 1; i < qtys.length; i++) {
    if ((qtys[i] ?? 0) <= (qtys[i - 1] ?? 0)) {
      ElMessage.error(t('ロット別単価: ロット数量は昇順である必要があります（行{row}）', { row: i + 1 }))
      return false
    }
  }
  return true
}

async function onSave() {
  const ok = await runAllValidations()
  if (!ok) return

  try {
    saving.value = true
    const dto = store.buildDto()
    if (store.isNew || store.isCopy) {
      const res = await productApi.create(dto)
      if (res.code === 0) {
        store.loadFromDto(res.data)
        store.setOperationType(ProductOperationType.Edit)
        // 仕掛チェック実行（mcframe7 連携無し → 常に level=0）
        await runWipCheck()
        ElMessage.success(t('保存しました'))
        notifyOpener('saved')
      }
    } else if (store.isEdit) {
      const cd = store.productCd!
      const res = await productApi.update(cd, dto)
      if (res.code === 0) {
        store.loadFromDto(res.data)
        await runWipCheck()
        ElMessage.success(t('保存しました'))
        notifyOpener('saved')
      }
    }
  } catch (e) {
    const handled = await handleConflict(e)
    if (!handled) throw e
  } finally {
    saving.value = false
  }
}

async function onDelete() {
  try {
    await ElMessageBox.confirm(
      t('製品CD {cd} を削除します（軟削除）。よろしいですか？', { cd: store.productCd }),
      t('削除確認'),
      { type: 'warning' },
    )
  } catch { return }
  try {
    saving.value = true
    const res = await productApi.remove(store.productCd!, store.rowVersion)
    if (res.code === 0) {
      ElMessage.success(t('削除しました'))
      store.reset()
      notifyOpener('deleted')
      if (isStandalone.value) {
        try { window.close() } catch {}
      }
    }
  } catch (e) {
    const handled = await handleConflict(e)
    if (!handled) throw e
  } finally { saving.value = false }
}

function onReset() {
  if (isStandalone.value) {
    if (store.isDirty) {
      ElMessageBox.confirm(t('未保存の変更があります。閉じますか？'), t('確認'), { type: 'warning' })
        .then(() => window.close())
        .catch(() => {})
    } else {
      window.close()
    }
    return
  }
  store.reset()
  searchCd.value = ''
}

onBeforeUnmount(() => {
  // setup-style store には $reset 無 → 自前 reset() を呼ぶ
  if (typeof store.reset === 'function') store.reset()
})

// URL 参数驱动（独立窗口）
// /product/window?op=new
// /product/window?op=view&cd=000000000100001
// /product/window?op=new&quotationNo=Q-2026-0001 （MSBBPA040 から引入）
onMounted(async () => {
  const opParam = String(route.query.op || '').toLowerCase()
  const cdParam = route.query.cd ? String(route.query.cd) : ''
  const qNoParam = route.query.quotationNo ? String(route.query.quotationNo) : ''

  const opMap: Record<string, ProductOperationType> = {
    new: ProductOperationType.New,
    edit: ProductOperationType.Edit,
    view: ProductOperationType.View,
    delete: ProductOperationType.Delete,
    copy: ProductOperationType.Copy,
  }
  const op = opMap[opParam]
  if (op == null) return

  store.reset()
  store.setOperationType(op)

  if (op === ProductOperationType.New && qNoParam) {
    // 御見積書 NO による部材一覧引入
    try {
      loading.value = true
      const res = await productApi.getMembersByQuotation(qNoParam)
      if (res.code === 0) {
        store.members = res.data ?? []
        store.markDirty()
        ElMessage.success(t('{count} 件の部材を引入しました', { count: store.members.length }))
      }
    } finally { loading.value = false }
  } else if (op !== ProductOperationType.New && cdParam) {
    try {
      loading.value = true
      const res = await productApi.getByCd(cdParam, op === ProductOperationType.View || op === ProductOperationType.Delete)
      if (res.code === 0) {
        store.loadFromDto(res.data)
        store.setOperationType(op)
      } else {
        ElMessage.warning(res.message || t('製品CD={cd} 未找到', { cd: cdParam }))
      }
    } finally { loading.value = false }
  }

  if (isStandalone.value) {
    const labels: Record<number, string> = {
      [ProductOperationType.New]: t('新規'),
      [ProductOperationType.Edit]: t('訂正'),
      [ProductOperationType.View]: t('参照'),
      [ProductOperationType.Delete]: t('削除'),
      [ProductOperationType.Copy]: t('流用'),
    }
    const title = `${t('製品マスタ')} - ${labels[op] ?? ''}${cdParam ? ` - ${cdParam}` : ''}`
    try { document.title = title } catch {}
  }
})
</script>

<style scoped>
.product-master {
  padding: 12px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.header-card :deep(.el-card__body),
.search-card :deep(.el-card__body),
.footer-card :deep(.el-card__body) {
  padding: 12px 16px;
}
.header-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
}
.header-left {
  display: flex;
  align-items: center;
  gap: 12px;
}
.product-cd {
  font-size: 16px;
  font-weight: 600;
  color: var(--cp-brand);
}
.step-bar {
  margin-top: 8px;
}
.step-content {
  flex: 1;
  min-height: 400px;
}
.btn-row {
  display: flex;
  gap: 8px;
  justify-content: flex-end;
}
</style>
