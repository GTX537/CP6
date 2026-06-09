import { ElMessageBox, ElMessage } from 'element-plus'
import { h } from 'vue'
import type { AxiosError } from 'axios'
import { estimateCalcApi } from '@/api/erp/estimateCalc'
import { useEstimateStore } from '@/stores/estimate'
import { OperationType } from '@/types/erp/estimateCalc'

interface ApiErrBody {
  code?: number
  message?: string
  msgId?: string
}

/**
 * 見積計算書 乐观锁 409 冲突处理
 *
 * 后端返回：
 *   HTTP 409
 *   body: { code: 409, message: "更新対象が...", msgId: "MSG-W10002" }
 *
 * UX：
 *   弹 messagebox，主按钮=「最新版を取得」，副按钮=「取消」
 *   - 最新版：调 GET /estimate-calcs/{no}，覆盖 store.basicInfo，保持 Edit 模式
 *   - 取消：保留当前编辑（用户可能想保存到别处）
 */
export function useConflictHandler() {
  const store = useEstimateStore()

  /**
   * 捕获 axios 错误；若 status=409，弹冲突对话框并返回 true（已处理）；
   * 非 409 返回 false 让调用方决定。
   */
  async function handle(err: unknown): Promise<boolean> {
    const axErr = err as AxiosError<ApiErrBody>
    const status = axErr?.response?.status
    if (status !== 409) return false

    const body = axErr.response?.data
    const msg = body?.message ?? '更新が競合しました。'
    const msgId = body?.msgId ?? 'MSG-W10002'

    try {
      await ElMessageBox({
        title: '排他制御エラー',
        message: h('div', null, [
          h('p', null, `[${msgId}] ${msg}`),
          h('p', { style: 'color:#909399;font-size:12px;margin-top:8px' },
            '他のユーザーが先に更新しています。最新版を読み込んで再編集してください。'),
        ]),
        type: 'warning',
        confirmButtonText: '最新版を取得',
        cancelButtonText: 'キャンセル',
        showCancelButton: true,
        customClass: 'msbbpa010-conflict-dialog',
      })
    } catch {
      return true // 用户选取消，也算已处理
    }

    // 用户选「最新版を取得」→ 重新拉取
    const no = store.basicInfo.qtnCalcNo
    if (!no) return true
    try {
      const res = await estimateCalcApi.getByNo(no)
      if (res.code === 0) {
        store.loadBasicInfo(res.data)
        store.setOperationType(OperationType.Edit)
        ElMessage.success('最新データを取得しました。もう一度保存してください')
      }
    } catch (e) {
      console.error('重新拉取失败', e)
      ElMessage.error('最新版の取得に失敗しました')
    }
    return true
  }

  return { handle }
}
