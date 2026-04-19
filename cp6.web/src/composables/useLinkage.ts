import { watch } from 'vue'
import type { Ref } from 'vue'
import type { EstimateCalcDto, MasterStaff } from '@/types/estimateCalc'
import { masterApi } from '@/api/master'

/**
 * Step1 联动 composable
 * 1. 受注拠点 → 担当者列表 reload
 * 2. 刃渡り blur → シート寸法 auto-calc
 * 3. 見積り数 blur → パレット数 auto-calc
 */
export function useStep1Linkage(
  basicInfo: Ref<EstimateCalcDto>,
  staffList: Ref<MasterStaff[]>
) {
  // 1) 受注拠点 → 担当者 reload
  //    规格：当 orderBaseCd 变化，按新拠点重新拉担当者列表；
  //    若当前 staffCd 不在新列表中，清空。
  watch(
    () => basicInfo.value.orderBaseCd,
    async (newBase) => {
      if (!newBase) {
        staffList.value = []
        basicInfo.value.staffCd = undefined
        return
      }
      const res = await masterApi.getStaffs(newBase)
      staffList.value = res.data ?? []
      if (basicInfo.value.staffCd && !staffList.value.find((s) => s.staffCd === basicInfo.value.staffCd)) {
        basicInfo.value.staffCd = undefined
      }
    }
  )

  /**
   * 2) 刃渡り blur → シート寸法 auto-calc
   *    规格：SheetDimW ≈ BladeWidth + GutterLr × 2
   *          SheetDimF ≈ Flow      + GutterFb × 2
   *    仅当目标字段为空（未手工输入）时才覆盖，避免破坏用户值。
   */
  function onBladeBlur() {
    const b = basicInfo.value.bladeWidth ?? 0
    const flow = basicInfo.value.bladeFlow ?? 0
    const gLr = basicInfo.value.gutterLr ?? 0
    const gFb = basicInfo.value.gutterFb ?? 0
    if (b > 0 && (!basicInfo.value.sheetDimW || basicInfo.value.sheetDimW === 0)) {
      basicInfo.value.sheetDimW = Math.round((b + gLr * 2) * 10) / 10
    }
    if (flow > 0 && (!basicInfo.value.sheetDimF || basicInfo.value.sheetDimF === 0)) {
      basicInfo.value.sheetDimF = Math.round((flow + gFb * 2) * 10) / 10
    }
  }

  /**
   * 3) 見積り数 blur → パレット数 auto-calc
   *    规格：PalletCnt[i] = ceil(EstimateQty[i] / 100)
   *    仅当目标为空时才覆盖。
   */
  function onEstimateQtyBlur(index: number) {
    const qty = basicInfo.value.estimateQtys?.[index] ?? 0
    const pallet = basicInfo.value.palletCnts?.[index] ?? 0
    if (qty > 0 && pallet === 0 && basicInfo.value.palletCnts) {
      basicInfo.value.palletCnts[index] = Math.ceil(qty / 100)
    }
  }

  return { onBladeBlur, onEstimateQtyBlur }
}
