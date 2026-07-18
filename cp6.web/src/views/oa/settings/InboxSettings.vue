<template>
  <div class="inbox-settings">
    <el-tabs v-model="activeTab">
      <!-- Tab 1: 代理人設定 -->
      <el-tab-pane :label="t('oa.settings.delegateTab')" name="delegate">
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
          <div class="matrix-actions">
            <el-button v-permission="'oa-settings:edit'" type="primary" :loading="notifySaving" @click="saveNotifyMatrix">
              {{ t('common.save') }}
            </el-button>
            <el-button v-permission="'oa-settings:edit'" @click="resetNotifyMatrix">{{ t('oa.notify.matrix.reset') }}</el-button>
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
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { delegateApi } from '@/api/oa/delegate'
import { prefApi } from '@/api/oa/pref'
import { userApi } from '@/api/sys/user'
import type { DelegateItem } from '@/types/oa/advanced'
import { buildMatrixState, toNotifyPatch, type MatrixState, type NotifyMatrixRow } from './notifyMatrixModel'

const { t } = useI18n()

const activeTab = ref('delegate')

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
const matrixRows = ref<NotifyMatrixRow[]>([])
const matrixState = ref<MatrixState>({})
const notifySaving = ref(false)

async function loadNotifyMatrix(prefsJson: string) {
  try {
    const res = await prefApi.notifyMatrix()
    matrixRows.value = (((res as any).data as NotifyMatrixRow[]) || [])
    matrixState.value = buildMatrixState(prefsJson, matrixRows.value)
  } catch {
    // HTTP interceptor auto-toasts
  }
}

async function saveNotifyMatrix() {
  notifySaving.value = true
  try {
    await prefApi.saveMerge(toNotifyPatch(matrixState.value))   // 服务端合并：保 pageSize/rowMode 等他键
    ElMessage.success(t('oa.notify.matrix.saveOk'))
    await loadPref()
  } finally {
    notifySaving.value = false
  }
}

async function resetNotifyMatrix() {
  try {
    await prefApi.saveMerge('{"notify":null}')                  // 删键 = 恢复默认全开（三态坍缩）
    ElMessage.success(t('oa.notify.matrix.resetOk'))
    await loadPref()
  } catch {
    // HTTP interceptor auto-toasts
  }
}

async function loadPref() {
  try {
    const res = await prefApi.get()
    // Backend returns Ok2(new { prefsJson = "..." }) → envelope { code, data: { prefsJson } }
    // http interceptor returns response.data (the envelope body), so:
    const prefsJson: string | undefined = (res as any).data?.prefsJson
    if (prefsJson) {
      const parsed = JSON.parse(prefsJson) as Record<string, any>
      // Keep the full raw object so partial saves can always merge into it
      storedRaw.value = parsed
      prefs.value = {
        pageSize: parsed.pageSize ?? 20,
        hideCancelled: parsed.hideCancelled ?? false,
        showSummary: parsed.showSummary ?? true,
      }
    }
    // 矩阵行始终渲染（无 prefsJson 时传 '{}' 走三态坍缩全开）
    await loadNotifyMatrix(prefsJson ?? '{}')
  } catch {
    // use defaults silently
  }
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
