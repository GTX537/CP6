import http from 'k6/http'
import { check, fail } from 'k6'

const taskNos = csv(__ENV.TASK_NOS)
const deviceIds = csv(__ENV.DEVICE_IDS)
const maxScanP95Ms = Number(__ENV.MAX_SCAN_P95_MS || 300)
const maxCompleteP95Ms = Number(__ENV.MAX_COMPLETE_P95_MS || 2000)
const virtualUsers = Math.max(1, taskNos.length)

export const options = {
  scenarios: {
    move_workflow: {
      executor: 'per-vu-iterations',
      vus: virtualUsers,
      iterations: 1,
      maxDuration: __ENV.MAX_DURATION || '10m',
    },
  },
  thresholds: {
    'http_req_failed{endpoint:scan}': ['rate<0.001'],
    'http_req_duration{endpoint:scan}': [`p(95)<${maxScanP95Ms}`],
    'http_req_failed{endpoint:complete}': ['rate<0.001'],
    'http_req_duration{endpoint:complete}': [`p(95)<${maxCompleteP95Ms}`],
  },
}

const apiUrl = String(__ENV.API_URL || '').replace(/\/$/, '')
const token = String(__ENV.ACCESS_TOKEN || '')

export function setup() {
  if (!apiUrl || !token)
    throw new Error('API_URL and ACCESS_TOKEN are required.')
  if (taskNos.length < 1)
    throw new Error('TASK_NOS must contain prepared, unassigned MOVE task numbers.')
  if (deviceIds.length < 1)
    throw new Error('DEVICE_IDS must contain at least one active device ID.')
}

export default function () {
  const taskNo = taskNos[(__VU - 1) % taskNos.length]
  const deviceId = deviceIds[(__VU - 1) % deviceIds.length]
  let task = getJson(
    http.get(`${apiUrl}/api/v2/wms/tasks/${encodeURIComponent(taskNo)}`, params('task')),
    'load task',
  )

  if (task.status === 0) {
    task = getJson(
      post(taskNo, 'claim', {
        operationId: uuid(),
        rowVersion: task.rowVersion,
        deviceId,
        executionVersion: task.executionVersion,
      }, 'claim'),
      'claim task',
    )
  }
  if (task.status !== 1)
    fail(`Task ${taskNo} is not InProgress after claim; status=${task.status}.`)

  const scans = [
    ['SourceLocation', task.fromLocationCd],
    ['Product', task.productCd],
  ]
  if (task.lotNo) scans.push(['Lot', task.lotNo])
  scans.push(
    ['TargetLocation', task.toLocationCd],
    ['Quantity', String(task.qty)],
  )

  for (const [step, barcode] of scans) {
    const scanNo = `${deviceId}-${taskNo}-${step}-${uuid()}`
    const result = getJson(
      post(taskNo, 'scan', {
        operationId: uuid(),
        rowVersion: task.rowVersion,
        deviceId,
        executionVersion: task.executionVersion,
        step,
        rawBarcode: barcode,
        clientScanNo: scanNo.substring(0, 64),
        scannedAt: new Date().toISOString(),
      }, 'scan'),
      `scan ${step}`,
    )
    if (!result.matched)
      fail(`Task ${taskNo} scan ${step} failed: ${result.errorCode}.`)
    task.rowVersion = result.rowVersion || task.rowVersion
  }

  const operationId = uuid()
  const completed = getJson(
    post(taskNo, 'complete', {
      operationId,
      rowVersion: task.rowVersion,
      deviceId,
      executionVersion: task.executionVersion,
      scannedQty: task.qty,
      toLocationCd: task.toLocationCd,
    }, 'complete'),
    'complete task',
  )
  check(completed, {
    'MOVE reaches a completion state': value =>
      value.status === 2 || value.status === 3,
    'completion operation is preserved': value =>
      String(value.completionOperationId || '').toLowerCase()
        === operationId.toLowerCase(),
  })
}

function post(taskNo, command, body, endpoint) {
  return http.post(
    `${apiUrl}/api/v2/wms/tasks/${encodeURIComponent(taskNo)}/${command}`,
    JSON.stringify(body),
    params(endpoint),
  )
}

function params(endpoint) {
  return {
    headers: {
      Authorization: `Bearer ${token}`,
      'Content-Type': 'application/json',
    },
    tags: { endpoint },
    timeout: endpoint === 'complete' ? '5s' : '2s',
  }
}

function getJson(response, action) {
  if (!check(response, {
    [`${action} returns success`]: result =>
      result.status >= 200 && result.status < 300,
  })) {
    fail(`${action} failed with HTTP ${response.status}: ${response.body}.`)
  }
  try {
    return response.json()
  } catch (_) {
    fail(`${action} returned invalid JSON.`)
  }
}

function csv(value) {
  return String(value || '')
    .split(',')
    .map(item => item.trim())
    .filter(Boolean)
}

function uuid() {
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, token => {
    const random = Math.floor(Math.random() * 16)
    const value = token === 'x' ? random : (random & 0x3) | 0x8
    return value.toString(16)
  })
}
