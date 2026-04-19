import * as signalR from '@microsoft/signalr'

/**
 * SignalR 连接管理
 *
 * 面试要点：
 * 1. withAutomaticReconnect() — 断线自动重连（网络波动不丢消息）
 * 2. HubConnectionBuilder — 流式构建连接配置
 * 3. on('事件名', callback) — 监听服务端推送的事件
 * 4. 单例模式 — 整个应用共享一个连接
 */

let connection: signalR.HubConnection | null = null

export function getConnection(): signalR.HubConnection {
  if (!connection) {
    connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/notify')
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000]) // 重连间隔
      .configureLogging(signalR.LogLevel.Warning)
      .build()
  }
  return connection
}

export async function startConnection() {
  const conn = getConnection()
  if (conn.state === signalR.HubConnectionState.Disconnected) {
    try {
      await conn.start()
      console.log('[SignalR] Connected')
    } catch (err) {
      console.warn('[SignalR] Connection failed, will retry:', err)
    }
  }
}

export function stopConnection() {
  if (connection) {
    connection.stop()
    connection = null
  }
}
