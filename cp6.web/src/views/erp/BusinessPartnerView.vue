<template>
  <div class="bp-view">
    <!-- ヘッダー：操作種別 + 検索 + ステータス -->
    <el-card shadow="never" class="header-card">
      <div class="header-row">
        <el-tag :type="opTagType" size="large" effect="dark">{{ opLabel }}</el-tag>
        <el-radio-group v-model="opModel" size="small">
          <el-radio-button :value="BpOperationType.PreRegister">{{ t('sales.op.preregister') }}</el-radio-button>
          <el-radio-button :value="BpOperationType.Register">{{ t('sales.op.register') }}</el-radio-button>
          <el-radio-button :value="BpOperationType.Edit" :disabled="!hasLoaded">{{ t('sales.op.edit') }}</el-radio-button>
          <el-radio-button :value="BpOperationType.Delete" :disabled="!hasLoaded || !store.isAdmin">{{ t('sales.op.delete') }}</el-radio-button>
          <el-radio-button :value="BpOperationType.View" :disabled="!hasLoaded">{{ t('sales.op.view') }}</el-radio-button>
        </el-radio-group>
        <el-tag v-if="store.bp.status === 0" type="info" size="small">{{ t('sales.op.preregister') }}</el-tag>
        <el-tag v-else-if="store.bp.status === 1" type="success" size="small">{{ t('sales.op.register') }}</el-tag>
        <el-tag v-else-if="store.bp.status === 9" type="danger" size="small">{{ t('sales.op.delete') }}</el-tag>
      </div>

      <el-form inline size="small" style="margin-top: 8px">
        <el-form-item :label="t('sales.term.bpCd')" required>
          <el-input v-model="store.bp.bpCd" :disabled="!isCdEditable" style="width: 200px" />
          <el-button v-if="!store.isPreReg && !store.isReg" type="primary" size="small" :loading="loading" @click="onLoad()" style="margin-left: 4px">{{ t('sales.btn.load') }}</el-button>
        </el-form-item>
        <el-form-item :label="t('sales.term.bpName')">
          <el-input v-model="store.bp.bpName" :disabled="!store.canEdit" style="width: 280px" />
        </el-form-item>
        <el-form-item :label="t('sales.term.base') + ' CD'" required>
          <el-input v-model="store.bp.baseCd" :disabled="!store.canEdit" style="width: 140px" />
        </el-form-item>
      </el-form>
    </el-card>

    <!-- Tab 1: 基本情報 + 取引分類 ；Tab 2 以降: 9 個の属性 Tab -->
    <el-tabs v-model="activeTab" type="border-card" style="margin-top: 12px">
      <!-- Tab1 -->
      <el-tab-pane :label="t('sales.section.basicInfo')" name="basic">
        <BasicInfoTab :store="store" />
      </el-tab-pane>

      <!-- 9 個の動的 Tab — FLG=ON で表示 -->
      <el-tab-pane v-if="store.bp.customerFlg" :label="t('sales.term.customer')" name="customer"><CustomerTab :store="store" /></el-tab-pane>
      <el-tab-pane v-if="store.bp.accountsReceivableFlg" :label="t('売掛先')" name="ar"><ArTab :store="store" /></el-tab-pane>
      <el-tab-pane v-if="store.bp.billingFlg" :label="t('請求先')" name="billing"><BillingTab :store="store" /></el-tab-pane>
      <el-tab-pane v-if="store.bp.receiptFlg" :label="t('入金先')" name="receipt"><ReceiptTab :store="store" /></el-tab-pane>
      <el-tab-pane v-if="store.bp.deliveryFlg" :label="t('sales.term.deliveryTo')" name="delivery"><DeliveryTab :store="store" /></el-tab-pane>
      <el-tab-pane v-if="store.bp.supplierFlg" :label="t('sales.term.supplier')" name="supplier"><SupplierTab :store="store" /></el-tab-pane>
      <el-tab-pane v-if="store.bp.accountsPayableFlg" :label="t('買掛先')" name="ap"><ApTab :store="store" /></el-tab-pane>
      <el-tab-pane v-if="store.bp.paymentScheduleFlg" :label="t('支払予定管理先')" name="paySch"><PaySchTab :store="store" /></el-tab-pane>
      <el-tab-pane v-if="store.bp.paymentFlg" :label="t('支払先')" name="payment"><PaymentTab :store="store" /></el-tab-pane>
    </el-tabs>

    <!-- Footer -->
    <el-card shadow="never" class="footer-card">
      <div class="btn-row">
        <el-button @click="onClear">{{ t('sales.btn.clear') }}</el-button>
        <el-button v-if="store.canEdit" type="primary" :loading="saving" @click="onSave">{{ t('sales.btn.register') }}</el-button>
        <el-button v-if="store.isDelete" type="danger" :loading="saving" @click="onDelete">{{ t('sales.btn.delete') }}</el-button>
      </div>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useBpStore } from '@/stores/businessPartner'
import { bpApi } from '@/api/erp/businessPartner'
import { BpOperationType } from '@/types/erp/businessPartner'
import BasicInfoTab from './bp/BasicInfoTab.vue'
import CustomerTab from './bp/CustomerTab.vue'
import ArTab from './bp/ArTab.vue'
import BillingTab from './bp/BillingTab.vue'
import ReceiptTab from './bp/ReceiptTab.vue'
import DeliveryTab from './bp/DeliveryTab.vue'
import SupplierTab from './bp/SupplierTab.vue'
import ApTab from './bp/ApTab.vue'
import PaySchTab from './bp/PaySchTab.vue'
import PaymentTab from './bp/PaymentTab.vue'

const { t } = useI18n()
const route = useRoute()
const store = useBpStore()
const activeTab = ref<string>('basic')
const loading = ref(false)
const saving = ref(false)
const hasLoaded = ref(false)

// 管理者判定（簡易：roleId=1 を localStorage から）— 本番では別途取得
store.isAdmin = (() => {
  try {
    const u = JSON.parse(localStorage.getItem('user') || '{}')
    return u.roleId === 1
  } catch { return false }
})()

const opModel = computed({
  get: () => store.operationType,
  set: v => store.setOperationType(v as BpOperationType),
})

const opLabel = computed(() => {
  switch (store.operationType) {
    case BpOperationType.PreRegister: return t('sales.op.preregister')
    case BpOperationType.Register: return t('sales.op.register')
    case BpOperationType.Edit: return t('sales.op.edit')
    case BpOperationType.Delete: return t('sales.op.delete')
    case BpOperationType.View: return t('sales.op.view')
  }
  return ''
})
const opTagType = computed<'primary' | 'warning' | 'danger' | 'info' | 'success'>(() => {
  switch (store.operationType) {
    case BpOperationType.PreRegister: return 'info'
    case BpOperationType.Register: return 'primary'
    case BpOperationType.Edit: return 'warning'
    case BpOperationType.Delete: return 'danger'
    case BpOperationType.View: return 'info'
  }
  return 'info'
})

// 取引先 CD は事前登録/登録時のみ入力可（仕様書 §7）
const isCdEditable = computed(() => store.isPreReg || store.isReg)

async function onLoad(skipDirtyCheck = false) {
  if (!store.bp.bpCd) {
    ElMessage.warning(t('取引先 CD を入力してください'))
    return
  }
  // 未保存の編集がある状態で再読込すると編集内容が失われるため確認する
  if (!skipDirtyCheck && store.isDirty) {
    try {
      await ElMessageBox.confirm(t('sales.msg.unsavedChanges'), t('sales.msg.confirmTitle'), { type: 'warning' })
    } catch { return }
  }
  loading.value = true
  try {
    const r = await bpApi.getByCd(store.bp.bpCd)
    if (r.code === 0 && r.data) {
      store.loadFromDto(r.data)
      hasLoaded.value = true
      ElMessage.success(t('sales.msg.loadSuccess'))
    } else {
      ElMessage.warning(t('sales.err.E10008'))
    }
  } finally {
    loading.value = false
  }
}

// 一覧画面からの遷移：route.query.bpCd で自動ロード、mode で参照/訂正を設定
onMounted(async () => {
  const bpCd = route.query.bpCd as string | undefined
  if (!bpCd) return
  store.bp.bpCd = bpCd
  await onLoad()
  if (hasLoaded.value) {
    const mode = route.query.mode === 'edit'
      ? BpOperationType.Edit
      : BpOperationType.View
    store.setOperationType(mode)
  }
})

async function onSave() {
  // 必須/9 FLG チェック
  if (!store.bp.bpName?.trim()) { ElMessage.error(t('取引先名は必須です')); return }
  if (!store.bp.baseCd?.trim()) { ElMessage.error(t('拠点 CD は必須です')); return }
  if (!store.hasAnyFlg) { ElMessage.error(t('9 個の属性 FLG のいずれかを選択してください')); return }

  // 訂正時 FLG 変更不可フロントガード
  if (store.isEdit) {
    const changed = store.flgChangedOnEdit()
    if (changed.length > 0) {
      ElMessage.error(t('以下の FLG は訂正時に変更できません: {list}', { list: changed.join(', ') }))
      return
    }
  }

  saving.value = true
  try {
    if (store.isPreReg || store.isReg) {
      const r = await bpApi.create(store.bp, store.isPreReg)
      if (r.code === 0 && r.data) {
        store.loadFromDto(r.data)
        hasLoaded.value = true
        ElMessage.success(t('sales.msg.saveSuccess'))
        store.setOperationType(BpOperationType.Edit)
      }
    } else if (store.isEdit) {
      const r = await bpApi.update(store.bp.bpCd, store.bp)
      if (r.code === 0 && r.data) {
        store.loadFromDto(r.data)
        ElMessage.success(t('sales.msg.saveSuccess'))
      }
    }
  } catch (e: any) {
    // 楽観ロック競合（409/E10034）：interceptor は 409 を黙殺するため、ここで明示通知する
    const status = e?.response?.status
    if (status === 409) {
      const msg = e?.response?.data?.message
        || t('すでに他のユーザーに更新されています。最新を再取得してください')
      try {
        await ElMessageBox.confirm(
          msg + '\n' + t('最新データを再取得しますか？（現在の編集内容は破棄されます）'),
          t('更新競合'), { type: 'warning', confirmButtonText: t('再取得'), cancelButtonText: t('キャンセル') },
        )
        await onLoad(true)  // 確認済みのため dirty 再確認はスキップ。最新 rowVersion で再読込（再編集すれば保存可能）
      } catch { /* キャンセル時は編集内容を保持 */ }
    }
    // 401 / その他のステータスは interceptor 側で通知済みのため二重表示しない
  } finally {
    saving.value = false
  }
}

async function onDelete() {
  try {
    await ElMessageBox.confirm(
      t('取引先 {cd} を削除します。よろしいですか？', { cd: store.bp.bpCd }),
      t('削除確認'), { type: 'warning' },
    )
  } catch { return }
  saving.value = true
  try {
    const r = await bpApi.remove(store.bp.bpCd, store.bp.rowVersion)
    if (r.code === 0) {
      ElMessage.success(t('sales.msg.deleteSuccess'))
      store.reset()
      hasLoaded.value = false
    }
  } finally {
    saving.value = false
  }
}

async function onClear() {
  if (store.isDirty) {
    try {
      await ElMessageBox.confirm(t('sales.msg.unsavedChanges'), t('sales.msg.confirmTitle'), { type: 'warning' })
    } catch { return }
  }
  store.reset()
  hasLoaded.value = false
  activeTab.value = 'basic'
}

// 発注先パターン連動
watch(() => store.bp.supplierPattern, () => store.applySupplierPatternRules())
// メーカ FLG 連動
watch(() => store.bp.makerFlg, () => store.applyMakerFlgRules())
// 有償支給先 FLG 連動
watch(() => store.bp.paidSupplyFlg, () => store.applyPaidSupplyFlgRules())

// FLG 変化を監視 → ON でない Tab がアクティブになっていた場合は basic に戻す
watch(() => [store.bp.customerFlg, store.bp.accountsReceivableFlg, store.bp.billingFlg,
              store.bp.receiptFlg, store.bp.deliveryFlg, store.bp.supplierFlg,
              store.bp.accountsPayableFlg, store.bp.paymentScheduleFlg, store.bp.paymentFlg], () => {
  const flgMap: Record<string, boolean> = {
    customer: store.bp.customerFlg,
    ar: store.bp.accountsReceivableFlg,
    billing: store.bp.billingFlg,
    receipt: store.bp.receiptFlg,
    delivery: store.bp.deliveryFlg,
    supplier: store.bp.supplierFlg,
    ap: store.bp.accountsPayableFlg,
    paySch: store.bp.paymentScheduleFlg,
    payment: store.bp.paymentFlg,
  }
  if (activeTab.value !== 'basic' && !flgMap[activeTab.value]) {
    activeTab.value = 'basic'
  }
})
</script>

<style scoped>
.bp-view { padding: 16px; }
.header-card, .footer-card { margin-bottom: 12px; }
.header-row { display: flex; gap: 12px; align-items: center; flex-wrap: wrap; }
.btn-row { display: flex; gap: 8px; justify-content: flex-end; }
</style>
