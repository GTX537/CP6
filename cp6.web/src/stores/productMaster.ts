import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { ProductOperationType } from '@/types/productMaster'
import type {
  ProductDto,
  ProductMemberDto,
  ProductBasicInfoDto,
  ProductProcessDto,
  ProductMaterialDto,
  ProductLotPriceDto,
  ProductCoProductDto,
  WipCheckResultDto,
} from '@/types/productMaster'

/** 空 BasicInfo 工厂 */
function emptyBasicInfo(): ProductBasicInfoDto {
  return {
    customerCd: '',
    setProductCd: '',
    parentChildDiv: '0',
    setRatio: 1,
    salesPriceDiv: '2',
    freightBilling: '0',
    strategicDivs: Array(10).fill(false),
  }
}

/** 空 ProductDto 工厂 */
function emptyProduct(): ProductDto {
  return {
    members: [],
    basicInfo: emptyBasicInfo(),
    processes: [],
    coProducts: [],
    materials: [],
    lotPrices: [],
    status: 0,
    wfApprovalFlg: false,
    mcTransferFlg: false,
  }
}

/**
 * MSBBPA050 製品マスタ 5 ページ ウィザード store
 *
 * 5 ページ間で状態を保持し、最終的に第5页「保存」で 1 度に POST/PUT する。
 * 仕様書 §16.2 ProductMasterState に準拠。
 */
export const useProductMasterStore = defineStore('productMaster', () => {
  // ───── 操作モード / ナビゲーション ─────
  const operationType = ref<ProductOperationType>(ProductOperationType.New)
  const currentStep = ref<1 | 2 | 3 | 4 | 5>(1)

  // ───── 5 ページの状態 ─────
  /** 第1页：部材一覧（多行 = 多製品 CD） */
  const members = ref<ProductMemberDto[]>([])
  /** 第2页：基本情報 */
  const basicInfo = ref<ProductBasicInfoDto>(emptyBasicInfo())
  /** 第3页：工程情報 */
  const processes = ref<ProductProcessDto[]>([])
  /** 第3页 Popup：連産品（工程 CD → 行のリスト） */
  const coProducts = ref<ProductCoProductDto[]>([])
  /** 第4页：材料設定 */
  const materials = ref<ProductMaterialDto[]>([])
  /** 第5页：ロット別単価 */
  const lotPrices = ref<ProductLotPriceDto[]>([])

  // ───── サーバ側状態 ─────
  /** 製品 CD（PK） */
  const productCd = ref<string | undefined>(undefined)
  const itemCd = ref<string | undefined>(undefined)
  const branch1 = ref<string | undefined>(undefined)
  const branch2 = ref<string | undefined>(undefined)
  const branch3 = ref<string | undefined>(undefined)
  /** ステータス：0=承認待ち / 1=承認済み / 9=mc転送済 */
  const status = ref<number>(0)
  const wfApprovalFlg = ref<boolean>(false)
  const mcTransferFlg = ref<boolean>(false)
  /** 楽観锁トークン */
  const rowVersion = ref<string | undefined>(undefined)

  /** 仕掛チェック 結果（第10章；mcframe7 無し → 常に level=0） */
  const wipCheckResult = ref<WipCheckResultDto | null>(null)

  /** 未保存マーカー */
  const isDirty = ref<boolean>(false)

  // ───── 派生 ─────
  const isNew = computed(() => operationType.value === ProductOperationType.New)
  const isEdit = computed(() => operationType.value === ProductOperationType.Edit)
  const isDelete = computed(() => operationType.value === ProductOperationType.Delete)
  const isView = computed(() => operationType.value === ProductOperationType.View)
  const isCopy = computed(() => operationType.value === ProductOperationType.Copy)
  /** ページ全体が読み取り専用（参照/削除モード） */
  const isPageReadOnly = computed(() => isView.value || isDelete.value)
  /** PK 系フィールドの編集可（基本：登録のみ） */
  const isPkEditable = computed(() => isNew.value)
  /** 編集系操作モード（New/Edit/Copy） */
  const canEdit = computed(() => isNew.value || isEdit.value || isCopy.value)

  // ───── アクション ─────
  function setOperationType(op: ProductOperationType) {
    operationType.value = op
  }

  function setStep(step: 1 | 2 | 3 | 4 | 5) {
    currentStep.value = step
  }

  /** サーバから取得した DTO を全フィールドに展開 */
  function loadFromDto(dto: ProductDto) {
    productCd.value = dto.productCd
    itemCd.value = dto.itemCd
    branch1.value = dto.branch1
    branch2.value = dto.branch2
    branch3.value = dto.branch3
    members.value = dto.members ?? []
    basicInfo.value = {
      ...emptyBasicInfo(),
      ...dto.basicInfo,
      strategicDivs: dto.basicInfo?.strategicDivs ?? Array(10).fill(false),
    }
    processes.value = dto.processes ?? []
    coProducts.value = dto.coProducts ?? []
    materials.value = dto.materials ?? []
    lotPrices.value = dto.lotPrices ?? []
    status.value = dto.status ?? 0
    wfApprovalFlg.value = dto.wfApprovalFlg ?? false
    mcTransferFlg.value = dto.mcTransferFlg ?? false
    rowVersion.value = dto.rowVersion
    isDirty.value = false
  }

  /** 现在の 5 ページ状態を 1 個の DTO に組み立て（保存時に呼ぶ） */
  function buildDto(): ProductDto {
    return {
      productCd: productCd.value,
      itemCd: itemCd.value,
      branch1: branch1.value,
      branch2: branch2.value,
      branch3: branch3.value,
      members: members.value,
      basicInfo: basicInfo.value,
      processes: processes.value,
      coProducts: coProducts.value,
      materials: materials.value,
      lotPrices: lotPrices.value,
      status: status.value,
      wfApprovalFlg: wfApprovalFlg.value,
      mcTransferFlg: mcTransferFlg.value,
      rowVersion: rowVersion.value,
    }
  }

  /** ストアを初期状態にリセット（ページを閉じる/別の操作モードを開始する時） */
  function reset() {
    operationType.value = ProductOperationType.New
    currentStep.value = 1
    members.value = []
    basicInfo.value = emptyBasicInfo()
    processes.value = []
    coProducts.value = []
    materials.value = []
    lotPrices.value = []
    productCd.value = undefined
    itemCd.value = undefined
    branch1.value = undefined
    branch2.value = undefined
    branch3.value = undefined
    status.value = 0
    wfApprovalFlg.value = false
    mcTransferFlg.value = false
    rowVersion.value = undefined
    wipCheckResult.value = null
    isDirty.value = false
  }

  function markDirty() {
    isDirty.value = true
  }

  /** 部材一覧 1 行追加（第1页） */
  function addMember(seed: Partial<ProductMemberDto> = {}) {
    const nextRowNo = members.value.length === 0
      ? 1
      : Math.max(...members.value.map(m => m.rowNo)) + 1
    members.value.push({
      rowNo: nextRowNo,
      status: 0,
      wfApproved: false,
      masterLinked: false,
      ...seed,
    })
    isDirty.value = true
  }

  /** 部材一覧 1 行削除 */
  function removeMember(rowNo: number) {
    members.value = members.value.filter(m => m.rowNo !== rowNo)
    // 連番を詰め直す（rowNo は連続性が前提）
    members.value.forEach((m, i) => (m.rowNo = i + 1))
    isDirty.value = true
  }

  /**
   * 部材一覧 行入れ替え（▲ / ▼ ボタン）
   * 仕様書 §処理概要 No.6/7：1行目の▲は何もしない / 最終行の▼は何もしない
   */
  function moveMember(rowNo: number, direction: 'up' | 'down') {
    const idx = members.value.findIndex(m => m.rowNo === rowNo)
    if (idx < 0) return
    const swapIdx = direction === 'up' ? idx - 1 : idx + 1
    if (swapIdx < 0 || swapIdx >= members.value.length) return
    const a = members.value[idx]
    const b = members.value[swapIdx]
    // 行番号を入れ替え
    const tmp = a.rowNo
    a.rowNo = b.rowNo
    b.rowNo = tmp
    // 配列上の順序も入れ替え（表示順）
    members.value[idx] = b
    members.value[swapIdx] = a
    isDirty.value = true
  }

  /** 第1页 入力エリア + 部材一覧をクリア（操作種別は維持） */
  function clearForm() {
    basicInfo.value = emptyBasicInfo()
    members.value = []
    processes.value = []
    coProducts.value = []
    materials.value = []
    lotPrices.value = []
    isDirty.value = false
  }

  return {
    // state
    operationType,
    currentStep,
    members,
    basicInfo,
    processes,
    coProducts,
    materials,
    lotPrices,
    productCd,
    itemCd,
    branch1,
    branch2,
    branch3,
    status,
    wfApprovalFlg,
    mcTransferFlg,
    rowVersion,
    wipCheckResult,
    isDirty,
    // computed
    isNew,
    isEdit,
    isDelete,
    isView,
    isCopy,
    isPageReadOnly,
    isPkEditable,
    canEdit,
    // actions
    setOperationType,
    setStep,
    loadFromDto,
    buildDto,
    reset,
    markDirty,
    addMember,
    removeMember,
    moveMember,
    clearForm,
  }
})
