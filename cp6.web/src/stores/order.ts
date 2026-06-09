import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { OrderOperationType } from '@/types/erp/order'
import type {
  OrderDto,
  OrderDetailDto,
  OrderProcessDto,
  OrderMaterialDto,
  OrderProcessNoteDto,
  OrderWipCheckResultDto,
} from '@/types/erp/order'

/** 空 OrderDto 工厂 */
function emptyOrder(): OrderDto {
  return {
    customerCd: '',
    orderType: '',
    details: [],
    status: 0,
    mcTransferFlg: false,
  }
}

/** 空 OrderDetailDto 工厂（webOrderDetailNo は呼び出し側で割当） */
export function emptyOrderDetail(rowNo: number, productCd = ''): OrderDetailDto {
  return {
    webOrderDetailNo: rowNo,
    productCd,
    status: 0,
    wfApprovalFlg: false,
    mcTransferFlg: false,
    provisionalPriceFlg: false,
    processes: [],
    processNotes: [],
    materials: [],
  }
}

/**
 * MSBBPA070 受注入力 — 3 ページ Wizard store
 *
 * 3 ページ間で状態を保持し、最終 第3画面「保存」ボタンで 1 度に POST/PUT。
 * 仕様書 §11 T_Order / T_OrderDetail / T_OrderProcess / T_OrderProcessNote / T_OrderMaterial 5 表 1-shot。
 */
export const useOrderStore = defineStore('order', () => {
  // ───── 操作モード / ナビゲーション ─────
  const operationType = ref<OrderOperationType>(OrderOperationType.New)
  const currentStep = ref<1 | 2 | 3>(1)

  // ───── ヘッダー（第1画面 基本情報） ─────
  const order = ref<OrderDto>(emptyOrder())

  /** 第1画面 部材一覧 — 多明細（製品単位）= details */
  // ※ details は order.details の参照 — 同期化は computed 経由で行う
  const details = computed({
    get: () => order.value.details,
    set: v => { order.value.details = v },
  })

  /** 現在 Step2/Step3 で編集中の明細（部材一覧で選択した行） */
  const currentDetailIndex = ref<number>(-1)
  const currentDetail = computed<OrderDetailDto | null>(() => {
    if (currentDetailIndex.value < 0 || currentDetailIndex.value >= order.value.details.length) return null
    return order.value.details[currentDetailIndex.value] ?? null
  })

  // ───── 業務状態 ─────
  /** isEditable フラグ（受注画面制御 — 構成・工程編集可） */
  const isEditableFlag = ref<boolean>(true)
  /** 仕掛チェック結果（mcframe7 連携無し → 常に level=0） */
  const wipCheckResult = ref<OrderWipCheckResultDto | null>(null)

  /** 未保存マーカー */
  const isDirty = ref<boolean>(false)

  // ───── 派生 ─────
  const isNew = computed(() => operationType.value === OrderOperationType.New)
  const isEdit = computed(() => operationType.value === OrderOperationType.Edit)
  const isDelete = computed(() => operationType.value === OrderOperationType.Delete)
  const isView = computed(() => operationType.value === OrderOperationType.View)

  /** 受注画面制御マトリクス — 全画面読み取り専用（参照/削除モード） */
  const isPageReadOnly = computed(() => isView.value || isDelete.value)
  /** 編集系操作モード（New/Edit） */
  const canEdit = computed(() => isNew.value || isEdit.value)
  /** PK 系（受注区分・得意先・手配NO）— 基本：登録・訂正・削除・参照すべて編集可（仕様書 §9） */
  const isPkEditable = computed(() => true)

  // ───── アクション ─────
  function setOperationType(op: OrderOperationType) {
    operationType.value = op
  }

  function setStep(step: 1 | 2 | 3) {
    currentStep.value = step
  }

  /** サーバから取得した DTO を全フィールドに展開 */
  function loadFromDto(dto: OrderDto) {
    order.value = {
      ...emptyOrder(),
      ...dto,
      details: dto.details ?? [],
    }
    currentDetailIndex.value = order.value.details.length > 0 ? 0 : -1
    isDirty.value = false
  }

  /** 現在状態を 1 個の DTO に組み立て */
  function buildDto(): OrderDto {
    return {
      ...order.value,
      details: [...order.value.details],
    }
  }

  /** ストアを初期状態にリセット */
  function reset() {
    operationType.value = OrderOperationType.New
    currentStep.value = 1
    order.value = emptyOrder()
    currentDetailIndex.value = -1
    isEditableFlag.value = true
    wipCheckResult.value = null
    isDirty.value = false
  }

  function markDirty() {
    isDirty.value = true
  }

  // ───── 部材一覧（第1画面）操作 ─────

  function addDetail(seed: Partial<OrderDetailDto> = {}) {
    const nextRowNo = order.value.details.length === 0
      ? 1
      : Math.max(...order.value.details.map(d => d.webOrderDetailNo)) + 1
    const d: OrderDetailDto = {
      ...emptyOrderDetail(nextRowNo),
      ...seed,
    }
    order.value.details.push(d)
    currentDetailIndex.value = order.value.details.length - 1
    isDirty.value = true
  }

  function removeDetail(rowNo: number) {
    order.value.details = order.value.details.filter(d => d.webOrderDetailNo !== rowNo)
    // 連番再振り
    order.value.details.forEach((d, i) => (d.webOrderDetailNo = i + 1))
    if (currentDetailIndex.value >= order.value.details.length) {
      currentDetailIndex.value = order.value.details.length - 1
    }
    isDirty.value = true
  }

  function moveDetail(rowNo: number, direction: 'up' | 'down') {
    const idx = order.value.details.findIndex(d => d.webOrderDetailNo === rowNo)
    if (idx < 0) return
    const swapIdx = direction === 'up' ? idx - 1 : idx + 1
    if (swapIdx < 0 || swapIdx >= order.value.details.length) return
    const a = order.value.details[idx]!
    const b = order.value.details[swapIdx]!
    const tmp = a.webOrderDetailNo
    a.webOrderDetailNo = b.webOrderDetailNo
    b.webOrderDetailNo = tmp
    order.value.details[idx] = b
    order.value.details[swapIdx] = a
    isDirty.value = true
  }

  /** 部材一覧 行コピー（原紙のみ有効：仕様書 §4） */
  function copyDetail(rowNo: number) {
    const idx = order.value.details.findIndex(d => d.webOrderDetailNo === rowNo)
    if (idx < 0) return
    const src = order.value.details[idx]
    const copy: OrderDetailDto = JSON.parse(JSON.stringify(src))
    copy.webOrderDetailNo = order.value.details.length + 1
    copy.haibaiNo1 = undefined // 新規採番対象
    copy.status = 0
    copy.wfApprovalFlg = false
    copy.mcTransferFlg = false
    order.value.details.push(copy)
    currentDetailIndex.value = order.value.details.length - 1
    isDirty.value = true
  }

  function clearForm() {
    order.value = emptyOrder()
    currentDetailIndex.value = -1
    isDirty.value = false
  }

  // ───── 第2/3 画面（明細単位）の更新 ─────

  function updateCurrentDetail(patch: Partial<OrderDetailDto>) {
    if (!currentDetail.value) return
    Object.assign(currentDetail.value, patch)
    isDirty.value = true
  }

  function setCurrentDetailProcesses(processes: OrderProcessDto[]) {
    if (!currentDetail.value) return
    currentDetail.value.processes = processes
    isDirty.value = true
  }

  function setCurrentDetailMaterials(materials: OrderMaterialDto[]) {
    if (!currentDetail.value) return
    currentDetail.value.materials = materials
    isDirty.value = true
  }

  function setCurrentDetailNotes(notes: OrderProcessNoteDto[]) {
    if (!currentDetail.value) return
    currentDetail.value.processNotes = notes
    isDirty.value = true
  }

  return {
    // state
    operationType,
    currentStep,
    order,
    details,
    currentDetailIndex,
    currentDetail,
    isEditableFlag,
    wipCheckResult,
    isDirty,
    // computed
    isNew,
    isEdit,
    isDelete,
    isView,
    isPageReadOnly,
    canEdit,
    isPkEditable,
    // actions
    setOperationType,
    setStep,
    loadFromDto,
    buildDto,
    reset,
    markDirty,
    addDetail,
    removeDetail,
    moveDetail,
    copyDetail,
    clearForm,
    updateCurrentDetail,
    setCurrentDetailProcesses,
    setCurrentDetailMaterials,
    setCurrentDetailNotes,
  }
})
