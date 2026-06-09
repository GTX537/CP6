import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { PlateMoldOperationType } from '@/types/erp/plateMold'
import type { PlateMoldDto, PlateMoldHistoryItemDto } from '@/types/erp/plateMold'

function emptyDto(): PlateMoldDto {
  const today = new Date().toISOString().slice(0, 10)
  const tenYears = (() => { const d = new Date(); d.setFullYear(d.getFullYear() + 10); return d.toISOString().slice(0, 10) })()
  return {
    wdRev: 1,
    baseCd: '',
    decisionEstimateNo: '',
    vrsnName: '',
    stDate: today,
    endDate: tenYears,
    customerCd: '',
    representativeProductCd: '',
    duplicatePlateFlg: false,
    sheetWidth: 0, sheetFlow: 0, bladeWidth: 0, bladeFlow: 0,
    compositionQty: 0, mfgQty: 1, colorQty: 0, bookQty: 0,
    supplierCd: '',
    estimateAmount: 0, decisionAmount: 0, purchaseAmount: 0,
    wdQty: 0, limitWdQty: 0,
    atachInfoSheetFront: false, atachInfoSheetBack: false,
    atachInfoActual: false, atachInfoBaseplate: false,
    atachInfoPositive: false, atachInfoNegative: false,
    atachInfoMo: false, atachInfoFd: false,
    needDraft: 0, needMylar: 0, needGalley: 0, needProof: 0,
    needBlueprint: 0, needComp: 0, needDesignSheet: 0,
    status: 0, mcTransferFlg: false,
  }
}

export const usePlateMoldStore = defineStore('plateMold', () => {
  const operationType = ref<PlateMoldOperationType>(PlateMoldOperationType.Register)
  const dto = ref<PlateMoldDto>(emptyDto())
  const history = ref<PlateMoldHistoryItemDto[]>([])
  const isDirty = ref(false)
  const isAdmin = ref(true) // 簡略：admin role 持ちと仮定。本来は user state より参照

  const isRegister = computed(() => operationType.value === PlateMoldOperationType.Register)
  const isRevise   = computed(() => operationType.value === PlateMoldOperationType.Revise)
  const isEdit     = computed(() => operationType.value === PlateMoldOperationType.Edit)
  const isDelete   = computed(() => operationType.value === PlateMoldOperationType.Delete)
  const isView     = computed(() => operationType.value === PlateMoldOperationType.View)
  const isPageReadOnly = computed(() => isView.value || isDelete.value)
  const canEdit    = computed(() => isRegister.value || isRevise.value || isEdit.value)
  /** 仕様書 §10：版型名は登録/訂正で R、改定では RO */
  const canEditVrsnName = computed(() => isRegister.value || isEdit.value)

  function setOperationType(op: PlateMoldOperationType) { operationType.value = op }
  function loadFromDto(d: PlateMoldDto) { dto.value = { ...emptyDto(), ...d }; isDirty.value = false }
  function reset() {
    operationType.value = PlateMoldOperationType.Register
    dto.value = emptyDto()
    history.value = []
    isDirty.value = false
  }
  function markDirty() { isDirty.value = true }
  /** 工程→版型分類の自動セット（仕様書 §5） */
  function autoTypeClassFromProcess() {
    if (!dto.value.processCd) return
    const map: Record<string, string> = {
      '0300': 'ゴム型', '0450': 'オフ型', '0600': '木型', '0650': '箔版',
    }
    if (map[dto.value.processCd]) dto.value.typeClass = map[dto.value.processCd]
  }

  return {
    // state
    operationType, dto, history, isDirty, isAdmin,
    // computed
    isRegister, isRevise, isEdit, isDelete, isView,
    isPageReadOnly, canEdit, canEditVrsnName,
    // actions
    setOperationType, loadFromDto, reset, markDirty,
    autoTypeClassFromProcess,
  }
})
