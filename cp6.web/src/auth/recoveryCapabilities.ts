// Shared origin storage, without sending the capability through storage events, URLs or tab broadcasts.
// An id in the public UI record is not accepted as authority by the server.
async function database(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open('cp6-authentication-recovery-v1', 1)
    request.onupgradeneeded = () => request.result.createObjectStore('capabilities')
    request.onsuccess = () => resolve(request.result)
    request.onerror = () => reject(new Error('无法保存本次登录的恢复凭据，请检查浏览器存储设置。'))
    request.onblocked = () => reject(new Error('登录恢复存储正在被旧页面占用，请关闭旧页面后重试。'))
  })
}

async function operation<T>(mode: IDBTransactionMode, action: (store: IDBObjectStore) => IDBRequest<T>): Promise<T> {
  const db = await database()
  try {
    return await new Promise<T>((resolve, reject) => {
      const tx = db.transaction('capabilities', mode); const request = action(tx.objectStore('capabilities'))
      tx.oncomplete = () => resolve(request.result)
      tx.onerror = tx.onabort = () => reject(new Error('登录恢复凭据未能保存，请检查浏览器存储设置。'))
    })
  } finally { db.close() }
}

export async function newRecoveryCapability(): Promise<string> {
  const id = crypto.randomUUID()
  const secret = Array.from(crypto.getRandomValues(new Uint8Array(32)), x => x.toString(16).padStart(2, '0')).join('')
  await operation('readwrite', store => store.add(secret, id))
  return id
}

export async function recoverySecret(id: string): Promise<string> {
  const secret = await operation('readonly', store => store.get(id))
  if (typeof secret !== 'string' || !/^[0-9a-f]{64}$/i.test(secret)) throw new Error('原登录恢复凭据不可用，当前结果仍需确认。')
  return secret
}

export async function forgetRecoveryCapability(id: string): Promise<void> {
  await operation('readwrite', store => store.delete(id))
}
