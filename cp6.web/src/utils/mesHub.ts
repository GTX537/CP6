import * as signalR from '@microsoft/signalr'

/**
 * MES SignalR Hub クライアント
 * /hubs/mes 接続専用シングルトン
 *
 * 用法：
 *   import { getMesHub, startMesHub } from '@/utils/mesHub'
 *   const hub = getMesHub()
 *   hub.on('ProductionReported', payload => { ... })
 *   await startMesHub()
 */

let connection: signalR.HubConnection | null = null

export function getMesHub(): signalR.HubConnection {
  if (!connection) {
    connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/mes')
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Warning)
      .build()
  }
  return connection
}

export async function startMesHub() {
  const conn = getMesHub()
  if (conn.state === signalR.HubConnectionState.Disconnected) {
    try {
      await conn.start()
      console.log('[MES Hub] Connected')
    } catch (err) {
      console.warn('[MES Hub] Connect failed, will retry:', err)
    }
  }
}

export async function stopMesHub() {
  if (connection) {
    try { await connection.stop() } catch { /* ignore */ }
    connection = null
  }
}
