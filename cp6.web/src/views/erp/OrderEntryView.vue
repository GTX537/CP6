<template>
  <div class="order-entry" :class="{ 'is-mobile': isMobile }">
    <!-- ヘッダー：操作種別 + WebOrderNo + ステップバー -->
    <el-card shadow="never" class="header-card">
      <div class="header-row">
        <div class="header-left">
          <el-tag :type="opTagType" :size="isMobile ? 'default' : 'large'" effect="dark">{{ opLabel }}</el-tag>
          <span v-if="store.order.webOrderNo" class="web-order-no">
            Web受注NO: {{ store.order.webOrderNo }}
          </span>
          <el-tag v-else-if="store.isNew" type="info" size="small" effect="plain">{{ t('sales.status.autoNumber') }}</el-tag>
          <el-tag v-if="store.order.status === 9" type="success" size="small">{{ t('sales.status.transferred') }}</el-tag>
          <el-tag v-if="store.order.mcTransferFlg" type="info" size="small">mc連携</el-tag>
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
        <el-button v-if="canSave" type="success" :loading="saving" @click="onSave">{{ t('sales.btn.save') }}</el-button>
        <el-button v-if="store.isDelete" type="danger" :loading="saving" @click="onDelete">{{ t('sales.btn.delete') }}</el-button>
        <el-button @click="onReset">{{ t('sales.btn.clear') }}</el-button>
      </div>
    </el-card>
    <!-- 手机端底部安全占位（避免内容被 sticky footer 遮挡） -->
    <div v-if="isMobile" class="footer-spacer" />
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useOrderStore } from '@/stores/order'
import { OrderOperationType } from '@/types/order'
import { orderApi } from '@/api/order'
import Step1HeaderAndDetails from './order/Step1HeaderAndDetails.vue'
import Step2BasicInfo from './order/Step2BasicInfo.vue'
import Step3ProcessInfo from './order/Step3ProcessInfo.vue'
import { useBreakpoint } from '@/composables/useBreakpoint'

const { t } = useI18n()
const store = useOrderStore()
const { isMobile } = useBreakpoint()

const searchNo = ref('')
const searchHaibaiNo1 = ref('')
const loading = ref(false)
const saving = ref(false)

const hasNoOrder = computed(() => !store.order.webOrderNo)
const canSave = computed(() => store.canEdit)

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
  loading.value = true
  try {
    if (searchNo.value) {
      const res = await orderApi.getByWebOrderNo(searchNo.value)
      if (res.code === 0 && res.data) {
        store.loadFromDto(res.data)
        store.setOperationType(OrderOperationType.Edit)
        ElMessage.success(t('sales.msg.loadSuccess'))
      }
    } else if (searchHaibaiNo1.value) {
      const res = await orderApi.lookupByHaibaiNo(searchHaibaiNo1.value)
      if (res.code === 0 && res.data) {
        store.loadFromDto(res.data)
        store.setOperationType(OrderOperationType.Edit)
        ElMessage.success(t('sales.msg.loadSuccess'))
      }
    } else {
      ElMessage.warning('Web受注NO または 手配NO1 を入力してください')
    }
  } catch {
    /* http interceptor */
  } finally {
    loading.value = false
  }
}

function onNewClick() {
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
      ElMessage.warning('部材一覧に少なくとも 1 行追加してください')
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
  if (!validateAll()) return
  // 与信チェック（W：確認後続行可）
  try {
    const totalAmount = store.order.details.reduce((s, d) => s + (d.amount ?? 0), 0)
    const credit = await orderApi.creditCheck(store.order.customerCd, totalAmount)
    if (credit.code === 0 && credit.data?.isOver) {
      try {
        await ElMessageBox.confirm(credit.data.message ?? '与信限度額を超えています。受注継続しますか？', '確認', {
          confirmButtonText: 'はい', cancelButtonText: 'いいえ', type: 'warning',
        })
      } catch { return }
    }
  } catch { /* 無視可：与信 API 失敗時 */ }

  saving.value = true
  try {
    const dto = store.buildDto()
    if (store.isNew) {
      const res = await orderApi.create(dto)
      if (res.code === 0 && res.data) {
        store.loadFromDto(res.data)
        store.setOperationType(OrderOperationType.Edit)
        ElMessage.success(t('sales.msg.saveSuccess'))
      }
    } else if (store.isEdit && store.order.webOrderNo) {
      const res = await orderApi.update(store.order.webOrderNo, dto)
      if (res.code === 0 && res.data) {
        store.loadFromDto(res.data)
        ElMessage.success(t('sales.msg.saveSuccess'))
      }
    }
  } catch {
    /* http interceptor */
  } finally {
    saving.value = false
  }
}

async function onDelete() {
  if (!store.order.webOrderNo) return
  try {
    await ElMessageBox.confirm(
      `Web受注NO ${store.order.webOrderNo} を削除します（軟削除）。よろしいですか？`,
      '削除確認', { type: 'warning' },
    )
  } catch { return }
  saving.value = true
  try {
    const res = await orderApi.remove(store.order.webOrderNo, store.order.rowVersion)
    if (res.code === 0) {
      ElMessage.success(t('sales.msg.deleteSuccess'))
      store.reset()
    }
  } catch { /* */ } finally {
    saving.value = false
  }
}

async function onReset() {
  if (store.isDirty) {
    try {
      await ElMessageBox.confirm(t('sales.msg.unsavedChanges'), t('sales.msg.confirmTitle'), { type: 'warning' })
    } catch { return }
  }
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
  box-shadow: 0 -2px 8px rgba(0,0,0,0.06);
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
