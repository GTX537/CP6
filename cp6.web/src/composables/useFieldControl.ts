import { computed } from 'vue'
import { OperationType, type FieldState } from '@/types/erp/estimateCalc'
import { useEstimateStore } from '@/stores/estimate'

/**
 * 操作种别 × 字段 的控制矩阵
 * E  = Editable / Enabled
 * RO = ReadOnly
 * D  = Disabled
 * R  = Required
 *
 * 默认规则：
 *   View / Delete  → 所有字段 RO
 *   New / Copy     → 所有字段 E（必填位 R）
 *   Edit           → 主键 RO、其余 E/R
 */

/** 主键类字段（新建时自动采番，编辑时不可改） */
const PK_FIELDS = new Set<string>([
  'qtnCalcNo',
  'qtnCalcNoMain',
  'qtnCalcNoBranch',
])

/** 系统字段（全状态 RO） */
const SYSTEM_FIELDS = new Set<string>([
  'creator',
  'createDate',
  'modifier',
  'modifyDate',
  'rowVersion',
  'isDeleted',
])

/** 必填字段集合（基本信息页 19 条核心必填） */
const REQUIRED_FIELDS = new Set<string>([
  'proCd',
  'qtnDate',
  'qtnBaseCd',
  'orderBaseCd',
  'staffCd',
  'customerCd',
  'orderType',
  'productCategoryBig',
  'productCategoryMid',
  'customerProductName1',
  'orderQty',
  'parentChildDiv',
  'sheetFlute',
  'paperCdF',
  'printCdF',
  'finalMachineProc',
  'productShape1',
  'distDiv',
  'unit',
])

export function useFieldControl() {
  const store = useEstimateStore()

  /**
   * 取得指定字段在当前操作种别下的状态
   */
  function stateOf(field: string): FieldState {
    // 系统字段：全状态 RO
    if (SYSTEM_FIELDS.has(field)) return 'RO'

    // View / Delete：所有字段 RO
    if (store.isView || store.isDelete) return 'RO'

    // Edit 模式下，主键字段 RO
    if (store.isEdit && PK_FIELDS.has(field)) return 'RO'

    // New / Copy 模式下，主键字段 D（系统采番）
    if ((store.isNew || store.isCopy) && PK_FIELDS.has(field)) return 'D'

    // 必填判断
    if (REQUIRED_FIELDS.has(field)) return 'R'

    return 'E'
  }

  /**
   * 是否禁用输入（template 直接绑定 :disabled）
   */
  function isDisabled(field: string) {
    const s = stateOf(field)
    return s === 'D' || s === 'RO'
  }

  /**
   * 是否必填（模板用于标星 / rules）
   */
  function isRequired(field: string) {
    return stateOf(field) === 'R'
  }

  /**
   * 整体是否只读（页面级）
   */
  const isPageReadOnly = computed(() => store.isReadOnly)

  /**
   * 按钮显隐（根据操作种别）
   * - New:      [保存] [下一步] [取消]
   * - Edit:     [保存] [下一步] [取消]
   * - Copy:     [保存] [下一步] [取消]
   * - Delete:   [删除] [取消]
   * - View:     [关闭]
   */
  const buttonVisibility = computed(() => ({
    save: store.isNew || store.isEdit || store.isCopy,
    next: store.isNew || store.isEdit || store.isCopy || store.isView,
    del: store.isDelete,
    close: store.isView,
    cancel: true,
  }))

  /**
   * 禁用某个字段集合（例如子拠点切换引起的担当者禁用）
   */
  function isForceDisabled(field: string, extra: Set<string> = new Set()) {
    return extra.has(field) || isDisabled(field)
  }

  return {
    stateOf,
    isDisabled,
    isRequired,
    isPageReadOnly,
    buttonVisibility,
    isForceDisabled,
    OperationType,
  }
}
