import { ElMessageBox, ElMessage } from 'element-plus'
import { h } from 'vue'
import { useI18n } from 'vue-i18n'
import type { AxiosError } from 'axios'
import { productApi } from '@/api/erp/product'
import { useProductMasterStore } from '@/stores/productMaster'
import { ProductOperationType } from '@/types/erp/productMaster'

interface ApiErrBody {
  code?: number
  message?: string
  msgId?: string
}

/**
 * 製品マスタ 楽観锁 409 冲突处理（仕様書 §第15章 楽観排他制御）
 *
 * 后端返回：
 *   HTTP 409
 *   body: { code: 409, message: "更新対象が...", msgId: "MSG-W10002" }
 */
export function useProductConflictHandler() {
  const store = useProductMasterStore()
  const { t } = useI18n()

  async function handle(err: unknown): Promise<boolean> {
    const axErr = err as AxiosError<ApiErrBody>
    const status = axErr?.response?.status
    if (status !== 409) return false

    const body = axErr.response?.data
    const msg = body?.message ?? t('更新が競合しました。')
    const msgId = body?.msgId ?? 'MSG-W10002'

    try {
      await ElMessageBox({
        title: t('排他制御エラー'),
        message: h('div', null, [
          h('p', null, `[${msgId}] ${msg}`),
          h('p', { style: 'color:#909399;font-size:12px;margin-top:8px' },
            t('他のユーザーが先に更新しています。最新版を読み込んで再編集してください。')),
        ]),
        type: 'warning',
        confirmButtonText: t('最新版を取得'),
        cancelButtonText: t('キャンセル'),
        showCancelButton: true,
      })
    } catch {
      return true
    }

    const cd = store.productCd
    if (!cd) return true
    try {
      const res = await productApi.getByCd(cd)
      if (res.code === 0) {
        store.loadFromDto(res.data)
        store.setOperationType(ProductOperationType.Edit)
        ElMessage.success(t('最新データを取得しました。もう一度保存してください'))
      }
    } catch (e) {
      console.error('再取得失敗', e)
      ElMessage.error(t('最新版の取得に失敗しました'))
    }
    return true
  }

  return { handle }
}
