import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { OperationType } from '@/types/erp/estimateCalc'
import type { EstimateCalcDto, EstimateCalcProcessDto } from '@/types/erp/estimateCalc'

/** 新建时的默认值 */
function emptyEstimate(): EstimateCalcDto {
  return {
    qtnDate: new Date().toISOString().slice(0, 10),
    strategicDivs: Array(10).fill(false),
    estimateQtys: Array(8).fill(0),
    palletCnts: Array(8).fill(0),
    processes: [],
  }
}

/**
 * 見積計算書 store
 * 负责跨 Step 1/2/3 的数据共享与操作种别状态管理
 */
export const useEstimateStore = defineStore('estimate', () => {
  // ================ 状态 ================
  /** 操作种别：新建/编辑/删除/查看/流用 */
  const operationType = ref<OperationType>(OperationType.New)

  /** 当前向导步骤：1=基本信息, 2=工程明细, 3=计算结果 */
  const currentStep = ref<number>(1)

  /** 主档数据（第 1/2 页共享） */
  const basicInfo = ref<EstimateCalcDto>(emptyEstimate())

  /** 工程明细（第 2 页编辑） */
  const processRows = ref<EstimateCalcProcessDto[]>([])

  /** 计算结果缓存（第 3 页） */
  const calcResult = ref<Record<string, any>>({})

  /** リサイクル法（A/B/C）弹窗回写结果 */
  const recycleResult = ref<{ a?: any; b?: any; c?: any }>({})

  /** 是否有未保存的修改 */
  const isDirty = ref<boolean>(false)

  // ================ 计算属性 ================
  /** 是否新建模式 */
  const isNew = computed(() => operationType.value === OperationType.New)
  /** 是否编辑模式 */
  const isEdit = computed(() => operationType.value === OperationType.Edit)
  /** 是否删除模式 */
  const isDelete = computed(() => operationType.value === OperationType.Delete)
  /** 是否查看模式 */
  const isView = computed(() => operationType.value === OperationType.View)
  /** 是否流用模式 */
  const isCopy = computed(() => operationType.value === OperationType.Copy)
  /** 只读态（View / Delete） */
  const isReadOnly = computed(() => isView.value || isDelete.value)
  /** 是否允许编辑（E 或 R 字段可输入） */
  const canEdit = computed(() => isNew.value || isEdit.value || isCopy.value)

  // ================ 动作 ================
  /** 切换操作种别（5×4 矩阵核心切换） */
  function setOperationType(op: OperationType) {
    operationType.value = op
  }

  /** 切换步骤 */
  function setStep(step: number) {
    currentStep.value = step
  }

  /** 加载主档（编辑/查看/删除/流用时调用） */
  function loadBasicInfo(dto: EstimateCalcDto) {
    basicInfo.value = {
      ...emptyEstimate(),
      ...dto,
      strategicDivs: dto.strategicDivs ?? Array(10).fill(false),
      estimateQtys: dto.estimateQtys ?? Array(8).fill(0),
      palletCnts: dto.palletCnts ?? Array(8).fill(0),
    }
    processRows.value = dto.processes ?? []
    isDirty.value = false
  }

  /** 重置整个 store（关闭页面/导航时调用） */
  function reset() {
    operationType.value = OperationType.New
    currentStep.value = 1
    basicInfo.value = emptyEstimate()
    processRows.value = []
    calcResult.value = {}
    recycleResult.value = {}
    isDirty.value = false
  }

  /** 标记变更 */
  function markDirty() {
    isDirty.value = true
  }

  return {
    // state
    operationType,
    currentStep,
    basicInfo,
    processRows,
    calcResult,
    recycleResult,
    isDirty,
    // computed
    isNew,
    isEdit,
    isDelete,
    isView,
    isCopy,
    isReadOnly,
    canEdit,
    // actions
    setOperationType,
    setStep,
    loadBasicInfo,
    reset,
    markDirty,
  }
})
