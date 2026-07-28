import http from 'k6/http'
import ws from 'k6/ws'
import { check, fail } from 'k6'
import { Counter, Rate, Trend } from 'k6/metrics'

const onlineDevices = Number(__ENV.ONLINE_DEVICES || 500)
const targetRate = Number(__ENV.RATE || 100)
const maxReadP95Ms = Number(__ENV.MAX_READ_P95_MS || 300)
const maxRealtimeMs = Number(__ENV.MAX_REALTIME_MS || 2000)
const socketHoldMs = Number(__ENV.SOCKET_HOLD_MS || 110000)
const duration = __ENV.DURATION || '2m'
const requireRealtimeEvent =
  String(__ENV.REQUIRE_REALTIME_EVENT || 'true').toLowerCase() === 'true'

const hubConnectionSuccess = new Rate('wms_hub_connection_success')
const hubErrors = new Counter('wms_hub_errors')
const hubLifetime = new Trend('wms_hub_lifetime_ms', true)
const realtimeDelivery = new Trend('wms_realtime_delivery_ms', true)
const realtimeEvents = new Counter('wms_realtime_events')

const thresholds = {
  'http_req_failed{endpoint:tasks}': ['rate<0.001'],
  'http_req_duration{endpoint:tasks}': [`p(95)<${maxReadP95Ms}`],
  wms_hub_connection_success: ['rate>0.999'],
  wms_hub_errors: ['count<1'],
  wms_hub_lifetime_ms: [`min>${Math.floor(socketHoldMs * 0.9)}`],
  dropped_iterations: ['count<1'],
}
thresholds.wms_realtime_delivery_ms = requireRealtimeEvent
  ? ['count>0', `p(95)<${maxRealtimeMs}`]
  : [`p(95)<${maxRealtimeMs}`]

export const options = {
  scenarios: {
    online_devices: {
      executor: 'per-vu-iterations',
      exec: 'holdHubConnection',
      vus: onlineDevices,
      iterations: 1,
      maxDuration: duration,
    },
    task_read_peak: {
      executor: 'constant-arrival-rate',
      exec: 'readTasks',
      rate: targetRate,
      timeUnit: '1s',
      duration,
      preAllocatedVUs: Number(__ENV.PREALLOCATED_VUS || targetRate),
      maxVUs: Number(__ENV.MAX_VUS || Math.max(500, targetRate * 5)),
    },
  },
  thresholds,
}

const recordSeparator = '\u001e'

export function setup() {
  const apiUrl = String(__ENV.API_URL || '').replace(/\/$/, '')
  const token = String(__ENV.ACCESS_TOKEN || '')
  if (!apiUrl || !token)
    throw new Error('API_URL and ACCESS_TOKEN are required.')
  if (onlineDevices < 1 || targetRate < 1 || socketHoldMs < 1000)
    throw new Error('ONLINE_DEVICES, RATE and SOCKET_HOLD_MS must be positive.')

  const startedAt = Date.now()
  const bootstrap = http.get(
    `${apiUrl}/api/client/bootstrap?platform=android&currentVersion=1.0.0`,
    { tags: { endpoint: 'bootstrap' }, timeout: '5s' },
  )
  const endedAt = Date.now()
  if (bootstrap.status !== 200)
    throw new Error(`Bootstrap clock probe failed with HTTP ${bootstrap.status}.`)
  const serverUtc = Date.parse(String(bootstrap.json('serverUtc') || ''))
  if (!Number.isFinite(serverUtc))
    throw new Error('Bootstrap response did not contain a valid serverUtc.')

  return {
    apiUrl,
    token,
    clockOffsetMs: Math.round((startedAt + endedAt) / 2 - serverUtc),
  }
}

export function readTasks(data) {
  const response = http.get(
    `${data.apiUrl}/api/v2/wms/tasks?page=1&pageSize=50&openOnly=true`,
    {
      headers: { Authorization: `Bearer ${data.token}` },
      tags: { endpoint: 'tasks' },
      timeout: '2s',
    },
  )
  check(response, {
    'task list returns 200': result => result.status === 200,
    'task list is JSON': result =>
      String(result.headers['Content-Type'] || '').includes('application/json'),
  })
}

export function holdHubConnection(data) {
  const negotiation = http.post(
    `${data.apiUrl}/hubs/wms/negotiate?negotiateVersion=1`,
    null,
    {
      headers: { Authorization: `Bearer ${data.token}` },
      tags: { endpoint: 'hub-negotiate' },
      timeout: '5s',
    },
  )
  if (!check(negotiation, {
    'SignalR negotiation returns 200': result => result.status === 200,
  })) {
    hubConnectionSuccess.add(false)
    fail(`SignalR negotiation failed with HTTP ${negotiation.status}.`)
  }

  const connectionToken = String(negotiation.json('connectionToken') || '')
  if (!connectionToken) {
    hubConnectionSuccess.add(false)
    fail('SignalR negotiation returned no connectionToken.')
  }

  const webSocketBase = data.apiUrl.replace(/^http/i, 'ws')
  const connectedAt = Date.now()
  const response = ws.connect(
    `${webSocketBase}/hubs/wms?id=${encodeURIComponent(connectionToken)}`,
    {
      headers: { Authorization: `Bearer ${data.token}` },
      tags: { endpoint: 'wms-hub' },
    },
    socket => {
      socket.on('open', () => {
        socket.send(
          `${JSON.stringify({ protocol: 'json', version: 1 })}${recordSeparator}`,
        )
      })
      socket.on('message', raw => {
        for (const frame of String(raw).split(recordSeparator)) {
          if (!frame) continue
          let message
          try {
            message = JSON.parse(frame)
          } catch (_) {
            hubErrors.add(1)
            continue
          }
          if (message.type !== 1
              || !String(message.target || '').startsWith('MobileTask'))
            continue
          const payload = message.arguments && message.arguments[0]
          const serverEventAt = Date.parse(String(payload?.at || ''))
          if (!Number.isFinite(serverEventAt)) continue
          const deliveryMs = Date.now()
            - (serverEventAt + Number(data.clockOffsetMs || 0))
          realtimeDelivery.add(Math.max(0, deliveryMs))
          realtimeEvents.add(1)
        }
      })
      socket.on('error', () => hubErrors.add(1))
      socket.setTimeout(() => socket.close(), socketHoldMs)
    },
  )

  hubLifetime.add(Date.now() - connectedAt)
  hubConnectionSuccess.add(response && response.status === 101)
  check(response, {
    'SignalR WebSocket upgraded': result => result && result.status === 101,
  })
}
