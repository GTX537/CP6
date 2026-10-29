import type { RmaPending } from '@/api/wms/rmaFormalApplications'
import type { RmaPolicyPending } from '@/api/wms/rmaPolicies'
import type { DeliveryEvidencePending } from '@/api/wms/rmaDeliveryEvidence'
import type { AuthorizationPending } from '@/api/wms/rmaAuthorizations'

// The original body is private recovery data. Keep it out of localStorage, URLs and storage events.
async function transaction<T>(mode: IDBTransactionMode, action: (store: IDBObjectStore) => IDBRequest<T>): Promise<T> {
  const db = await new Promise<IDBDatabase>((resolve, reject) => {
    const request = indexedDB.open('cp6-rma-formal-recovery-v1', 1)
    request.onupgradeneeded = () => request.result.createObjectStore('pending')
    request.onsuccess = () => resolve(request.result)
    request.onerror = () => reject(request.error)
  })
  try {
    return await new Promise<T>((resolve, reject) => {
      const tx = db.transaction('pending', mode), request = action(tx.objectStore('pending'))
      tx.oncomplete = () => resolve(request.result)
      tx.onabort = () => reject(tx.error ?? request.error ?? new Error('无法保存原请求，请检查浏览器存储。'))
    })
  } finally { db.close() }
}
export const rmaPendingStore = {
  read: (key: string) => transaction<unknown>('readonly', s => s.get(key)),
  save: (key: string, value: RmaPending | RmaPolicyPending | DeliveryEvidencePending | AuthorizationPending) => transaction('readwrite', s => s.put(value, key)),
  remove: (key: string) => transaction('readwrite', s => s.delete(key)),
}
