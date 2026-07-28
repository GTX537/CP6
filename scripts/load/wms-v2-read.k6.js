import http from 'k6/http'
import { check } from 'k6'

const targetRate = Number(__ENV.RATE || 100)
const maxReadP95Ms = Number(__ENV.MAX_READ_P95_MS || 300)

export const options = {
  scenarios: {
    production_read: {
      executor: 'constant-arrival-rate',
      rate: targetRate,
      timeUnit: '1s',
      duration: __ENV.DURATION || '2m',
      preAllocatedVUs: Number(__ENV.PREALLOCATED_VUS || targetRate),
      maxVUs: Number(__ENV.MAX_VUS || Math.max(500, targetRate * 5)),
    },
  },
  thresholds: {
    'http_req_failed{endpoint:tasks}': ['rate<0.001'],
    'http_req_duration{endpoint:tasks}': [`p(95)<${maxReadP95Ms}`],
    dropped_iterations: ['count<1'],
  },
}

const apiUrl = (__ENV.API_URL || '').replace(/\/$/, '')
const token = __ENV.ACCESS_TOKEN || ''

export function setup() {
  if (!apiUrl || !token)
    throw new Error('API_URL and ACCESS_TOKEN are required.')
}

export default function () {
  const response = http.get(
    `${apiUrl}/api/v2/wms/tasks?page=1&pageSize=50&openOnly=true`,
    {
      headers: { Authorization: `Bearer ${token}` },
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
