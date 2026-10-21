<template>
  <div class="order-entry" :class="{ 'is-mobile': isMobile }">
    <!-- ヘッダー：操作種別 + WebOrderNo + ステップバー -->
    <el-card shadow="never" class="header-card">
      <div class="header-row">
        <div class="header-left">
          <el-tag :type="opTagType" :size="isMobile ? 'default' : 'large'" effect="dark">{{ opLabel }}</el-tag>
          <span v-if="store.order.webOrderNo" class="web-order-no">
            {{ t('Web受注NO') }}: {{ store.order.webOrderNo }}
          </span>
          <CpTag v-else-if="store.isNew" tone="info">{{ t('sales.status.autoNumber') }}</CpTag>
          <CpTag v-if="store.order.status === 9" tone="ok">{{ t('sales.status.transferred') }}</CpTag>
          <CpTag v-if="store.order.mcTransferFlg" tone="info">{{ t('mc連携') }}</CpTag>
        </div>
        <div class="header-right">
          <el-radio-group v-model="opModel" size="small" :disabled="hasNoOrder">
            <el-radio-button :value="OrderOperationType.New">{{ t('sales.op.register') }}</el-radio-button>
            <el-radio-button :value="OrderOperationType.Edit" :disabled="hasNoOrder">{{ t('sales.op.edit') }}</el-radio-button>
            <el-radio-button :value="OrderOperationType.Delete" :disabled="hasNoOrder">{{ t('sales.op.delete') }}</el-radio-button>
            <el-radio-button :value="OrderOperationType.View" :disabled="hasNoOrder">{{ t('sales.op.view') }}</el-radio-button>
          </el-radio-group>
        </div>
      </div>

      <!-- 桌面端：完整 steps；手机端：紧凑 simple 模式 -->
      <el-steps
        :active="store.currentStep - 1"
        finish-status="success"
        class="step-bar"
        :simple="isMobile"
      >
        <el-step :title="isMobile ? 'Step 1' : t('sales.section.basicInfo') + '・' + t('sales.section.orderDetail')" :description="isMobile ? '' : 'Step 1'" />
        <el-step :title="isMobile ? 'Step 2' : t('sales.section.basicInfo') + '・' + t('sales.section.composition') + '・' + t('sales.section.notes')" :description="isMobile ? '' : 'Step 2'" />
        <el-step :title="isMobile ? 'Step 3' : t('sales.section.process') + '・' + t('sales.section.material')" :description="isMobile ? '' : 'Step 3'" />
      </el-steps>
    </el-card>

    <!-- 検索：手配NO で既存受注読込 -->
    <el-card shadow="never" class="search-card" v-if="!store.isNew">
      <el-form inline>
        <el-form-item :label="t('Web受注NO')">
          <el-input v-model="searchNo" :placeholder="t('例: WO20260501000001')" clearable style="width: 240px" />
        </el-form-item>
        <el-form-item :label="t('sales.term.haibaiNo') + '1'">
          <el-input v-model="searchHaibaiNo1" clearable style="width: 200px" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="onLoad" :loading="loading">{{ t('sales.btn.load') }}</el-button>
          <el-button @click="onNewClick">{{ t('sales.btn.new') }}</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <!-- ステップ内容 -->
    <div class="step-content">
      <Step1HeaderAndDetails v-if="store.currentStep === 1" />
      <Step2BasicInfo v-else-if="store.currentStep === 2" />
      <Step3ProcessInfo v-else />
    </div>

    <!-- 底部 -->
    <el-card shadow="never" class="footer-card">
      <div class="btn-row">
        <el-button v-if="store.currentStep > 1" @click="onPrev">{{ t('sales.btn.prev') }}</el-button>
        <el-button v-if="store.currentStep < 3" type="primary" @click="onNext">{{ t('sales.btn.next') }}</el-button>
        <el-button v-if="canSave" :key="store.isNew ? 'create' : 'edit'" type="success" :loading="saving" @click="onSave">{{ t('sales.btn.save') }}</el-button>
        <el-button v-if="store.isDelete" v-permission="'erp-order:del'" type="danger" :loading="saving" @click="onDelete">{{ t('sales.btn.delete') }}</el-button>
        <el-button @click="onReset">{{ t('sales.btn.clear') }}</el-button>
      </div>
    </el-card>
    <!-- 手机端底部安全占位（避免内容被 sticky footer 遮挡） -->
    <div v-if="isMobile" class="footer-spacer" />
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onBeforeUnmount } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useOrderStore } from '@/stores/order'
import { usePermissionStore } from '@/stores/permission'
import { OrderOperationType } from '@/types/erp/order'
import { orderApi } from '@/api/erp/order'
import Step1HeaderAndDetails from './order/Step1HeaderAndDetails.vue'
import Step2BasicInfo from './order/Step2BasicInfo.vue'
import Step3ProcessInfo from './order/Step3ProcessInfo.vue'
import { useBreakpoint } from '@/composables/useBreakpoint'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import CpTag from '@/components/base/CpTag.vue'

const { t } = useI18n()
const store = useOrderStore()
store.ensureCurrentIdentity()
const { isMobile } = useBreakpoint()

const searchNo = ref('')
const searchHaibaiNo1 = ref('')
const loading = ref(false)
const saving = ref(false)
let generation = 0, pageIdentity = identityStamp()
function identityChanged() {
  if (pageIdentity === identityStamp()) return
  pageIdentity = identityStamp(); generation++
  store.reset(); searchNo.value = ''; searchHaibaiNo1.value = ''; loading.value = false
}
window.addEventListener(AUTH_EVENT, identityChanged)
window.addEventListener('storage', identityChanged)
onBeforeUnmount(() => {
  generation++
  window.removeEventListener(AUTH_EVENT, identityChanged)
  window.removeEventListener('storage', identityChanged)
})

const hasNoOrder = computed(() => !store.order.webOrderNo)
const permissions = usePermissionStore()
// Vue must own this conditional node: a directive that removes it cannot restore it when New changes to Edit.
const canSave = computed(() => store.canEdit && (!permissions.loaded || permissions.has(store.isNew ? 'erp-order:add' : 'erp-order:edit')))

const opModel = computed({
  get: () => store.operationType,
  set: v => store.setOperationType(v),
})

const opLabel = computed(() => {
  switch (store.operationType) {
    case OrderOperationType.New: return t('sales.op.register')
    case OrderOperationType.Edit: return t('sales.op.edit')
    case OrderOperationType.Delete: return t('sales.op.delete')
    case OrderOperationType.View: return t('sales.op.view')
    default: return ''
  }
})
const opTagType = computed<'primary' | 'warning' | 'danger' | 'info' | 'success'>(() => {
  switch (store.operationType) {
    case OrderOperationType.New: return 'primary'
    case OrderOperationType.Edit: return 'warning'
    case OrderOperationType.Delete: return 'danger'
    case OrderOperationType.View: return 'info'
    default: return 'info'
  }
})

async function onLoad() {
  if (!searchNo.value && !searchHaibaiNo1.value) {
    ElMessage.warning(t('Web受注NO または 手配NO1 を入力してください')); return
  }
  const id = ++generation, stamp = identityStamp()
  const number = searchNo.value, haibai = searchHaibaiNo1.value
  if (store.isDirty) {
    try { await ElMessageBox.confirm(t('sales.msg.unsavedChanges'), t('sales.msg.confirmTitle'), { type: 'warning' }) }
    catch { return }
    if (id !== generation || stamp !== identityStamp()) return
  }
  store.reset(); loading.value = true
  try {
    const res = number ? await orderApi.getByWebOrderNo(number) : await orderApi.lookupByHaibaiNo(haibai)
    requireIdentity(stamp)
    if (id !== generation) return
    if (res.code === 0 && res.data) {
      store.loadFromDto(res.data); store.setOperationType(OrderOperationType.Edit)
      ElMessage.success(t('sales.msg.loadSuccess'))
    }
  } catch { /* Previous protected data was cleared before reading. */ }
  finally { if (id === generation) loading.value = false }
}

function onNewClick() {
  generation++; loading.value = false
  store.reset()
  store.setOperationType(OrderOperationType.New)
}

function onPrev() {
  if (store.currentStep > 1) store.setStep((store.currentStep - 1) as 1 | 2 | 3)
}

async function onNext() {
  // Step 1 → Step 2 / Step 2 → Step 3 共通：明細選択行が必要
  if (store.currentStep === 1) {
    if (store.order.details.length === 0) {
      ElMessage.warning(t('部材一覧に少なくとも 1 行追加してください'))
      return
    }
    if (store.currentDetailIndex < 0) {
      // 最初の行を選択
      store.currentDetailIndex = 0
    }
  }
  if (store.currentStep < 3) {
    store.setStep((store.currentStep + 1) as 1 | 2 | 3)
  }
}

function validateAll(): boolean {
  const o = store.order
  if (!o.customerCd?.trim()) { ElMessage.error(t('sales.err.E10022') + ': ' + t('sales.term.customerCd')); return false }
  if (!o.orderType?.trim()) { ElMessage.error(t('sales.err.E10022') + ': ' + t('sales.term.orderType')); return false }
  if (o.details.length === 0) { ElMessage.error(t('sales.err.E10009')); return false }
  for (const d of o.details) {
    if (!d.productCd?.trim()) { ElMessage.error(t('sales.err.E10022') + `: ${d.webOrderDetailNo} - ` + t('sales.term.productCd')); return false }
  }
  return true
}

async function onSave() {
  if (saving.value || loading.value || !validateAll()) return
  const stamp = identityStamp(), id = generation
  const dto = JSON.parse(JSON.stringify(store.buildDto())) as ReturnType<typeof store.buildDto>
  const creating = store.isNew, editing = store.isEdit, number = store.order.webOrderNo
  saving.value = true
  try {
    // Hold the single write slot across credit checks and confirmation too.
    try {
      const total = dto.details.reduce((sum, detail) => sum + (detail.amount ?? 0), 0)
      const credit = await orderApi.creditCheck(dto.customerCd, total)
      if (id !== generation || stamp !== identityStamp()) return
      if (credit.code === 0 && credit.data?.isOver) {
        try {
          await ElMessageBox.confirm(credit.data.message ?? t('与信限度額を超えています。受注継続しますか？'), t('確認'), {
            confirmButtonText: t('はい'), cancelButtonText: t('いいえ'), type: 'warning',
          })
        } catch { return }
      }
    } catch { /* Existing advisory credit check; server write authorization remains mandatory. */ }
    if (id !== generation || stamp !== identityStamp()) return
    if (JSON.stringify(store.buildDto()) !== JSON.stringify(dto)) {
      ElMessage.warning(t('内容が変更されたため保存を中止しました。変更内容を確認して保存してください。'))
      return
    }
    const result = creating ? await orderApi.create(dto) : editing && number ? await orderApi.update(number, dto) : null
    requireIdentity(stamp)
    if (id !== generation || !result || result.code !== 0) return
    if (!result.data) {
      store.reset()
      ElMessage.success(t('保存しました。詳細は現在読み取れません。再送信せず、権限を確認してください。'))
      return
    }
    if (JSON.stringify(store.buildDto()) !== JSON.stringify(dto)) {
      // This exact write committed, but later edits must remain an unsaved draft of that order.
      store.order.webOrderNo = result.data.webOrderNo
      store.order.rowVersion = result.data.rowVersion
      if (creating) store.setOperationType(OrderOperationType.Edit)
      store.markDirty()
      ElMessage.warning(t('送信した内容は保存されました。その後の変更は未保存のまま保持しています。'))
      return
    }
    store.loadFromDto(result.data)
    if (creating) store.setOperationType(OrderOperationType.Edit)
    ElMessage.success(t('sales.msg.saveSuccess'))
  } catch {
    /* HTTP interceptor reports the error; never automatically replay a write. */
  } finally { saving.value = false }
}

async function onDelete() {
  if (saving.value || loading.value || !store.order.webOrderNo) return
  const stamp = identityStamp(), id = generation
  const number = store.order.webOrderNo, version = store.order.rowVersion
  saving.value = true
  try {
    try {
      await ElMessageBox.confirm(t('Web受注NO {no} を削除します（軟削除）。よろしいですか？', { no: number }),
        t('削除確認'), { type: 'warning' })
    } catch { return }
    if (id !== generation || stamp !== identityStamp()) return
    const result = await orderApi.remove(number, version)
    requireIdentity(stamp)
    if (id !== generation) return
    if (result.code === 0) { ElMessage.success(t('sales.msg.deleteSuccess')); store.reset() }
  } catch { /* No replay; the current draft must not be cleared by a failed/old request. */ }
  finally { saving.value = false }
}

async function onReset() {
  if (store.isDirty) {
    try {
      await ElMessageBox.confirm(t('sales.msg.unsavedChanges'), t('sales.msg.confirmTitle'), { type: 'warning' })
    } catch { return }
  }
  generation++; loading.value = false
  store.reset()
  searchNo.value = ''
  searchHaibaiNo1.value = ''
}
</script>

<style scoped>
.order-entry { padding: 16px; }
.header-card, .search-card, .footer-card { margin-bottom: 12px; }
.header-row { display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; }
.header-left { display: flex; gap: 12px; align-items: center; flex-wrap: wrap; }
.web-order-no { font-weight: 600; font-size: 14px; }
.step-bar { margin-top: 8px; }
.step-content { min-height: 400px; }
.btn-row { display: flex; gap: 8px; justify-content: flex-end; flex-wrap: wrap; }

/* 手机端 */
.order-entry.is-mobile {
  padding: 12px;
  padding-bottom: 100px; /* sticky footer 占位 */
}
.order-entry.is-mobile .header-row {
  flex-direction: column;
  align-items: stretch;
  gap: 10px;
}
.order-entry.is-mobile .header-left {
  font-size: 13px;
}
.order-entry.is-mobile .web-order-no {
  font-size: 13px;
}
.order-entry.is-mobile .header-right :deep(.el-radio-group) {
  display: flex;
  width: 100%;
}
.order-entry.is-mobile .header-right :deep(.el-radio-button) {
  flex: 1;
}
.order-entry.is-mobile .header-right :deep(.el-radio-button__inner) {
  width: 100%;
  padding: 8px 4px;
  font-size: 12px;
}
.order-entry.is-mobile .footer-card {
  position: fixed;
  bottom: 0;
  left: 0;
  right: 0;
  margin: 0;
  border-radius: 0;
  z-index: 50;
  padding-bottom: env(safe-area-inset-bottom, 0);
  box-shadow: var(--cp-shadow-up);
}
.order-entry.is-mobile .footer-card :deep(.el-card__body) {
  padding: 10px 12px;
}
.order-entry.is-mobile .btn-row {
  justify-content: stretch;
  gap: 6px;
}
.order-entry.is-mobile .btn-row :deep(.el-button) {
  flex: 1;
  margin-left: 0 !important;
}
.footer-spacer { height: 20px; }
</style>
