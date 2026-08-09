<template>
  <div class="task-page">
    <el-card shadow="never">
      <div class="toolbar">
        <div>
          <h2>{{ t('wms.mobile.title') }}</h2>
          <span class="live-state" :class="{ online: realtimeOnline }">
            <i />{{ realtimeOnline ? 'Live updates connected' : 'Live updates offline; manual refresh is available' }}
          </span>
        </div>
        <div class="toolbar-actions">
          <el-button :icon="Refresh" :loading="loading" @click="reload">Refresh</el-button>
          <el-button @click="productionVisible = true">Production console</el-button>
          <el-button v-permission="'wms-mobile:add'" type="primary" :icon="Plus" @click="createVisible = true">
            New MOVE
          </el-button>
        </div>
      </div>

      <el-form inline class="filters" @submit.prevent>
        <el-form-item label="Status">
          <el-select v-model="query.status" clearable style="width: 170px" @change="search">
            <el-option v-for="status in statusOptions" :key="status.value" :label="status.label" :value="status.value" />
          </el-select>
        </el-form-item>
        <el-form-item label="Warehouse"><el-input v-model="query.warehouseCd" clearable /></el-form-item>
        <el-form-item label="Area"><el-input v-model="query.areaCd" clearable /></el-form-item>
        <el-form-item label="Assignee"><el-input v-model="query.assignedTo" clearable @keyup.enter="search" /></el-form-item>
        <el-form-item><el-checkbox v-model="query.openOnly" @change="search">Open only</el-checkbox></el-form-item>
        <el-form-item><el-button type="primary" @click="search">Search</el-button></el-form-item>
      </el-form>

      <el-table :data="tasks" v-loading="loading" stripe @row-click="openDetail">
        <el-table-column prop="taskNo" label="Task" min-width="165" fixed />
        <el-table-column label="Status" width="150">
          <template #default="{ row }"><el-tag :type="statusType(row.status)">{{ statusName(row.status) }}</el-tag></template>
        </el-table-column>
        <el-table-column prop="priority" label="Priority" width="82" />
        <el-table-column prop="assignedTo" label="Assignee" min-width="110">
          <template #default="{ row }">{{ row.assignedTo || 'Unassigned' }}</template>
        </el-table-column>
        <el-table-column prop="warehouseCd" label="Warehouse" width="105" />
        <el-table-column prop="areaCd" label="Area" width="95" />
        <el-table-column prop="fromLocationCd" label="From" min-width="105" />
        <el-table-column prop="productCd" label="Product" min-width="110" />
        <el-table-column prop="toLocationCd" label="To" min-width="105" />
        <el-table-column prop="qty" label="Qty" width="90" align="right" />
        <el-table-column label="Actions" width="230" fixed="right">
          <template #default="{ row }">
            <el-button v-if="row.status === 0" v-permission="'wms-mobile:assign'"
                       size="small" text type="primary" @click.stop="assign(row)">Assign</el-button>
            <el-button v-if="row.status === 1" v-permission="'wms-mobile:pause'"
                       size="small" text type="warning" @click.stop="pause(row)">Pause</el-button>
            <el-dropdown v-if="[0, 1, 4, 5].includes(row.status)" trigger="click"
                         @command="(action: string) => taskAction(action, row)" @click.stop>
              <el-button size="small" text>More</el-button>
              <template #dropdown>
                <el-dropdown-menu>
                  <el-dropdown-item v-if="[1, 4].includes(row.status)" command="release">Release</el-dropdown-item>
                  <el-dropdown-item command="takeover">Take over</el-dropdown-item>
                  <el-dropdown-item v-if="row.status !== 5" command="exception">Raise exception</el-dropdown-item>
                  <el-dropdown-item v-if="row.status === 5" command="resolve">Resolve exception</el-dropdown-item>
                </el-dropdown-menu>
              </template>
            </el-dropdown>
            <el-button v-if="![2, 3, 9].includes(row.status)" v-permission="'wms-mobile:cancel'"
                       size="small" text type="danger" @click.stop="cancel(row)">Cancel</el-button>
          </template>
        </el-table-column>
      </el-table>

      <div class="pager">
        <el-pagination v-model:current-page="query.page" v-model:page-size="query.pageSize"
                       :total="total" :page-sizes="[20, 50, 100]"
                       layout="total, sizes, prev, pager, next" @change="reload" />
      </div>
    </el-card>

    <el-drawer v-model="detailVisible" :title="selected?.taskNo" size="560px">
      <el-descriptions v-if="selected" :column="1" border>
        <el-descriptions-item label="Status">{{ statusName(selected.status) }}</el-descriptions-item>
        <el-descriptions-item label="Assignee">{{ selected.assignedTo || 'Unassigned' }}</el-descriptions-item>
        <el-descriptions-item label="Warehouse / area">{{ selected.warehouseCd }} / {{ selected.areaCd || '—' }}</el-descriptions-item>
        <el-descriptions-item label="Route">{{ selected.fromLocationCd }} → {{ selected.toLocationCd }}</el-descriptions-item>
        <el-descriptions-item label="Product / lot">{{ selected.productCd }} / {{ selected.lotNo || '—' }}</el-descriptions-item>
        <el-descriptions-item label="Quantity">{{ selected.qty }} {{ selected.unitCd }}</el-descriptions-item>
        <el-descriptions-item label="Reservations">
          source {{ selected.reservedSourceQty }} / target capacity {{ selected.reservedTargetCapacityQty }}
        </el-descriptions-item>
        <el-descriptions-item label="Source order">{{ selected.sourceType || '—' }} / {{ selected.sourceNo || '—' }}</el-descriptions-item>
        <el-descriptions-item label="Execution version">{{ selected.executionVersion }}</el-descriptions-item>
        <el-descriptions-item label="Related tasks">
          parent {{ selected.parentTaskNo || '—' }} / remainder {{ selected.remainderTaskNo || '—' }}
        </el-descriptions-item>
        <el-descriptions-item label="Exception">
          {{ selected.exceptionReasonCd || '—' }} {{ selected.exceptionDescription || '' }}
        </el-descriptions-item>
        <el-descriptions-item label="Started">{{ selected.startedAt ? formatDateTime(selected.startedAt) : '—' }}</el-descriptions-item>
        <el-descriptions-item label="Completed">{{ selected.completedAt ? formatDateTime(selected.completedAt) : '—' }}</el-descriptions-item>
        <el-descriptions-item label="Remarks">{{ selected.remarks || '—' }}</el-descriptions-item>
      </el-descriptions>
      <el-timeline v-if="eventHistory.length" class="event-history">
        <el-timeline-item v-for="event in eventHistory"
                          :key="`${event.eventType}-${event.occurredAt}-${event.operationId}`"
                          :timestamp="formatDateTime(event.occurredAt)">
          <strong>{{ event.eventType }}</strong>
          <div>{{ event.userName || 'system' }} · {{ event.deviceId || 'web' }} · execution {{ event.executionVersion }}</div>
        </el-timeline-item>
      </el-timeline>
    </el-drawer>

    <el-dialog v-model="createVisible" title="New MOVE" width="650px">
      <el-form :model="createForm" label-width="110px">
        <el-row :gutter="16">
          <el-col :span="12"><el-form-item label="Warehouse"><el-input v-model="createForm.warehouseCd" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item label="Area"><el-input v-model="createForm.areaCd" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item label="Assignee"><el-input v-model="createForm.assignedTo" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item label="Priority"><el-input-number v-model="createForm.priority" :min="1" :max="4" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item label="From"><el-input v-model="createForm.fromLocationCd" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item label="To"><el-input v-model="createForm.toLocationCd" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item label="Product"><el-input v-model="createForm.productCd" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item label="Lot"><el-input v-model="createForm.lotNo" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item label="Quantity"><el-input-number v-model="createForm.qty" :min="0.00000001" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item label="Source no."><el-input v-model="createForm.sourceNo" /></el-form-item></el-col>
          <el-col :span="24"><el-form-item label="Instruction"><el-input v-model="createForm.instruction" type="textarea" /></el-form-item></el-col>
        </el-row>
      </el-form>
      <template #footer>
        <el-button @click="createVisible = false">Cancel</el-button>
        <el-button type="primary" :loading="saving" @click="create">Create and reserve</el-button>
      </template>
    </el-dialog>
    <el-dialog v-model="productionVisible" title="WMS production console" width="92%" top="4vh">
      <WmsProductionConsole />
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { onMounted, onUnmounted, reactive, ref } from 'vue'
import { Plus, Refresh } from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { mobileApi } from '@/api/wms/mobile'
import { getWmsConnection, startWmsConnection } from '@/utils/wmsHub'
import { formatDateTime } from '@/utils/format'
import WmsProductionConsole from './WmsProductionConsole.vue'
import type { CreateMoveTaskRequest, MobileTask, MobileTaskEvent, MobileTaskQuery } from '@/types/wms/wms'

const { t } = useI18n()
const loading = ref(false)
const saving = ref(false)
const realtimeOnline = ref(false)
const tasks = ref<MobileTask[]>([])
const total = ref(0)
const selected = ref<MobileTask>()
const eventHistory = ref<MobileTaskEvent[]>([])
const detailVisible = ref(false)
const createVisible = ref(false)
const productionVisible = ref(false)
const query = reactive<MobileTaskQuery>({ page: 1, pageSize: 50, openOnly: true })
const createForm = reactive<CreateMoveTaskRequest>({
  warehouseCd: '', areaCd: '', fromLocationCd: '', toLocationCd: '',
  productCd: '', lotNo: '', qty: 1, priority: 2, assignedTo: '',
  instruction: '', sourceType: 'MANUAL', sourceNo: '',
})
const statusOptions = [
  { value: 0, label: 'Pending' }, { value: 1, label: 'In progress' },
  { value: 2, label: 'Completed' }, { value: 3, label: 'Partially completed' },
  { value: 4, label: 'Paused' }, { value: 5, label: 'Exception' },
  { value: 9, label: 'Cancelled' },
]
let refreshTimer: ReturnType<typeof setTimeout> | undefined
const taskEvents = [
  'MobileTaskCreated', 'MobileTaskAssigned', 'MobileTaskStarted',
  'MobileTaskPaused', 'MobileTaskReleased', 'MobileTaskTakenOver',
  'MobileTaskException', 'MobileTaskExceptionResolved',
  'MobileTaskCompleted', 'MobileTaskPartiallyCompleted', 'MobileTaskCancelled',
]

function statusName(status: number) {
  return statusOptions.find(x => x.value === status)?.label ?? `Unknown (${status})`
}
function statusType(status: number): 'info' | 'warning' | 'success' | 'danger' {
  return status === 0 ? 'info' : [1, 4].includes(status) ? 'warning' : [2, 3].includes(status) ? 'success' : 'danger'
}
function search() { query.page = 1; reload() }
async function reload() {
  loading.value = true
  try {
    const result = await mobileApi.tasks(query)
    tasks.value = result.items
    total.value = result.total
    if (selected.value) selected.value = tasks.value.find(x => x.taskNo === selected.value?.taskNo) ?? selected.value
  } finally { loading.value = false }
}
async function openDetail(row: MobileTask) {
  const [task, events] = await Promise.all([mobileApi.get(row.taskNo), mobileApi.events(row.taskNo)])
  selected.value = task
  eventHistory.value = events
  detailVisible.value = true
}
async function create() {
  saving.value = true
  try {
    await mobileApi.create(createForm)
    createVisible.value = false
    ElMessage.success('MOVE created; source stock and target capacity reserved')
    await reload()
  } finally { saving.value = false }
}
async function assign(task: MobileTask) {
  try {
    const result = await ElMessageBox.prompt('User name', 'Assign task', {
      inputValue: task.assignedTo || '',
      inputValidator: value => Boolean(value?.trim()) || 'Assignee is required',
    })
    await mobileApi.assign(task.taskNo, {
      assignedTo: result.value.trim(), rowVersion: task.rowVersion, executionVersion: task.executionVersion,
    })
    await reload()
  } catch (error) { await mutationError(error) }
}
async function cancel(task: MobileTask) {
  try {
    const result = await ElMessageBox.prompt('Cancellation reason', 'Cancel task', {
      inputValidator: value => Boolean(value?.trim()) || 'Reason is required',
    })
    await mobileApi.cancel(task.taskNo, task.rowVersion, result.value.trim())
    await reload()
  } catch (error) { await mutationError(error) }
}
async function pause(task: MobileTask) {
  try {
    const result = await ElMessageBox.prompt('Pause reason', 'Pause task', {
      inputValidator: value => Boolean(value?.trim()) || 'Reason is required',
    })
    await mobileApi.pause(task.taskNo, {
      rowVersion: task.rowVersion, executionVersion: task.executionVersion, reason: result.value.trim(),
    })
    await reload()
  } catch (error) { await mutationError(error) }
}
async function taskAction(action: string, task: MobileTask) {
  try {
    if (action === 'release') {
      const result = await ElMessageBox.prompt('Release reason', 'Release task')
      await mobileApi.release(task.taskNo, {
        rowVersion: task.rowVersion, executionVersion: task.executionVersion, reason: result.value.trim(),
      })
    } else if (action === 'takeover') {
      const result = await ElMessageBox.prompt('New assignee', 'Take over task')
      await mobileApi.takeover(task.taskNo, {
        rowVersion: task.rowVersion, executionVersion: task.executionVersion,
        assignedTo: result.value.trim(), reason: 'Supervisor takeover',
      })
    } else if (action === 'exception') {
      const result = await ElMessageBox.prompt('Reason code followed by description', 'Raise exception')
      const [reasonCode, ...description] = result.value.trim().split(/\s+/)
      const requiredReasonCode = reasonCode ?? ''
      await mobileApi.exception(task.taskNo, {
        rowVersion: task.rowVersion, executionVersion: task.executionVersion,
        reasonCode: requiredReasonCode, description: description.join(' ') || requiredReasonCode,
      })
    } else if (action === 'resolve') {
      const result = await ElMessageBox.prompt('RESUME / REASSIGN / ADJUST / CANCEL', 'Resolve exception', {
        inputValue: 'RESUME',
        inputValidator: value => ['RESUME', 'REASSIGN', 'ADJUST', 'CANCEL'].includes(value.toUpperCase()),
      })
      const resolution = result.value.trim().toUpperCase() as 'RESUME' | 'REASSIGN' | 'ADJUST' | 'CANCEL'
      let assignedTo: string | undefined
      let qty: number | undefined
      let toLocationCd: string | undefined
      if (resolution === 'REASSIGN') {
        const assignee = await ElMessageBox.prompt('New assignee', 'Resolve and reassign', {
          inputValidator: value => Boolean(value?.trim()) || 'Assignee is required',
        })
        assignedTo = assignee.value.trim()
      } else if (resolution === 'ADJUST') {
        const adjustment = await ElMessageBox.prompt(
          'Enter quantity and target location separated by a comma, for example: 8,B-02',
          'Resolve and adjust',
          {
            inputValue: `${task.qty},${task.toLocationCd ?? ''}`,
            inputValidator: value => {
              const [quantity, location] = value.split(',').map(item => item.trim())
              return (Number(quantity) > 0 && Boolean(location)) || 'A positive quantity and target location are required'
            },
          },
        )
        const [quantity, location] = adjustment.value.split(',').map(item => item.trim())
        qty = Number(quantity)
        toLocationCd = location
      }
      await mobileApi.resolveException(task.taskNo, {
        rowVersion: task.rowVersion, executionVersion: task.executionVersion,
        action: resolution, assignedTo, qty, toLocationCd,
      })
    }
    await reload()
  } catch (error) { await mutationError(error) }
}
async function mutationError(error: unknown) {
  if (error === 'cancel' || error === 'close') return
  if ((error as any)?.response?.status === 409) {
    ElMessage.warning('The task changed on another device. Latest state has been loaded.')
    await reload()
    return
  }
  throw error
}
function onTaskChanged() {
  if (refreshTimer) clearTimeout(refreshTimer)
  refreshTimer = setTimeout(reload, 150)
}
onMounted(async () => {
  await reload()
  const connection = await startWmsConnection()
  realtimeOnline.value = connection.state === 'Connected'
  taskEvents.forEach(eventName => connection.on(eventName, onTaskChanged))
  connection.onreconnecting(() => { realtimeOnline.value = false })
  connection.onreconnected(() => { realtimeOnline.value = true; reload() })
  connection.onclose(() => { realtimeOnline.value = false })
})
onUnmounted(() => {
  const connection = getWmsConnection()
  taskEvents.forEach(eventName => connection.off(eventName, onTaskChanged))
  if (refreshTimer) clearTimeout(refreshTimer)
})
</script>

<style scoped>
.task-page { padding: 16px; }
.toolbar { display: flex; align-items: center; justify-content: space-between; gap: 16px; }
.toolbar h2 { margin: 0 0 6px; }
.toolbar-actions { display: flex; gap: 8px; }
.live-state { color: var(--cp-muted); font-size: 12px; display: inline-flex; align-items: center; gap: 6px; }
.live-state i { width: 8px; height: 8px; border-radius: 50%; background: var(--cp-warn); }
.live-state.online i { background: var(--cp-ok); }
.filters { margin-top: 20px; padding: 14px 14px 0; background: var(--cp-bg-th); border-radius: var(--cp-r-sm); }
.pager { display: flex; justify-content: flex-end; margin-top: 16px; }
.event-history { margin-top: 28px; }
</style>
