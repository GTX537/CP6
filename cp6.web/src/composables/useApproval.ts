import { ref } from 'vue'
import { approvalApi } from '@/api/oa/approval'
import type { ApprovalPanelDetail } from '@/types/oa/approval'

export function useApproval(bizType: string, bizId: () => string) {
  const detail = ref<ApprovalPanelDetail | null>(null)
  const loading = ref(false)
  const acting = ref(false)

  async function reload() {
    const id = bizId()
    if (!id) {
      detail.value = null
      return
    }
    loading.value = true
    try {
      detail.value = (await approvalApi.detail(bizType, id)).data
    } finally {
      loading.value = false
    }
  }

  async function decide(decision: 'approve' | 'reject', comment?: string) {
    const taskId = detail.value?.myTask?.taskId
    if (!taskId || acting.value) return
    acting.value = true
    try {
      await approvalApi.decide(taskId, decision, comment)
      await reload()
    } finally {
      acting.value = false
    }
  }

  return { detail, loading, acting, reload, decide }
}
