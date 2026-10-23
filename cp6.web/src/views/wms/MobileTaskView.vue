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
          <el-button :icon="Refresh" :loading="loading" @click="onReload">Refresh</el-button>
          <el-button @click="productionVisible = true">Production console</el-button>
          <el-button v-permission="'wms-mobile:add'" type="primary" :icon="Plus" @click="openCreate" :disabled="!canWrite('add')">
            New MOVE
          </el-button>
        </div>
      </div>

      <el-alert v-if="!writesAvailable" title="Historical MOVE records are read-only; task execution is not enabled." type="info" :closable="false" />
      <el-alert v-if="readError" :title="readError" type="error" :closable="false" />
      <el-alert v-if="intent.error.value" :title="intent.error.value" type="warning" :closable="false" />
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

      <el-table :empty-text="readError || 'No records in the current scope'" :data="tasks" v-loading="loading" stripe @row-click="openDetail">
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
            <el-button v-if="row.status === 0" v-permission="'wms-mobile:assign'" :disabled="!canWrite('assign')"
                       size="small" text type="primary" @click.stop="assign(row)">Assign</el-button>
            <el-button v-if="row.status === 1" v-permission="'wms-mobile:pause'" :disabled="!canWrite('pause')"
                       size="small" text type="warning" @click.stop="pause(row)">Pause</el-button>
            <el-dropdown v-if="writesAvailable && [0, 1, 4, 5].includes(row.status)" trigger="click"
                         @command="(action: string) => taskAction(action, row)" @click.stop>
              <el-button size="small" text :disabled="intent.busy.value || intent.uncertain.value">More</el-button>
              <template #dropdown>
                <el-dropdown-menu>
                  <el-dropdown-item v-if="[1, 4].includes(row.status)" command="release">Release</el-dropdown-item>
                  <el-dropdown-item command="takeover">Take over</el-dropdown-item>
                  <el-dropdown-item v-if="row.status !== 5" command="exception">Raise exception</el-dropdown-item>
                  <el-dropdown-item v-if="row.status === 5" command="resolve">Resolve exception</el-dropdown-item>
                </el-dropdown-menu>
              </template>
            </el-dropdown>
            <el-button v-if="![2, 3, 9].includes(row.status)" v-permission="'wms-mobile:cancel'" :disabled="!canWrite('cancel')"
                       size="small" text type="danger" @click.stop="cancel(row)">Cancel</el-button>
          </template>
        </el-table-column>
      </el-table>

      <div class="pager">
        <el-pagination v-model:current-page="query.page" v-model:page-size="query.pageSize"
                       :total="total" :page-sizes="[20, 50, 100]"
                       layout="total, sizes, prev, pager, next" @change="onReload" />
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
          <div>{{ event.userName || 'Identity not disclosed' }} · {{ event.deviceId || 'Device not disclosed' }} · execution {{ event.executionVersion }}</div>
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
        <el-button type="primary" :loading="intent.busy.value" :disabled="!canWrite('add')" @click="create">Create and reserve</el-button>
      </template>
    </el-dialog>
    <el-dialog v-model="productionVisible" title="WMS production console" width="92%" top="4vh">
      <WmsProductionConsole v-if="productionVisible" :key="intent.epoch.value" />
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
import { useOperationsIntent } from '@/composables/useOperationsIntent'
import { usePermissionStore } from '@/stores/permission'
import WmsProductionConsole from './WmsProductionConsole.vue'
import type { CreateMoveTaskRequest, MobileTask, MobileTaskEvent, MobileTaskQuery } from '@/types/wms/wms'

const { t } = useI18n(), permission = usePermissionStore()
const loading = ref(false), realtimeOnline = ref(false), writesAvailable = ref(false), readError = ref('')
const tasks = ref<MobileTask[]>([]), total = ref(0), selected = ref<MobileTask>(), eventHistory = ref<MobileTaskEvent[]>([])
const detailVisible = ref(false), createVisible = ref(false), productionVisible = ref(false)
const query = reactive<MobileTaskQuery>({ page: 1, pageSize: 50, openOnly: true })
const blank = (): CreateMoveTaskRequest => ({ warehouseCd: '', areaCd: '', fromLocationCd: '', toLocationCd: '', productCd: '', lotNo: '', qty: 1, priority: 2, assignedTo: '', instruction: '', sourceType: 'MANUAL', sourceNo: '' })
const createForm = reactive<CreateMoveTaskRequest>(blank())
let readSequence = 0, reloadRequired = false
function clearRead() { tasks.value = []; total.value = 0; selected.value = undefined; eventHistory.value = []; detailVisible.value = false; writesAvailable.value = false }
const intent = useOperationsIntent(() => {
  readSequence++; reloadRequired = true; clearRead(); loading.value = false; readError.value = 'Identity changed; refresh the current scope.'
  createVisible.value = false; productionVisible.value = false; Object.assign(createForm, blank()); realtimeOnline.value = false
})
const permitted = (action: string) => permission.loaded && permission.has(`wms-mobile:${action}`)
const writePermitted = (action: string) => writesAvailable.value && permitted(action)
const canWrite = (action: string) => writePermitted(action) && !intent.busy.value && !intent.uncertain.value
const statusOptions = [
  { value: 0, label: 'Pending' }, { value: 1, label: 'In progress' },
  { value: 2, label: 'Legacy reported complete' }, { value: 3, label: 'Legacy reported partial' },
  { value: 4, label: 'Paused' }, { value: 5, label: 'Exception' }, { value: 9, label: 'Cancelled' },
]
function statusName(status: number) { return statusOptions.find(x => x.value === status)?.label ?? `Unknown (${status})` }
function statusType(status: number): 'info' | 'warning' | 'success' | 'danger' { return status === 0 ? 'info' : [1, 2, 3, 4].includes(status) ? 'warning' : 'danger' }
function search() { query.page = 1; onReload() }
function onReload() { reloadRequired = false; void reload().catch(() => {}) }
function openCreate() { if (canWrite('add')) createVisible.value = true }
async function reload() {
  const token = intent.capture(), sequence = ++readSequence
  const current = () => intent.current(token) && sequence === readSequence
  loading.value = true; clearRead(); readError.value = ''
  try {
    if (!permitted('view')) throw new Error('Current task view permission is required.')
    const capability = await mobileApi.capabilities()
    if (!current()) throw new Error('Stale task read.')
    if (typeof capability?.writesAvailable !== 'boolean') throw new Error('Task availability could not be read.')
    const result = await mobileApi.tasks({ ...query })
    if (!current() || !permitted('view')) throw new Error('Stale task read.')
    if (!Array.isArray(result?.items) || !Number.isSafeInteger(result.total) || result.total < 0) throw new Error('Task data could not be read.')
    tasks.value = result.items; total.value = result.total; writesAvailable.value = capability.writesAvailable
  } catch (error) {
    if (current()) { clearRead(); createVisible.value = false; readError.value = 'Tasks could not be read; refresh to check current access.' }
    throw error instanceof Error ? error : new Error('Task read unavailable.')
  } finally { if (current()) loading.value = false }
}
async function openDetail(row: MobileTask) {
  const token = intent.capture(), sequence = ++readSequence, number = row.taskNo
  loading.value = false; selected.value = undefined; eventHistory.value = []; detailVisible.value = false
  try {
    if (!permitted('view')) throw new Error('Current task view permission is required.')
    const [task, events] = await Promise.all([mobileApi.get(number), mobileApi.events(number)])
    if (!intent.current(token) || sequence !== readSequence || !permitted('view')) return
    if (!task || task.taskNo !== number || !Array.isArray(events)) throw new Error('Task detail unavailable.')
    selected.value = task; eventHistory.value = events; detailVisible.value = true
  } catch {
    if (intent.current(token) && sequence === readSequence) { clearRead(); readError.value = 'Task detail could not be read; refresh current access.' }
  }
}
async function mutate(action: string, work: (guard: () => void) => Promise<void>) {
  if (!canWrite(action)) return
  const token = intent.begin(); if (!token) return
  const guard = () => { if (!intent.current(token) || !writePermitted(action)) throw new Error('Current task intent is no longer valid.') }
  try { guard(); await work(guard); guard(); await reload() }
  catch (error) { intent.failed(token, error) }
  finally { intent.finish(token) }
}
async function acceptResult(request: Promise<MobileTask>, guard: () => void, number?: string) {
  const result = await request; guard()
  if (!result || typeof result.taskNo !== 'string' || !result.taskNo || number !== undefined && result.taskNo !== number)
    throw new Error('Task result could not be confirmed.')
}
async function create() {
  const frozen = { ...createForm }
  await mutate('add', async guard => { guard(); await acceptResult(mobileApi.create(frozen), guard); createVisible.value = false; ElMessage.success('Legacy MOVE request accepted.') })
}
async function assign(task: MobileTask) {
  const frozen = { ...task }
  await mutate('assign', async guard => {
    const result = await ElMessageBox.prompt('User name', 'Assign task', { inputValue: frozen.assignedTo || '', inputValidator: value => Boolean(value?.trim()) || 'Assignee is required' })
    guard(); await acceptResult(mobileApi.assign(frozen.taskNo, { assignedTo: result.value.trim(), rowVersion: frozen.rowVersion, executionVersion: frozen.executionVersion }), guard, frozen.taskNo)
  })
}
async function cancel(task: MobileTask) {
  const frozen = { ...task }
  await mutate('cancel', async guard => {
    const result = await ElMessageBox.prompt('Cancellation reason', 'Cancel task', { inputValidator: value => Boolean(value?.trim()) || 'Reason is required' })
    guard(); await acceptResult(mobileApi.cancel(frozen.taskNo, frozen.rowVersion, result.value.trim()), guard, frozen.taskNo)
  })
}
async function pause(task: MobileTask) {
  const frozen = { ...task }
  await mutate('pause', async guard => {
    const result = await ElMessageBox.prompt('Pause reason', 'Pause task', { inputValidator: value => Boolean(value?.trim()) || 'Reason is required' })
    guard(); await acceptResult(mobileApi.pause(frozen.taskNo, { rowVersion: frozen.rowVersion, executionVersion: frozen.executionVersion, reason: result.value.trim() }), guard, frozen.taskNo)
  })
}
async function taskAction(action: string, task: MobileTask) {
  if (!['release', 'takeover', 'exception', 'resolve'].includes(action)) return
  const frozen = { ...task }, permissionAction = action === 'resolve' ? 'exception' : action
  await mutate(permissionAction, async guard => {
    const version = { rowVersion: frozen.rowVersion, executionVersion: frozen.executionVersion }
    if (action === 'release') {
      const result = await ElMessageBox.prompt('Release reason', 'Release task'); guard()
      await acceptResult(mobileApi.release(frozen.taskNo, { ...version, reason: result.value.trim() }), guard, frozen.taskNo)
    } else if (action === 'takeover') {
      const result = await ElMessageBox.prompt('New assignee', 'Take over task'); guard()
      await acceptResult(mobileApi.takeover(frozen.taskNo, { ...version, assignedTo: result.value.trim(), reason: 'Supervisor takeover' }), guard, frozen.taskNo)
    } else if (action === 'exception') {
      const result = await ElMessageBox.prompt('Reason code followed by description', 'Raise exception'); guard()
      const [reasonCode = '', ...description] = result.value.trim().split(/\s+/)
      await acceptResult(mobileApi.exception(frozen.taskNo, { ...version, reasonCode, description: description.join(' ') || reasonCode }), guard, frozen.taskNo)
    } else {
      const result = await ElMessageBox.prompt('RESUME / REASSIGN / ADJUST / CANCEL', 'Resolve exception', { inputValue: 'RESUME', inputValidator: value => ['RESUME', 'REASSIGN', 'ADJUST', 'CANCEL'].includes(value.toUpperCase()) }); guard()
      const resolution = result.value.trim().toUpperCase() as 'RESUME' | 'REASSIGN' | 'ADJUST' | 'CANCEL'
      let assignedTo: string | undefined, qty: number | undefined, toLocationCd: string | undefined
      if (resolution === 'REASSIGN') {
        const answer = await ElMessageBox.prompt('New assignee', 'Resolve and reassign', { inputValidator: value => Boolean(value?.trim()) || 'Assignee is required' }); guard(); assignedTo = answer.value.trim()
      } else if (resolution === 'ADJUST') {
        const answer = await ElMessageBox.prompt('Quantity and target location separated by a comma', 'Resolve and adjust', { inputValue: `${frozen.qty},${frozen.toLocationCd ?? ''}`, inputValidator: value => { const [q, location] = value.split(',').map(x => x.trim()); return Number(q) > 0 && Boolean(location) || 'Positive quantity and target required' } }); guard()
        const [q, location] = answer.value.split(',').map(x => x.trim()); qty = Number(q); toLocationCd = location
      }
      guard(); await acceptResult(mobileApi.resolveException(frozen.taskNo, { ...version, action: resolution, assignedTo, qty, toLocationCd }), guard, frozen.taskNo)
    }
  })
}
let refreshTimer: ReturnType<typeof setTimeout> | undefined
const taskEvents = ['MobileTaskCreated', 'MobileTaskAssigned', 'MobileTaskStarted', 'MobileTaskPaused', 'MobileTaskReleased', 'MobileTaskTakenOver', 'MobileTaskException', 'MobileTaskExceptionResolved', 'MobileTaskCompleted', 'MobileTaskPartiallyCompleted', 'MobileTaskCancelled']
function onTaskChanged() {
  if (reloadRequired) return
  if (refreshTimer) clearTimeout(refreshTimer)
  refreshTimer = setTimeout(() => { if (!reloadRequired) onReload() }, 150)
}
onMounted(async () => {
  const token = intent.capture(); await reload().catch(() => {})
  if (!intent.current(token)) return
  try {
    const connection = await startWmsConnection(); if (!intent.current(token)) return
    realtimeOnline.value = connection.state === 'Connected'; taskEvents.forEach(name => connection.on(name, onTaskChanged))
    connection.onreconnecting(() => { if (intent.current(token)) realtimeOnline.value = false })
    connection.onreconnected(() => { if (intent.current(token)) { realtimeOnline.value = true; if (!reloadRequired) onReload() } })
    connection.onclose(() => { if (intent.current(token)) realtimeOnline.value = false })
  } catch { if (intent.current(token)) realtimeOnline.value = false }
})
onUnmounted(() => { const connection = getWmsConnection(); taskEvents.forEach(name => connection.off(name, onTaskChanged)); if (refreshTimer) clearTimeout(refreshTimer) })
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
