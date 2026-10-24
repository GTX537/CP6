import { computed, ref, watch } from 'vue'
import { usePermissionStore } from '@/stores/permission'
import { useOperationsIntent } from './useOperationsIntent'
import { warehouseOwnerApi } from '@/api/wms/warehouseOwner'
import { checkMasterPage } from '@/types/wms/warehouseOwner'
import type { WarehouseObservation } from '@/types/wms/warehouseOwner'

/** A bounded, current-authority code search, not an exhaustive warehouse catalogue. */
export function useWarehouseOwnerChoices(clearSelection: () => void) {
  const permissions = usePermissionStore()
  const allowed = computed(() => permissions.loaded && permissions.has('wms-warehouse:query'))
  const warehouses = ref<WarehouseObservation[]>([]), total = ref(0), loading = ref(false), failed = ref(false)
  let sequence = 0
  function clear() { sequence++; warehouses.value = []; total.value = 0; loading.value = false; failed.value = false; clearSelection() }
  const intent = useOperationsIntent(clear)
  watch(allowed, () => { intent.invalidate(); clear() }, { flush: 'sync' })
  watch(() => permissions.actionKeys, () => { intent.invalidate(); clear() }, { deep: true, flush: 'sync' })
  async function loadWarehouses(code = '') {
    const n = ++sequence, token = intent.capture(); warehouses.value = []; total.value = 0; failed.value = false
    if (!allowed.value || code.length > 10) return
    loading.value = true
    const q = { warehouseCd: code || undefined, page: 1, pageSize: 200 }
    try {
      const response = await warehouseOwnerApi.warehouses(q)
      if (!intent.current(token) || n !== sequence || !allowed.value) return
      if (response.code !== 0) throw new Error('READ_FAILED')
      const page = checkMasterPage(response.data, 'warehouses', q); warehouses.value = page.items; total.value = page.total
    } catch { if (intent.current(token) && n === sequence) failed.value = true }
    finally { if (intent.current(token) && n === sequence) loading.value = false }
  }
  return { warehouses, total, loading, failed, allowed, loadWarehouses }
}
