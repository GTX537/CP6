<template>
  <div class="inbox-settings">
    <el-tabs v-model="activeTab">
      <el-tab-pane label="当前任务委派" name="current-delegation"><TaskDelegationPanel v-if="activeTab === 'current-delegation'" /></el-tab-pane>
      <!-- Tab 1: 代理人設定 -->
      <el-tab-pane label="旧版任务委派" name="delegate">
        <p>此处授权沿用旧版任务规则。当前原生任务请在“当前任务委派”中明确设置动作、范围和有效期。</p>
        <div class="tab-toolbar">
          <el-button v-permission="'oa-settings:delegate'" type="primary" @click="openAddDialog">{{ t('oa.settings.addDelegate') }}</el-button>
        </div>
        <el-table :data="delegates" v-loading="delegateLoading" border stripe>
          <el-table-column :label="t('oa.settings.delegateName')" prop="delegateName" min-width="120" />
          <el-table-column :label="t('oa.settings.validPeriod')" min-width="200">
            <template #default="{ row }: { row: DelegateItem }">
              {{ row.validFrom }} ~ {{ row.validTo }}
            </template>
          </el-table-column>
          <el-table-column :label="t('oa.settings.scope')" prop="scope" min-width="100" />
          <el-table-column :label="t('oa.settings.remark')" prop="remark" min-width="140" />
          <el-table-column :label="t('common.action')" width="80" fixed="right">
            <template #default="{ row }: { row: DelegateItem }">
              <el-button v-permission="'oa-settings:delegate'" type="danger" link @click="removeDelegate(row)">{{ t('common.delete') }}</el-button>
            </template>
          </el-table-column>
        </el-table>
      </el-tab-pane>

      <!-- Tab 2: 显示偏好 -->
      <el-tab-pane :label="t('oa.settings.prefTab')" name="pref">
        <el-form label-width="130px" style="max-width: 500px; margin-top: 16px">
          <el-form-item :label="t('oa.settings.pageSize')">
            <el-input-number v-model="prefs.pageSize" :min="5" :max="100" :step="5" />
          </el-form-item>
          <el-form-item :label="t('oa.settings.hideCancelled')">
            <el-switch v-model="prefs.hideCancelled" />
          </el-form-item>
          <el-form-item :label="t('oa.settings.showSummary')">
            <el-switch v-model="prefs.showSummary" />
          </el-form-item>
          <el-form-item>
            <el-button v-permission="'oa-settings:edit'" type="primary" :loading="prefSaving" @click="savePref">
              {{ t('common.save') }}
            </el-button>
          </el-form-item>
        </el-form>
      </el-tab-pane>

      <!-- Tab 3: 通知设定（类型×通道矩阵，wfs-inbox-ux §2.3） -->
      <el-tab-pane :label="t('oa.notify.settings.tab')" name="notify">
        <el-card shadow="never" style="max-width: 640px; margin-top: 16px">
          <el-table :data="matrixRows" size="small" border>
            <el-table-column :label="t('oa.notify.matrix.colType')" min-width="180">
              <template #default="{ row }">{{ t('oa.notify.type.' + row.typeKey) }}</template>
            </el-table-column>
            <el-table-column :label="t('oa.notify.matrix.colInApp')" width="110" align="center">
              <template #default="{ row }">
                <el-tooltip :disabled="row.inAppSupported" :content="t('oa.notify.matrix.unsupported')">
                  <el-switch
                    v-model="matrixState[row.typeKey]!.inApp"
                    :disabled="!row.inAppSupported"
                  />
                </el-tooltip>
              </template>
            </el-table-column>
            <el-table-column :label="t('oa.notify.matrix.colEmail')" width="110" align="center">
              <template #default="{ row }">
                <el-tooltip :disabled="row.emailSupported" :content="t('oa.notify.matrix.unsupported')">
                  <el-switch
                    v-model="matrixState[row.typeKey]!.email"
                    :disabled="!row.emailSupported"
                  />
                </el-tooltip>
              </template>
            </el-table-column>
          </el-table>
          <p v-if="notifyReadState === 'LegacyInvalidDefaulted'" role="status">旧偏好中无法解析的设置按默认开启。读取不会修改已存偏好。</p>
          <p v-if="notifyIssue" role="status">{{ notifyIssue }}</p>
          <div class="matrix-actions">
            <el-button v-permission="'oa-settings:edit'" type="primary" :loading="notifySaving" :disabled="!notificationVersion || !!pendingPreference" @click="saveNotifyMatrix">
              {{ t('common.save') }}
            </el-button>
            <el-button v-permission="'oa-settings:edit'" :disabled="!notificationVersion || !!pendingPreference || notifySaving" @click="resetNotifyMatrix">{{ t('oa.notify.matrix.reset') }}</el-button>
            <el-button v-if="pendingPreference" :disabled="notifySaving" @click="finishNotificationSave(true)">查询保存结果</el-button>
            <el-button v-if="pendingPreference" :disabled="notifySaving" @click="finishNotificationSave()">按原命令重试</el-button>
          </div>
        </el-card>
      </el-tab-pane>
    </el-tabs>

    <!-- Add Delegate Dialog -->
    <el-dialog
      v-model="addDialogVisible"
      :title="t('oa.settings.addDelegate')"
      width="500px"
      @close="resetAddForm"
    >
      <el-form label-width="90px">
        <el-form-item :label="t('oa.settings.delegateName')">
          <el-select
            v-model="addForm.delegateId"
            filterable
            remote
            :remote-method="searchUsers"
            :loading="userSearchLoading"
            :placeholder="t('oa.settings.userHint')"
            style="width: 100%"
            clearable
          >
            <el-option
              v-for="u in userOptions"
              :key="u.value"
              :label="u.label"
              :value="u.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('oa.settings.validPeriod')">
          <el-date-picker
            v-model="addForm.dateRange"
            type="daterange"
            :range-separator="t('common.to')"
            :start-placeholder="t('oa.settings.validFrom')"
            :end-placeholder="t('oa.settings.validTo')"
            value-format="YYYY-MM-DD"
            style="width: 100%"
          />
        </el-form-item>
        <el-form-item :label="t('oa.settings.remark')">
          <el-input
            v-model="addForm.remark"
            type="textarea"
            :rows="2"
            :placeholder="t('oa.settings.remarkHint')"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="addDialogVisible = false">{{ t('common.cancel') }}</el-button>
        <el-button
          type="primary"
          :loading="addSubmitting"
          :disabled="!addForm.delegateId || !addForm.dateRange"
          @click="confirmAdd"
        >
          {{ t('common.confirm') }}
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import TaskDelegationPanel from './TaskDelegationPanel.vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { delegateApi } from '@/api/oa/delegate'
import { prefApi } from '@/api/oa/pref'
import { userApi } from '@/api/sys/user'
import type { DelegateItem } from '@/types/oa/advanced'
import { AUTH_EVENT, identityStamp } from '@/auth/identity'
import type { NotificationPreferenceCells, NotificationPreferenceCommand, NotificationPreferenceView, NotificationPreferenceRow } from '@/types/oa/notification'

const { t } = useI18n()

const activeTab = ref('current-delegation')

// ─── Delegate tab ────────────────────────────────────────────────────────────

const delegates = ref<DelegateItem[]>([])
const delegateLoading = ref(false)

async function loadDelegates() {
  delegateLoading.value = true
  try {
    const res = await delegateApi.list()
    delegates.value = ((res as any).data as DelegateItem[]) || []
  } finally {
    delegateLoading.value = false
  }
}

// ── Add dialog ───
const addDialogVisible = ref(false)
const addSubmitting = ref(false)

interface AddForm {
  delegateId: string
  dateRange: [string, string] | null
  remark: string
}

const addForm = ref<AddForm>({
  delegateId: '',
  dateRange: null,
  remark: '',
})

// User remote search (same pattern as TransferDialog.vue)
interface UserOption { label: string; value: string }
const userOptions = ref<UserOption[]>([])
const userSearchLoading = ref(false)

async function searchUsers(keyword: string) {
  if (!keyword) {
    userOptions.value = []
    return
  }
  userSearchLoading.value = true
  try {
    const res: any = await userApi.getList({ page: 1, pageSize: 20, keyword })
    const rows: any[] = res.rows ?? []
    userOptions.value = rows.map((u: any) => ({
      label: u.nickName || u.userName,
      value: u.id,
    }))
  } catch {
    // HTTP interceptor already toasts the error
  } finally {
    userSearchLoading.value = false
  }
}

function openAddDialog() {
  addDialogVisible.value = true
}

function resetAddForm() {
  addForm.value = { delegateId: '', dateRange: null, remark: '' }
  userOptions.value = []
}

async function confirmAdd() {
  const { delegateId, dateRange, remark } = addForm.value
  if (!delegateId || !dateRange) return
  addSubmitting.value = true
  try {
    await delegateApi.add({
      delegateId,
      validFrom: dateRange[0],
      validTo: dateRange[1],
      scope: null,
      remark: remark || undefined,
    })
    ElMessage.success(t('oa.settings.addOk'))
    addDialogVisible.value = false
    await loadDelegates()
  } catch {
    // HTTP interceptor auto-toasts
  } finally {
    addSubmitting.value = false
  }
}

async function removeDelegate(row: DelegateItem) {
  try {
    await ElMessageBox.confirm(
      t('oa.settings.deleteConfirm', { name: row.delegateName }),
      t('common.confirmTitle'),
      { type: 'warning', confirmButtonText: t('common.delete'), cancelButtonText: t('common.cancel') },
    )
  } catch {
    return // user cancelled
  }
  try {
    await delegateApi.remove(row.id)
    ElMessage.success(t('oa.settings.deleteOk'))
    await loadDelegates()
  } catch {
    // HTTP interceptor auto-toasts
  }
}

// ─── Pref tab ────────────────────────────────────────────────────────────────

interface Prefs {
  pageSize: number
  hideCancelled: boolean
  showSummary: boolean
}

// Full raw PrefsJson object kept in memory so that any partial save preserves
// all sub-keys written by other sections (display prefs / notify prefs / future keys).
const storedRaw = ref<Record<string, unknown>>({})

const prefs = ref<Prefs>({ pageSize: 20, hideCancelled: false, showSummary: true })
const prefSaving = ref(false)

// ─── Notify matrix tab ───────────────────────────────────────────────────────
const matrixRows = ref<NotificationPreferenceRow[]>([])
const matrixState = ref<NotificationPreferenceCells>({})
const notifySaving = ref(false)
const notificationVersion = ref('')
const notifyReadState = ref('')
const notifyIssue = ref('')
const pendingKey = 'cp6-oa-notification-pref-pending'
const pendingPreference = ref<{ identity: string; command: NotificationPreferenceCommand }>()
let currentIdentity = identityStamp()
let notificationLoad = 0

function applyNotification(view: NotificationPreferenceView) {
  notificationVersion.value = view.version
  notifyReadState.value = view.readState
  matrixRows.value = view.support
  matrixState.value = view.effectivePrefs
}

async function loadNotificationPreferences() {
  const stamp = identityStamp(), at = ++notificationLoad
  try {
    const view = await prefApi.notificationPreferences()
    if (stamp === identityStamp() && at === notificationLoad) applyNotification(view)
  } catch {
    if (stamp !== identityStamp() || at !== notificationLoad) return
    matrixRows.value = []
    matrixState.value = {}
    notificationVersion.value = ''
    notifyReadState.value = ''
    notifyIssue.value = '当前通知偏好不可读取。'
  }
}

function clearPending() {
  pendingPreference.value = undefined
  sessionStorage.removeItem(pendingKey)
}

function onNotificationIdentity() {
  const current = identityStamp()
  if (current === currentIdentity) return
  currentIdentity = current
  notificationLoad++
  clearPending()
  notificationVersion.value = ''
  matrixRows.value = []
  matrixState.value = {}
  notifyIssue.value = ''
  notifyReadState.value = ''
  void loadNotificationPreferences()
}

async function finishNotificationSave(queryOnly = false, reset = false) {
  const pending = pendingPreference.value
  if (!pending || pending.identity !== identityStamp() || notifySaving.value) return
  notifySaving.value = true
  notifyIssue.value = ''
  try {
    const receipt = queryOnly ? await prefApi.notificationPreferenceOriginal(pending.command.commandId)
      : await prefApi.saveNotificationPreferences(pending.command)
    if (pending.identity !== identityStamp()) return
    if (receipt.commandId !== pending.command.commandId) throw new Error('通知偏好回执未确认。')
    clearPending()
    applyNotification(receipt.result)
    ElMessage.success(t(reset ? 'oa.notify.matrix.resetOk' : 'oa.notify.matrix.saveOk'))
    await loadNotificationPreferences()
  } catch (cause) {
    if (pending.identity !== identityStamp()) return
    const status = (cause as { response?: { status?: number } })?.response?.status
    if (status === 409 || status === 412 || status === 422 || status === 400) {
      clearPending()
      notifyIssue.value = '保存未应用，请核对当前偏好后重试。'
      await loadNotificationPreferences()
    } else if (status === 401 || status === 403) {
      clearPending()
      notificationVersion.value = ''
      matrixRows.value = []
      matrixState.value = {}
      notifyReadState.value = ''
      notifyIssue.value = '当前通知偏好权限不可用。'
    } else {
      notifyIssue.value = '保存结果待确认，可查询或按原命令重试。'
    }
  } finally {
    notifySaving.value = false
  }
}

function saveNotification(notify: NotificationPreferenceCells | null) {
  if (!notificationVersion.value || pendingPreference.value || notifySaving.value) return
  const expectedVersion = notificationVersion.value
  const command: NotificationPreferenceCommand = {
    commandId: crypto.randomUUID(),
    expectedVersion,
    body: { expectedPreferenceVersion: expectedVersion, patchMode: 'TOP_LEVEL_MERGE', patch: { notify } },
  }
  const first = { identity: identityStamp(), command }
  pendingPreference.value = first
  sessionStorage.setItem(pendingKey, JSON.stringify(first))
  void finishNotificationSave(false, notify === null)
}

function saveNotifyMatrix() {
  saveNotification(Object.fromEntries(Object.entries(matrixState.value).map(([type, cell]) => [type, { ...cell }])))
}
function resetNotifyMatrix() { saveNotification(null) }

async function loadPref() {
  try {
    const res = await prefApi.get()
    const prefsJson = (res as any).data?.prefsJson
    if (!prefsJson) return
    const parsed = JSON.parse(prefsJson) as Record<string, any>
    if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
      storedRaw.value = parsed
      prefs.value = {
        pageSize: parsed.pageSize ?? 20,
        hideCancelled: parsed.hideCancelled ?? false,
        showSummary: parsed.showSummary ?? true,
      }
    }
  } catch { /* 显示偏好保留当前默认。 */ }
}

async function savePref() {
  prefSaving.value = true
  try {
    // 显示偏好三键顶层合并写（服务端保 notify/rowMode 等他键；storedRaw spread 兜底不删）
    const merged = { ...storedRaw.value, ...prefs.value }
    storedRaw.value = merged
    await prefApi.saveMerge(JSON.stringify(prefs.value))
    ElMessage.success(t('oa.settings.prefSaveOk'))
  } catch {
    // HTTP interceptor auto-toasts
  } finally {
    prefSaving.value = false
  }
}

// ─── Init ────────────────────────────────────────────────────────────────────

onMounted(() => {
  loadDelegates()
  loadPref()
  try {
    const stored = JSON.parse(sessionStorage.getItem(pendingKey) ?? 'null')
    if (stored?.identity === identityStamp() && typeof stored.command?.commandId === 'string') {
      pendingPreference.value = stored
      notifyIssue.value = '有一笔通知偏好保存待确认。'
    } else clearPending()
  } catch { clearPending() }
  void loadNotificationPreferences()
  window.addEventListener(AUTH_EVENT, onNotificationIdentity)
  window.addEventListener('storage', onNotificationIdentity)
})
onUnmounted(() => {
  notificationLoad++
  window.removeEventListener(AUTH_EVENT, onNotificationIdentity)
  window.removeEventListener('storage', onNotificationIdentity)
})
</script>

<style scoped>
.inbox-settings {
  padding: 16px;
}

.tab-toolbar {
  margin-bottom: 12px;
}

.matrix-actions {
  display: flex;
  gap: 10px;
  margin-top: 14px;
}
</style>
