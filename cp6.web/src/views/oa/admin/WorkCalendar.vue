<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { Refresh } from '@element-plus/icons-vue'
import { AUTH_EPOCH, AUTH_EVENT, AUTH_RECORD, IMPERSONATION, identityStamp } from '@/auth/identity'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import CpTag from '@/components/base/CpTag.vue'
import { workCalendarApi, type CalendarIntent, type CronExplanation, type RecalculationItem, type RecalculationPreview, type WorkCalendarYear } from '@/api/oa/workCalendar'
import { toExceptionMap, stateForDate, dayTone, hasTag, type DayKind } from './workCalendarModel'
import { CRON_EXPRESSION_MAX_LENGTH, hasCronInput } from './flowTriggerModel'

const { t } = useI18n()
const calDate = ref(new Date())
const calendar = ref<WorkCalendarYear>()
const historical = ref<number>()
const requestedRevision = ref<number>()
const loading = ref(false), saving = ref(false), error = ref('')
const pending = ref<CalendarIntent>()
const exMap = computed(() => toExceptionMap(calendar.value?.items ?? []))
const stateFor = (day: string) => stateForDate(day, exMap.value)
const locked = computed(() => loading.value || saving.value || !!pending.value)
const dialogVisible = ref(false), editDay = ref(''), editKind = ref<DayKind>('normal'), editNote = ref('')
const clearKind = computed(() => stateForDate(editDay.value, new Map()).kind)
const taskIds = ref(''), reason = ref('')
const preview = ref<RecalculationPreview>()
const confirmed = ref<string[]>([])
const cron = ref(''), afterUtc = ref(''), workdaysOnly = ref(false), cronBusy = ref(false), explanation = ref<CronExplanation>()
const cronError = ref('')
const cronReady = computed(() => !locked.value && !!calendar.value?.timeZoneId && !!calendar.value.timeZoneRulesVersion
  && hasCronInput(cron.value) && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?Z$/.test(afterUtc.value))
let cronSequence = 0, cronRequest: AbortController | undefined

function invalidateCron() {
  cronSequence++
  cronRequest?.abort()
  explanation.value = undefined
  cronBusy.value = false
  cronError.value = ''
}
function clearCron() {
  invalidateCron()
  cron.value = afterUtc.value = ''
  workdaysOnly.value = false
}
watch(() => [cron.value, afterUtc.value, workdaysOnly.value, calendar.value], invalidateCron, { flush: 'sync' })
const sessionKey = 'cp6-oa-work-calendar-v1'
let readSequence = 0, generation = 0, controller = new AbortController()

function clearPending() {
  pending.value = undefined
  sessionStorage.removeItem(sessionKey)
}
function clearIdentity() {
  generation++
  controller.abort()
  controller = new AbortController()
  calendar.value = undefined
  historical.value = requestedRevision.value = undefined
  preview.value = undefined
  confirmed.value = []
  taskIds.value = reason.value = editDay.value = editNote.value = ''
  clearCron()
  dialogVisible.value = loading.value = saving.value = false
  clearPending()
}
function identityChanged() {
  clearIdentity()
  error.value = '登录身份已变化，请重新读取。'
}
function storageChanged(event: StorageEvent) {
  if (!event.key || [AUTH_EPOCH, AUTH_RECORD, IMPERSONATION].includes(event.key)) identityChanged()
  else if (event.key === 'menus') clearCron()
}
function handleError(e: any) {
  const status = e?.response?.status
  if (status === 401 || status === 403) {
    clearIdentity()
    error.value = '当前权限不足，已清除本页资料。'
  } else {
    error.value = pending.value ? '尚未确认结果，请按原命令重试。'
      : (e?.response?.data?.code ?? (status ? '操作未完成（' + status + '）。' : '无法连接 Main，请重试。'))
  }
}
async function explainCron() {
  if (!cronReady.value || cronBusy.value || !calendar.value) return
  const sequence = ++cronSequence, at = generation, stamp = identityStamp(), pinned = calendar.value
  const current = () => sequence === cronSequence && at === generation && stamp === identityStamp()
  const request = new AbortController()
  cronRequest = request
  cronBusy.value = true
  explanation.value = undefined
  cronError.value = ''
  try {
    const value = await workCalendarApi.explainCron(pinned, cron.value, afterUtc.value, workdaysOnly.value, request.signal)
    if (!current()) return
    if (value.mode !== 'EXPLANATION_ONLY' || value.isExecutionPermit !== false || value.calendarId !== pinned.calendarId ||
        value.calendarRevision !== pinned.revision || value.timeZoneId !== pinned.timeZoneId ||
        value.timeZoneRulesVersion !== pinned.timeZoneRulesVersion) throw new Error('TIME_PIN_MISMATCH')
    explanation.value = value
  } catch (e: any) {
    if (!current()) return
    const status = e?.response?.status
    const code = e?.response?.data?.code
    if (code === 'FORECAST_AUTHORITY_UNPROVEN' || code === 'FORECAST_ACTION_FORBIDDEN') {
      clearCron()
      cronError.value = code === 'FORECAST_AUTHORITY_UNPROVEN'
        ? '预演权限映射尚未采用，当前已阻断。' : '当前没有 Cron 解释权限，已清除解释内容。'
    } else if (status === 412) cronError.value = '时区或规则版本已变化，请刷新日历后重新解释。'
    else if (code === 'CRON_EXPRESSION_INVALID') cronError.value = '请输入合法的五段 Cron 表达式。'
    else if (code === 'TENANT_TIMEZONE_REQUIRED' || code === 'TENANT_TIMEZONE_INVALID')
      cronError.value = '当前租户的时区规则未证实，无法解释时间。'
    else if (status === 401 || status === 403) handleError(e)
    else cronError.value = '时间解释未完成，请重新读取日历后重试。'
  } finally {
    if (cronRequest === request) cronRequest = undefined
    if (current()) cronBusy.value = false
  }
}

async function reload() {
  const sequence = ++readSequence, stamp = identityStamp(), at = generation
  const current = () => sequence === readSequence && at === generation && stamp === identityStamp()
  loading.value = true
  error.value = ''
  try {
    const year = await workCalendarApi.list(calDate.value.getFullYear(), historical.value, controller.signal)
    if (current()) calendar.value = year
  } catch (e) {
    if (current()) { calendar.value = undefined; handleError(e) }
  } finally {
    if (current()) loading.value = false
  }
}
function readRevision(revision?: number) {
  historical.value = revision
  preview.value = undefined
  void reload()
}
watch(() => calDate.value.getFullYear(), reload)

function openDay(day: string) {
  if (!calendar.value || historical.value !== undefined || locked.value || !day.startsWith(calendar.value.year + '-')) return
  const state = stateFor(day)
  editDay.value = day
  editKind.value = state.kind === 'weekend' ? 'normal' : state.kind
  editNote.value = state.note ?? ''
  dialogVisible.value = true
}
function calendarCommand() {
  return { commandId: crypto.randomUUID(), calendarId: calendar.value!.calendarId, expectedRevision: calendar.value!.revision }
}
function saveDay() {
  if (!calendar.value || locked.value || historical.value !== undefined) return
  const isWorkday = editKind.value === 'normal' ? null : editKind.value === 'makeup'
  void transmit({ kind: 'changes', command: { ...calendarCommand(), date: editDay.value, isWorkday,
    note: isWorkday === null ? null : editNote.value || null } })
}
function importJp() {
  if (!calendar.value || locked.value || historical.value !== undefined) return
  void transmit({ kind: 'import-jp', command: { ...calendarCommand(), year: calendar.value.year } })
}
async function transmit(intent: CalendarIntent) {
  if (saving.value) return
  const stamp = identityStamp(), at = generation
  const current = () => at === generation && stamp === identityStamp()
  saving.value = true
  error.value = ''
  pending.value = intent
  try {
    sessionStorage.setItem(sessionKey, JSON.stringify({ stamp, intent }))
    const receipt = await workCalendarApi.execute(intent, controller.signal)
    if (!current()) return
    if (receipt.commandId !== intent.command.commandId) throw new Error('原回执编号不匹配')
    clearPending()
    dialogVisible.value = false
    if (intent.kind === 'deadline-recalculations') {
      confirmed.value.push(intent.command.taskId)
      ElMessage.success('已取得原重算回执。当前 UTC 截止 ' + (receipt.after?.dueAt ?? '未设置'))
    } else {
      preview.value = undefined
      if (intent.kind === 'import-jp')
        ElMessage.success('已导入 ' + (receipt.inserted?.length ?? 0) + ' 日，保留已有例外 ' + (receipt.skipped?.length ?? 0) + ' 日。')
      await reload()
    }
  } catch (e: any) {
    if (!current()) return
    if ([400, 401, 403, 404, 409, 412, 422].includes(e?.response?.status)) {
      clearPending()
      preview.value = undefined
    }
    handleError(e)
  } finally {
    if (current()) saving.value = false
  }
}
async function prepare() {
  const ids = taskIds.value.trim().split(/[\s,，]+/).filter(Boolean)
  if (!calendar.value || locked.value || historical.value !== undefined) return
  if (!ids.length || ids.length > 50 || new Set(ids).size !== ids.length ||
    ids.some(id => !/^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(id))) {
    error.value = '请输入 1 至 50 个不同的原任务 ID。'
    return
  }
  const stamp = identityStamp(), at = generation
  const current = () => at === generation && stamp === identityStamp()
  saving.value = true
  preview.value = undefined
  confirmed.value = []
  error.value = ''
  try {
    const result = await workCalendarApi.preview(calendar.value.calendarId, calendar.value.revision, ids, controller.signal)
    if (current()) preview.value = result
  } catch (e) {
    if (current()) handleError(e)
  } finally {
    if (current()) saving.value = false
  }
}
function confirmDeadline(item: RecalculationItem) {
  if (!preview.value || locked.value || !reason.value.trim() || confirmed.value.includes(item.taskId)) return
  void transmit({ kind: 'deadline-recalculations', command: {
    commandId: crypto.randomUUID(), previewId: preview.value.previewId, taskId: item.taskId, reason: reason.value.trim(),
    expectedDeadlineRevision: item.before.revision, expectedTaskVersion: item.taskVersion, expectedInstanceVersion: item.instanceVersion,
  } })
}
onMounted(() => {
  window.addEventListener(AUTH_EVENT, identityChanged)
  window.addEventListener('cp6-workspace-authority-changed', clearCron)
  window.addEventListener('storage', storageChanged)
  try {
    const saved = JSON.parse(sessionStorage.getItem(sessionKey) ?? 'null')
    if (saved?.stamp === identityStamp() && saved.intent?.command?.commandId) pending.value = saved.intent
    else clearPending()
  } catch { clearPending() }
  void reload()
})
onBeforeUnmount(() => {
  generation++
  controller.abort()
  invalidateCron()
  window.removeEventListener(AUTH_EVENT, identityChanged)
  window.removeEventListener('cp6-workspace-authority-changed', clearCron)
  window.removeEventListener('storage', storageChanged)
})
</script>

<template>
  <CpPageShell :title="t('oa.workcal.title')" :count="calendar?.items.length">
    <template #actions>
      <el-button :icon="Refresh" circle :loading="loading" aria-label="刷新工作日历" @click="reload" />
    </template>
    <p v-if="error" role="alert">{{ error }}</p>
    <el-button v-if="pending" :loading="saving" @click="transmit(pending)">按原命令重试</el-button>
    <template v-if="calendar">
      <section aria-label="工作日历版本" class="wc-meta">
        <p>工作日历 {{ calendar.calendarId }} · 版本 {{ calendar.revision }}<span v-if="historical !== undefined"> · 历史只读</span></p>
        <p>默认周一至周五工作，周六和周日休息。当天例外优先。</p>
        <p>租户时区 {{ calendar.timeZoneId || '无可信配置' }} · 当前规则 {{ calendar.timeZoneRulesVersion || calendar.timeZoneFailure }}
          <span v-if="calendar.timeZoneSource === 'TENANT_SYSTEM_RULES'"> · 系统时区规则</span>
        </p>
        <div class="wc-controls">
          <el-input-number v-model="requestedRevision" :min="0" :precision="0" aria-label="历史日历版本" :disabled="locked" />
          <el-button :disabled="requestedRevision === undefined || locked" @click="readRevision(requestedRevision)">查看版本</el-button>
          <el-button v-if="historical !== undefined" :disabled="locked" @click="readRevision()">回到当前版本</el-button>
        </div>
        <p>日本假日包 {{ calendar.japaneseHolidays.version }} · 年份 {{ calendar.japaneseHolidays.years.join('、') }} · SHA-256 {{ calendar.japaneseHolidays.sha256 }}</p>
        <el-button
          v-if="historical === undefined && calendar.japaneseHolidays.years.includes(calendar.year)"
          v-permission="'oa-work-calendar:Calendar.Edit'" :disabled="locked" @click="importJp"
        >{{ t('oa.workcal.importJp') }}（{{ calendar.year }}）</el-button>
      </section>
      <section aria-label="Cron 时间解释" class="wc-meta">
        <h3>Cron 时间解释</h3>
        <p v-if="cronError" role="alert">{{ cronError }}</p>
        <p v-if="!calendar.timeZoneRulesVersion">当前租户的时区规则未证实，无法解释时间。</p>
        <template v-else>
          <label>Cron（5 段） <input v-model="cron" :maxlength="CRON_EXPRESSION_MAX_LENGTH" placeholder="30 1 * * *" :disabled="cronBusy" /></label>
          <label>起算 UTC <input v-model.trim="afterUtc" placeholder="2027-11-07T04:00:00Z" :disabled="cronBusy" /></label>
          <label><input v-model="workdaysOnly" type="checkbox" :disabled="cronBusy" /> 仅采用当前版本标记的工作日</label>
          <el-button :disabled="!cronReady" :loading="cronBusy" @click="explainCron">解释未来时点</el-button>
          <div v-if="explanation" aria-live="polite">
            <p>仅解释。缺失的当地时刻跳过，重复时刻按 UTC 顺序列出两次。</p>
            <p v-if="explanation.coverage === 'SEARCH_LIMIT'">在搜索上限内未确认五个时点。</p>
            <table v-if="explanation.points.length">
              <thead><tr><th>UTC</th><th>当地时间</th><th>偏移（分钟）</th><th>重复序号</th></tr></thead>
              <tbody><tr v-for="point in explanation.points" :key="point.utc">
                <td>{{ point.utc }}</td><td>{{ point.local }}</td><td>{{ point.offsetMinutes }}</td><td>{{ point.fold }}</td>
              </tr></tbody>
            </table>
          </div>
        </template>
      </section>
      <div class="wc-legend">
        <CpTag :tone="dayTone('makeup')">{{ t('oa.workcal.legend.makeup') }}</CpTag>
        <CpTag :tone="dayTone('closed')">{{ t('oa.workcal.legend.closed') }}</CpTag>
        <CpTag :tone="dayTone('weekend')">{{ t('oa.workcal.legend.weekend') }}</CpTag>
      </div>
      <el-calendar v-model="calDate">
        <template #date-cell="{ data }">
          <div class="wc-cell" @click="openDay(data.day)">
            <span class="wc-day num">{{ data.day.split('-')[2] }}</span>
            <CpTag v-if="hasTag(stateFor(data.day).kind)" :tone="dayTone(stateFor(data.day).kind)">
              {{ t('oa.workcal.kind.' + stateFor(data.day).kind) }}
            </CpTag>
            <span v-if="stateFor(data.day).note" class="wc-note">{{ stateFor(data.day).note }}</span>
          </div>
        </template>
      </el-calendar>
      <section v-if="historical === undefined" v-permission="'oa-work-calendar:Calendar.Recalculate'" aria-label="重算期限" class="wc-meta">
        <h3>重算期限</h3>
        <el-input v-model="taskIds" type="textarea" :rows="2" aria-label="原任务 ID" placeholder="原任务 ID，用空格、换行或逗号分隔" :disabled="locked" />
        <el-button :disabled="locked" @click="prepare">预览重算</el-button>
        <template v-if="preview">
          <p>日历版本 {{ preview.calendarRevision }} · 预览有效至 {{ preview.expiresAt }}</p>
          <el-input v-model="reason" maxlength="250" aria-label="重算理由" placeholder="重算理由" :disabled="locked" />
          <ul>
            <li v-for="item in preview.items" :key="item.taskId">
              <p>任务 {{ item.taskId }}</p>
              <p>原 UTC 截止 {{ item.before.dueAt || '未计算' }}（版本 {{ item.before.revision }}）</p>
              <p>拟定 UTC 截止 {{ item.after.dueAt }}（版本 {{ item.after.revision }}）</p>
              <el-button :disabled="locked || !reason.trim() || confirmed.includes(item.taskId)" @click="confirmDeadline(item)">
                {{ confirmed.includes(item.taskId) ? '已取得回执' : '确认重算此任务' }}
              </el-button>
            </li>
          </ul>
        </template>
      </section>
    </template>
    <el-dialog v-model="dialogVisible" :title="t('oa.workcal.dialog.title') + ' ' + editDay" width="360px">
      <el-radio-group v-model="editKind" :disabled="locked">
        <el-radio value="normal">{{ t('oa.workcal.kind.normal') }}</el-radio>
        <el-radio value="makeup">{{ t('oa.workcal.kind.makeup') }}</el-radio>
        <el-radio value="closed">{{ t('oa.workcal.kind.closed') }}</el-radio>
      </el-radio-group>
      <p v-if="editKind === 'normal'">恢复默认后为{{ clearKind === 'weekend' ? '休息日' : '工作日' }}。</p>
      <el-input v-else v-model="editNote" class="wc-note-input" :placeholder="t('oa.workcal.dialog.note')" maxlength="100" :disabled="locked" />
      <template #footer>
        <el-button @click="dialogVisible = false">{{ t('取消') }}</el-button>
        <el-button v-permission="'oa-work-calendar:Calendar.Edit'" type="primary" :disabled="locked" @click="saveDay">{{ t('确定') }}</el-button>
      </template>
    </el-dialog>
  </CpPageShell>
</template>

<style scoped>
.wc-meta { display: grid; gap: 10px; justify-items: start; margin-bottom: 20px; }
.wc-meta p { margin: 0; overflow-wrap: anywhere; }
.wc-controls, .wc-legend { display: flex; gap: 10px; flex-wrap: wrap; }
.wc-cell { display: flex; flex-direction: column; gap: 3px; min-height: 46px; cursor: pointer; }
.wc-day { font-weight: 700; }
.wc-note { font-size: 11px; color: var(--cp-muted); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.wc-note-input { margin-top: 12px; }
</style>
