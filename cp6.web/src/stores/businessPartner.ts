import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { BpOperationType } from '@/types/businessPartner'
import type { BusinessPartnerDto } from '@/types/businessPartner'

/** 空 BP DTO 工厂（仕様書 §6.6 共通初期値含む） */
function emptyBp(): BusinessPartnerDto {
  return {
    bpCd: '',
    bpName: '',
    baseCd: '',
    status: 1,
    customerFlg: false,
    accountsReceivableFlg: false,
    billingFlg: false,
    receiptFlg: false,
    deliveryFlg: false,
    supplierFlg: false,
    accountsPayableFlg: false,
    paymentScheduleFlg: false,
    paymentFlg: false,
    creditMgmtFlg: false,
    makerFlg: false,
    paidSupplyFlg: false,
    rebuyObligationFlg: false,
    subcontractTargetFlg: false,
    supplyPriceChangeAllowFlg: false,
    deliveryConfirmFlg: true,
    purchaseTaxPriorityFlg: false,
    paidSupplyTaxPriorityFlg: false,
    paidEachTimeFlg: false,
    paymentTaxCalcFlg: false,
    batchPaymentScheduleFlg: false,
    gifuInterfaceFlg: false,
    mcTransferFlg: false,
    purchaseFractionDiv: '3',
    purchaseTaxFractionDiv: '3',
    purchaseTaxCd: 'P010',
    supplierCalendarCd: 'CAL01',
    supplyConsignDiv: '1',
    purchaseLotSplitDiv: '1',
    purchasePostingDiv: '2',
  }
}

/**
 * MSBBPA110 取引先マスタ ストア
 * - 9 個の属性 FLG が各 Tab 表示制御
 * - 訂正時 FLG 変更不可（バックエンドで MSG-018 に変換）
 * - 発注先パターン連動：パターン=外注品(5) 時のみ「下請対象/外注単価区分/支給単価変更許可」が有効
 */
export const useBpStore = defineStore('businessPartner', () => {
  const operationType = ref<BpOperationType>(BpOperationType.Register)
  const bp = ref<BusinessPartnerDto>(emptyBp())
  /** 訂正前のスナップショット（FLG 変更不可チェック用、UI 側ガード） */
  const original = ref<BusinessPartnerDto | null>(null)
  const isDirty = ref(false)
  const isAdmin = ref(false)        // 削除モード可否（外部から注入）

  const isPreReg  = computed(() => operationType.value === BpOperationType.PreRegister)
  const isReg     = computed(() => operationType.value === BpOperationType.Register)
  const isEdit    = computed(() => operationType.value === BpOperationType.Edit)
  const isDelete  = computed(() => operationType.value === BpOperationType.Delete)
  const isView    = computed(() => operationType.value === BpOperationType.View)
  const canEdit   = computed(() => isPreReg.value || isReg.value || isEdit.value)
  const isPageReadOnly = computed(() => isView.value || isDelete.value)

  // 属性 FLG が一つも ON でないと登録不可（仕様書 §8 No.2）
  const hasAnyFlg = computed(() =>
    bp.value.customerFlg || bp.value.accountsReceivableFlg || bp.value.billingFlg
    || bp.value.receiptFlg || bp.value.deliveryFlg || bp.value.supplierFlg
    || bp.value.accountsPayableFlg || bp.value.paymentScheduleFlg || bp.value.paymentFlg)

  // 発注先パターン連動（仕様書 §6.6）
  const isOutsourcing = computed(() => bp.value.supplierPattern === '5')

  function setOperationType(op: BpOperationType) {
    operationType.value = op
  }

  function loadFromDto(dto: BusinessPartnerDto) {
    bp.value = { ...emptyBp(), ...dto }
    original.value = JSON.parse(JSON.stringify(bp.value))
    isDirty.value = false
  }

  function reset() {
    bp.value = emptyBp()
    original.value = null
    operationType.value = BpOperationType.Register
    isDirty.value = false
  }

  function markDirty() { isDirty.value = true }

  /** FLG 変更不可（13 種）— UI 警告のためのフロント側ガード */
  function flgChangedOnEdit(): string[] {
    if (!isEdit.value || !original.value) return []
    const o = original.value
    const c = bp.value
    const out: string[] = []
    const cmp = (k: keyof BusinessPartnerDto, label: string) => {
      if (o[k] !== c[k]) out.push(label)
    }
    cmp('customerFlg', '得意先FLG')
    cmp('accountsReceivableFlg', '売掛先FLG')
    cmp('billingFlg', '請求先FLG')
    cmp('receiptFlg', '入金先FLG')
    cmp('supplierFlg', '発注先FLG')
    cmp('accountsPayableFlg', '買掛先FLG')
    cmp('paymentScheduleFlg', '支払予定管理先FLG')
    cmp('paymentFlg', '支払先FLG')
    cmp('creditMgmtFlg', '与信管理先FLG')
    cmp('deliveryFlg', '納品先FLG')
    cmp('makerFlg', 'メーカFLG')
    cmp('paidSupplyFlg', '有償支給先FLG')
    cmp('rebuyObligationFlg', '買戻義務FLG')
    return out
  }

  /** 発注先パターン変更時の値リセット（仕様書 §6.6） */
  function applySupplierPatternRules() {
    const isOut = bp.value.supplierPattern === '5'
    if (!isOut) {
      bp.value.subcontractTargetFlg = false
      bp.value.subcontractPriceDiv = undefined
      bp.value.supplyPostingDiv = undefined
      bp.value.paidSupplyFlg = false
      bp.value.rebuyObligationFlg = false
    } else {
      // 外注品：固定値セット
      bp.value.supplyPostingDiv = '2'        // 着荷基準
      bp.value.subcontractPriceDiv = '1'     // 加工単価+有償支給単価
      bp.value.supplyPriceChangeAllowFlg = true
    }
  }

  /** メーカ FLG 連動（仕様書 §6.6） */
  function applyMakerFlgRules() {
    if (!bp.value.makerFlg) bp.value.fscCertificationDiv = undefined
  }

  /** 有償支給先 FLG 連動（仕様書 §6.6） */
  function applyPaidSupplyFlgRules() {
    if (!bp.value.paidSupplyFlg) {
      bp.value.supplyPostingDiv = undefined
      bp.value.paidSupplyTaxCd = undefined
      bp.value.paidSupplyTaxPriorityFlg = false
      bp.value.paidSupplyAmountFractionDiv = undefined
      bp.value.paidSupplyTaxFractionDiv = undefined
    }
  }

  /** Tab 切替時に FLG=OFF の Tab データをクリア（仕様書 §12 ル UI 要点） */
  function clearTabIfFlgOff() {
    if (!bp.value.customerFlg) {
      bp.value.parentCustomerCd = undefined
      bp.value.userConverterDiv = undefined
      bp.value.accountsReceivableCd = undefined
      bp.value.creditMgmtCd = undefined
      // ... 簡略：必要に応じて追加
    }
    if (!bp.value.deliveryFlg) {
      bp.value.logisticsGroupCd = undefined
      bp.value.logisticsGroupName = undefined
    }
  }

  return {
    operationType, bp, original, isDirty, isAdmin,
    isPreReg, isReg, isEdit, isDelete, isView,
    canEdit, isPageReadOnly, hasAnyFlg, isOutsourcing,
    setOperationType, loadFromDto, reset, markDirty,
    flgChangedOnEdit, applySupplierPatternRules,
    applyMakerFlgRules, applyPaidSupplyFlgRules, clearTabIfFlgOff,
  }
})
