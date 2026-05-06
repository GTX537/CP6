<template>
  <div class="bp-view">
    <!-- ヘッダー：操作種別 + 検索 + ステータス -->
    <el-card shadow="never" class="header-card">
      <div class="header-row">
        <el-tag :type="opTagType" size="large" effect="dark">{{ opLabel }}</el-tag>
        <el-radio-group v-model="opModel" size="small">
          <el-radio-button :value="BpOperationType.PreRegister">事前登録</el-radio-button>
          <el-radio-button :value="BpOperationType.Register">登録</el-radio-button>
          <el-radio-button :value="BpOperationType.Edit" :disabled="!hasLoaded">訂正</el-radio-button>
          <el-radio-button :value="BpOperationType.Delete" :disabled="!hasLoaded || !store.isAdmin">削除</el-radio-button>
          <el-radio-button :value="BpOperationType.View" :disabled="!hasLoaded">参照</el-radio-button>
        </el-radio-group>
        <el-tag v-if="store.bp.status === 0" type="info" size="small">事前登録</el-tag>
        <el-tag v-else-if="store.bp.status === 1" type="success" size="small">本登録</el-tag>
        <el-tag v-else-if="store.bp.status === 9" type="danger" size="small">削除済</el-tag>
      </div>

      <el-form inline size="small" style="margin-top: 8px">
        <el-form-item label="取引先 CD" required>
          <el-input v-model="store.bp.bpCd" :disabled="!isCdEditable" style="width: 200px" />
          <el-button v-if="!store.isPreReg && !store.isReg" type="primary" size="small" :loading="loading" @click="onLoad" style="margin-left: 4px">読込</el-button>
        </el-form-item>
        <el-form-item label="取引先名">
          <el-input v-model="store.bp.bpName" :disabled="!store.canEdit" style="width: 280px" />
        </el-form-item>
        <el-form-item label="拠点 CD" required>
          <el-input v-model="store.bp.baseCd" :disabled="!store.canEdit" style="width: 140px" />
        </el-form-item>
      </el-form>
    </el-card>

    <!-- Tab 1: 基本情報 + 取引分類 ；Tab 2 以降: 9 個の属性 Tab -->
    <el-tabs v-model="activeTab" type="border-card" style="margin-top: 12px">
      <!-- Tab1 -->
      <el-tab-pane label="基本情報・取引分類" name="basic">
        <BasicInfoTab :store="store" />
      </el-tab-pane>

      <!-- 9 個の動的 Tab — FLG=ON で表示 -->
      <el-tab-pane v-if="store.bp.customerFlg" label="得意先" name="customer"><CustomerTab :store="store" /></el-tab-pane>
      <el-tab-pane v-if="store.bp.accountsReceivableFlg" label="売掛先" name="ar"><ArTab :store="store" /></el-tab-pane>
      <el-tab-pane v-if="store.bp.billingFlg" label="請求先" name="billing"><BillingTab :store="store" /></el-tab-pane>
      <el-tab-pane v-if="store.bp.receiptFlg" label="入金先" name="receipt"><ReceiptTab :store="store" /></el-tab-pane>
      <el-tab-pane v-if="store.bp.deliveryFlg" label="納品先" name="delivery"><DeliveryTab :store="store" /></el-tab-pane>
      <el-tab-pane v-if="store.bp.supplierFlg" label="発注先" name="supplier"><SupplierTab :store="store" /></el-tab-pane>
      <el-tab-pane v-if="store.bp.accountsPayableFlg" label="買掛先" name="ap"><ApTab :store="store" /></el-tab-pane>
      <el-tab-pane v-if="store.bp.paymentScheduleFlg" label="支払予定管理先" name="paySch"><PaySchTab :store="store" /></el-tab-pane>
      <el-tab-pane v-if="store.bp.paymentFlg" label="支払先" name="payment"><PaymentTab :store="store" /></el-tab-pane>
    </el-tabs>

    <!-- Footer -->
    <el-card shadow="never" class="footer-card">
      <div class="btn-row">
        <el-button @click="onClear">クリア</el-button>
        <el-button v-if="store.canEdit" type="primary" :loading="saving" @click="onSave">登録</el-button>
        <el-button v-if="store.isDelete" type="danger" :loading="saving" @click="onDelete">削除実行</el-button>
      </div>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useBpStore } from '@/stores/businessPartner'
import { bpApi } from '@/api/businessPartner'
import { BpOperationType } from '@/types/businessPartner'
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
    case BpOperationType.PreRegister: return '事前登録'
    case BpOperationType.Register: return '登録'
    case BpOperationType.Edit: return '訂正'
    case BpOperationType.Delete: return '削除'
    case BpOperationType.View: return '参照'
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

async function onLoad() {
  if (!store.bp.bpCd) {
    ElMessage.warning('取引先 CD を入力してください')
    return
  }
  loading.value = true
  try {
    const r = await bpApi.getByCd(store.bp.bpCd)
    if (r.code === 0 && r.data) {
      store.loadFromDto(r.data)
      hasLoaded.value = true
      ElMessage.success('取引先を読込みました')
    } else {
      ElMessage.warning('E10008: 検索結果がありません')
    }
  } finally {
    loading.value = false
  }
}

async function onSave() {
  // 必須/9 FLG チェック
  if (!store.bp.bpName?.trim()) { ElMessage.error('E10022: 取引先名は必須です'); return }
  if (!store.bp.baseCd?.trim()) { ElMessage.error('E10022: 拠点 CD は必須です'); return }
  if (!store.hasAnyFlg) { ElMessage.error('E10030: 9 個の属性 FLG のいずれかを選択してください'); return }

  // 訂正時 FLG 変更不可フロントガード
  if (store.isEdit) {
    const changed = store.flgChangedOnEdit()
    if (changed.length > 0) {
      ElMessage.error(`E10033: 以下の FLG は訂正時に変更できません: ${changed.join(', ')}`)
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
        ElMessage.success('登録しました')
        store.setOperationType(BpOperationType.Edit)
      }
    } else if (store.isEdit) {
      const r = await bpApi.update(store.bp.bpCd, store.bp)
      if (r.code === 0 && r.data) {
        store.loadFromDto(r.data)
        ElMessage.success('訂正しました')
      }
    }
  } finally {
    saving.value = false
  }
}

async function onDelete() {
  try {
    await ElMessageBox.confirm(
      `取引先 ${store.bp.bpCd} を削除します。よろしいですか？`,
      '削除確認', { type: 'warning' },
    )
  } catch { return }
  saving.value = true
  try {
    const r = await bpApi.remove(store.bp.bpCd, store.bp.rowVersion)
    if (r.code === 0) {
      ElMessage.success('削除しました')
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
      await ElMessageBox.confirm('未保存の変更があります。クリアしますか？', '確認', { type: 'warning' })
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
