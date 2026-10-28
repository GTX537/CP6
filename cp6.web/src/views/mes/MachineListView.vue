<template>
  <div class="machine-list">
    <router-link to="/mes/machine-sources">资源来源与资料版本</router-link>
    <router-link to="/mes/machine-capabilities">资源能力目录</router-link>
    <el-alert v-if="intent.error.value" :title="intent.error.value" type="warning" :closable="false" show-icon />
    <el-alert v-if="creationError" :title="creationError" type="warning" :closable="false" show-icon />
    <div v-if="allowed('query')" class="creation-recovery">
      <el-button :disabled="saving" @click="loadCreationScope">{{ t('登録操作の状態を更新') }}</el-button>
      <template v-if="creationPending">
        <span>{{ t('確認が必要な登録操作') }}: {{ creationPending }}</span>
        <el-button :disabled="saving" @click="resolveCreation(false)">{{ t('元の結果を確認') }}</el-button>
        <el-button :disabled="saving" @click="resolveCreation(true)">{{ t('未実行なら登録を取り消す') }}</el-button>
      </template>
      <span v-if="creationResult">{{ creationResult.status === 'COMMITTED' ? t('登録済み') : creationResult.status === 'CLOSED' ? t('未実行の登録を取り消しました') : t('結果は未確認です。再送せず、確認または取消を行ってください。') }} {{ creationResult.result?.machineCd }}</span>
    </div>
    <el-card shadow="never" class="search-card">
      <el-form :model="query" inline size="small">
        <el-form-item :label="t('設備CD')">
          <el-input v-model="query.machineCd" style="width: 150px;" />
        </el-form-item>
        <el-form-item :label="t('工程CD')">
          <el-input v-model="query.processCd" style="width: 130px;" />
        </el-form-item>
        <el-form-item label="WG">
          <el-input v-model="query.wgCd" style="width: 130px;" />
        </el-form-item>
        <el-form-item :label="t('状態')">
          <el-checkbox-group v-model="query.statuses">
            <el-checkbox v-for="o in MACHINE_STATUS_OPTIONS" :key="o.value" :value="o.value">{{ t(o.label) }}</el-checkbox>
          </el-checkbox-group>
        </el-form-item>
        <el-form-item>
          <el-checkbox v-model="query.activeOnly">{{ t('有効のみ') }}</el-checkbox>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :loading="loading" @click="search">{{ t('検索') }}</el-button>
          <el-button @click="reset">{{ t('クリア') }}</el-button>
          <el-button v-permission="'mes-machine:add'" type="success" @click="openCreate">{{ t('新規') }}</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <!-- 設備状態グリッド（緑灯/赤灯） -->
    <el-card shadow="never" class="grid-card">
      <template #header>{{ t('設備稼働状態グリッド') }}</template>
      <div v-if="rows.length === 0" class="empty">{{ t('データなし') }}</div>
      <div v-else class="status-grid">
        <div v-for="m in rows" :key="m.machineCd" class="machine-cell" :class="'light-' + getLight(m.status)"
          @click="openEdit(m)">
          <div class="machine-cd">{{ m.machineCd }}</div>
          <div class="machine-name">{{ m.machineName }}</div>
          <div class="machine-status">{{ getStatusLabel(m.status) }}</div>
          <div v-if="m.currentWorkOrderNo" class="machine-wo">{{ t('指図') }} {{ m.currentWorkOrderNo }}</div>
          <div v-if="m.todayOee != null" class="machine-oee">OEE {{ m.todayOee }}%</div>
        </div>
      </div>
    </el-card>

    <!-- 一覧表 -->
    <el-card shadow="never" style="margin-top: 12px;">
      <el-table :data="rows" border stripe size="small">
        <el-table-column prop="machineCd" :label="t('設備CD')" width="120" />
        <el-table-column prop="machineName" :label="t('設備名')" min-width="180" />
        <el-table-column prop="machineType" :label="t('種別')" width="120" />
        <el-table-column prop="processCd" :label="t('工程CD')" width="100" />
        <el-table-column prop="wgCd" label="WG" width="100" />
        <el-table-column :label="t('状態')" width="100" align="center">
          <template #default="{ row }">
            <el-tag :color="getStatusColor(row.status)" effect="dark" size="small">{{ getStatusLabel(row.status) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="capacityPerHour" :label="t('能力/h')" width="100" align="right" />
        <el-table-column prop="plannedRunMinutesPerDay" :label="t('計画稼働分/日')" width="120" align="right" />
        <el-table-column :label="t('本日OEE')" width="100" align="center">
          <template #default="{ row }">
            <span v-if="row.todayOee != null">{{ row.todayOee }}%</span>
            <span v-else>-</span>
          </template>
        </el-table-column>
        <el-table-column :label="t('現在指図')" width="160">
          <template #default="{ row }">{{ row.currentWorkOrderNo || '-' }}</template>
        </el-table-column>
        <el-table-column :label="t('次回メンテ')" width="120">
          <template #default="{ row }">{{ formatDate(row.nextMaintenanceDate) }}</template>
        </el-table-column>
        <el-table-column :label="t('有効')" width="70" align="center">
          <template #default="{ row }">
            <el-icon v-if="row.activeFlg" color="#67C23A"><Check /></el-icon>
          </template>
        </el-table-column>
        <el-table-column :label="t('操作')" width="220" align="center" fixed="right">
          <template #default="{ row }">
            <el-button v-permission="'mes-machine:edit'" link type="primary" size="small" @click="openEdit(row)">{{ t('編集') }}</el-button>
            <el-button v-permission="'mes-machine:downtime'" link type="warning" size="small" @click="openDowntime(row)">{{ t('停止登録') }}</el-button>
            <el-button v-permission="'mes-machine:del'" link type="danger" size="small" @click="onDelete(row)">{{ t('削除') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- 設備編集 ダイアログ -->
    <el-dialog v-model="dialogVisible" :title="form.machineCd && isEdit ? t('設備編集') : t('設備新規')" width="700px" @closed="resetForm">
      <el-form :model="form" label-width="120px" size="small">
        <el-form-item v-if="!isEdit && creationContext" :label="t('所属部門')" :required="!creationContext.allowsUnbound">
          <el-select v-model="creationDepartment" clearable :disabled="saving" :placeholder="t('部門を選択してください')" @clear="creationDepartment = null">
            <el-option v-for="department in creationContext.departments" :key="department.id" :value="department.id" :label="`${department.code} — ${department.name}`" />
          </el-select>
          <span v-if="creationContext.allowsUnbound">{{ t('部門を指定しない登録も可能です') }}</span>
        </el-form-item>
        <el-row :gutter="12">
          <el-col :span="12">
            <el-form-item :label="t('設備CD')" required>
              <el-input v-model="form.machineCd" :disabled="isEdit" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item :label="t('設備名')" required>
              <el-input v-model="form.machineName" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item :label="t('種別')">
              <el-input v-model="form.machineType" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item :label="t('工程CD')">
              <el-input v-model="form.processCd" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="WG">
              <el-input v-model="form.wgCd" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item :label="t('拠点CD')">
              <el-input v-model="form.baseCd" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item :label="t('計画稼働(分/日)')">
              <el-input-number v-model="form.plannedRunMinutesPerDay" :min="0" :step="60" style="width: 100%;" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item :label="t('能力(個/時間)')">
              <el-input-number v-model="form.capacityPerHour" :min="0" :precision="2" style="width: 100%;" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item :label="t('標準サイクル(秒)')">
              <el-input-number v-model="form.standardCycleSec" :min="0" :precision="4" style="width: 100%;" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item :label="t('導入日')">
              <el-date-picker v-model="form.installDate" type="date" value-format="YYYY-MM-DD" style="width: 100%;" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item :label="t('最終メンテ日')">
              <el-date-picker v-model="form.lastMaintenanceDate" type="date" value-format="YYYY-MM-DD" style="width: 100%;" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item :label="t('次回メンテ予定')">
              <el-date-picker v-model="form.nextMaintenanceDate" type="date" value-format="YYYY-MM-DD" style="width: 100%;" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item :label="t('メーカ')">
              <el-input v-model="form.manufacturer" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item :label="t('型番')">
              <el-input v-model="form.model" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item :label="t('状態')">
              <el-select v-model="form.status" style="width: 100%;">
                <el-option v-for="o in MACHINE_STATUS_OPTIONS" :key="o.value" :label="t(o.label)" :value="o.value" />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item :label="t('有効FLG')">
              <el-switch v-model="form.activeFlg" />
            </el-form-item>
          </el-col>
          <el-col :span="24">
            <el-form-item :label="t('備考')">
              <el-input v-model="form.remarks" type="textarea" :rows="2" />
            </el-form-item>
          </el-col>
        </el-row>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">{{ t('キャンセル') }}</el-button>
        <el-button v-if="isEdit" v-permission="'mes-machine:edit'" type="primary" :loading="saving" :disabled="intent.uncertain.value" @click="onSave">{{ t('保存') }}</el-button>
        <el-button v-else v-permission="'mes-machine:add'" type="primary" :loading="saving" :disabled="intent.uncertain.value" @click="onSave">{{ t('保存') }}</el-button>
      </template>
    </el-dialog>

    <!-- 停止登録 ダイアログ -->
    <el-dialog v-model="dtDialogVisible" :title="t('設備停止登録')" width="600px">
      <el-form :model="dtForm" label-width="120px" size="small">
        <el-form-item :label="t('設備CD')">
          <el-tag>{{ dtForm.machineCd }}</el-tag>
        </el-form-item>
        <el-form-item :label="t('停止区分')" required>
          <el-select v-model="dtForm.downtimeType" style="width: 100%;">
            <el-option v-for="o in DOWNTIME_TYPE_OPTIONS" :key="o.value" :label="t(o.label)" :value="o.value" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('停止開始日時')" required>
          <el-date-picker v-model="dtForm.startTime" type="datetime" value-format="YYYY-MM-DDTHH:mm:ss" style="width: 100%;" />
        </el-form-item>
        <el-form-item :label="t('停止終了日時')">
          <el-date-picker v-model="dtForm.endTime" type="datetime" value-format="YYYY-MM-DDTHH:mm:ss" style="width: 100%;" :placeholder="t('空＝継続中')" />
        </el-form-item>
        <el-form-item :label="t('関連指図NO')">
          <el-input v-model="dtForm.workOrderNo" />
        </el-form-item>
        <el-form-item :label="t('担当者CD')">
          <el-input v-model="dtForm.operatorCd" />
        </el-form-item>
        <el-form-item :label="t('停止内容')">
          <el-input v-model="dtForm.description" type="textarea" :rows="3" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dtDialogVisible = false">{{ t('キャンセル') }}</el-button>
        <el-button v-permission="'mes-machine:downtime'" type="primary" :loading="saving" :disabled="intent.uncertain.value" @click="onRegisterDowntime">{{ t('登録') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
import { ref, reactive, onMounted, onBeforeUnmount } from 'vue'
import { useOperationsIntent } from '@/composables/useOperationsIntent'
import { usePermissionStore } from '@/stores/permission'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Check } from '@element-plus/icons-vue'
import { machineApi } from '@/api/mes/mes'
import { machineResponse, pendingMachineCreation, saveMachineCreation, resolveMachineCreation } from '@/auth/machineCreationRecovery'
import type { MachineCreationScope, MachineCreationContext, MachineCreationOperation } from '@/types/mes/machineCreation'
import {
  MACHINE_STATUS_OPTIONS,
  DOWNTIME_TYPE_OPTIONS,
  type MachineDto,
  type MachineSearchQuery,
  type MachineDowntimeDto,
} from '@/types/mes/mes'

const loading = ref(false)
const permissions = usePermissionStore()
const rows = ref<MachineDto[]>([])

const defaultQuery = (): MachineSearchQuery => ({
  machineCd: '', processCd: '', wgCd: '', statuses: [], activeOnly: true,
})
const query = reactive<MachineSearchQuery>(defaultQuery())

const dialogVisible = ref(false)
const isEdit = ref(false)

const emptyForm = (): MachineDto => ({
  machineCd: '', machineName: '', machineType: '', processCd: '', wgCd: '', baseCd: '',
  status: 0, plannedRunMinutesPerDay: 480, capacityPerHour: null, standardCycleSec: null,
  installDate: null, lastMaintenanceDate: null, nextMaintenanceDate: null,
  manufacturer: '', model: '', remarks: '', activeFlg: true,
})
const form = reactive<MachineDto>(emptyForm())
const creationContext = ref<MachineCreationContext | null>(null), creationDepartment = ref<string | null>(null)
const creationScope = ref<MachineCreationScope | null>(null), creationPending = ref<string | null>(null)
const creationError = ref(''), creationResult = ref<MachineCreationOperation | null>(null)
let creationReads = 0
let creationWriting = false

// ─── downtime dialog ───
const dtDialogVisible = ref(false)
const emptyDt = (): MachineDowntimeDto => ({
  downtimeNo: '', machineCd: '',
  startTime: new Date().toISOString().slice(0, 19),
  endTime: null, downtimeType: 2,
  workOrderNo: '', operatorCd: '', description: '',
})
const dtForm = reactive<MachineDowntimeDto>(emptyDt())

function getStatusLabel(v: number) { return t(MACHINE_STATUS_OPTIONS.find(o => o.value === v)?.label ?? `${v}`) }
function getStatusColor(v: number) { return MACHINE_STATUS_OPTIONS.find(o => o.value === v)?.color ?? '#909399' }
function getLight(v: number) { return MACHINE_STATUS_OPTIONS.find(o => o.value === v)?.light ?? 'gray' }
function formatDate(v?: string | null) { return v ? v.substring(0, 10) : '' }

const intent = useOperationsIntent(clearProtected)
const saving = intent.busy
let listSequence = 0, detailSequence = 0
function clearProtected() {
  listSequence++; detailSequence++; rows.value = []; loading.value = false
  creationReads++; creationContext.value = null; creationDepartment.value = null; creationScope.value = null
  creationWriting = false
  creationPending.value = null; creationError.value = ''; creationResult.value = null
  dialogVisible.value = false; dtDialogVisible.value = false
  Object.assign(form, emptyForm()); Object.assign(dtForm, emptyDt())
}
function allowed(action: string) { return permissions.loaded && permissions.has(`mes-machine:${action}`) }
function forbidden(error: any, writeSettled = false) {
  if ([401, 403].includes(error?.response?.status)) {
    const unresolved = intent.uncertain.value || (!writeSettled && intent.busy.value && !creationWriting)
    intent.invalidate(); clearProtected()
    if (unresolved) {
      intent.uncertain.value = true
      intent.error.value = '処理結果を確認できません。再送せず、対象の最新状態を確認してください。'
    }
    return true
  }
  return false
}
async function search() {
  const token = intent.capture(), request = ++listSequence
  loading.value = true
  try {
    const res = await machineApi.search({ ...query, statuses: [...(query.statuses || [])] })
    if (intent.current(token) && request === listSequence) rows.value = res.data || []
  } catch (error) {
    if (intent.current(token) && request === listSequence) {
      rows.value = []; forbidden(error)
    }
  } finally { if (intent.current(token) && request === listSequence) loading.value = false }
}
function reset() { Object.assign(query, defaultQuery()); void search() }
async function openCreate() {
  if (!allowed('add') || intent.busy.value || intent.uncertain.value) return
  const token = intent.capture(), request = ++detailSequence
  Object.assign(form, emptyForm()); isEdit.value = false; dialogVisible.value = false
  creationContext.value = null; creationDepartment.value = null; creationError.value = ''; creationResult.value = null
  try {
    const context = machineResponse(await machineApi.creationContext())
    if (!intent.current(token) || request !== detailSequence || !allowed('add')) return
    creationReads++; creationScope.value = { tenantId: context.tenantId, actorId: context.actorId }; syncCreationPending()
    if (creationPending.value) { creationError.value = '先に前回の登録結果を確認してください。'; return }
    creationContext.value = context; dialogVisible.value = true
  } catch (error) {
    if (intent.current(token) && request === detailSequence && !forbidden(error)) creationError.value = '登録できる部門を確認できません。権限と最新状態を確認してください。'
  }
}
async function openEdit(row: MachineDto) {
  if (!allowed('edit') || intent.busy.value || intent.uncertain.value) return
  const token = intent.capture(), request = ++detailSequence
  dialogVisible.value = false; Object.assign(form, emptyForm())
  try {
    const result = await machineApi.get(row.machineCd)
    if (!intent.current(token) || request !== detailSequence || !result.data) return
    Object.assign(form, emptyForm(), result.data); isEdit.value = true; dialogVisible.value = true
  } catch (error) { if (intent.current(token) && request === detailSequence) forbidden(error) }
}
function resetForm() { detailSequence++; Object.assign(form, emptyForm()); creationContext.value = null; creationDepartment.value = null }
async function onSave() {
  const edit = isEdit.value
  if (!allowed(edit ? 'edit' : 'add')) return
  if (!form.machineCd || !form.machineName) { ElMessage.error(t('設備CDと設備名は必須です')); return }
  if (!edit) { await saveCreation(); return }
  const token = intent.begin(); if (!token) return
  const input = { ...form }
  try {
    await machineApi.update(input.machineCd, input)
    if (!intent.current(token)) return
    ElMessage.success(t(edit ? 'ME-MSG-041: 更新しました' : 'ME-MSG-041: 登録しました')); dialogVisible.value = false; void search()
  } catch (error) {
    if (intent.current(token) && !forbidden(error, true)) intent.failed(token, error)
  } finally { intent.finish(token) }
}
function syncCreationPending() {
  if (creationScope.value) creationPending.value = pendingMachineCreation(creationScope.value)
}
async function loadCreationScope() {
  if (saving.value || !allowed('query')) return
  const token = intent.capture(), read = ++creationReads
  creationScope.value = null; creationPending.value = null; creationError.value = ''; creationResult.value = null
  try {
    const scope = machineResponse(await machineApi.creationScope())
    if (!intent.current(token) || read !== creationReads || !allowed('query')) return
    const pending = pendingMachineCreation(scope); creationScope.value = scope; creationPending.value = pending
  } catch (error) {
    if (intent.current(token) && read === creationReads && !forbidden(error)) creationError.value = '登録操作の状態を確認できません。権限と保存された操作番号を確認してください。'
  }
}
async function saveCreation() {
  const context = creationContext.value, department = creationDepartment.value || null
  if (!dialogVisible.value || !context || !allowed('add')) return
  if (department === null ? !context.allowsUnbound : !context.departments.some(d => d.id === department)) {
    creationError.value = '登録する設備の所属部門を選択してください。'; return
  }
  const token = intent.begin(); if (!token) return
  creationWriting = true
  const input = { ...form }; creationError.value = ''; creationResult.value = null
  const guard = () => { if (!intent.current(token) || !allowed('add')) throw new Error('操作画面が変わりました。') }
  try {
    const result = await saveMachineCreation(context, guard, operationId => machineApi.createOperation({
      operationId, expectedAuthorityVersion: context.authorityVersion, departmentId: department, machine: input,
    }))
    if (!intent.current(token)) return
    creationResult.value = result; ElMessage.success(t('登録しました')); dialogVisible.value = false; void search()
  } catch (error) {
    if (intent.current(token)) {
      dialogVisible.value = false; Object.assign(form, emptyForm()); creationContext.value = null; creationDepartment.value = null
      if (!forbidden(error, true)) creationError.value = '結果を確認できません。元の登録操作を確認してください。再送は行いません。'
    }
  } finally {
    if (intent.current(token)) { creationWriting = false; try { syncCreationPending() } catch { creationScope.value = null; creationError.value = '保存された操作番号を確認してください。' } }
    intent.finish(token)
  }
}
async function resolveCreation(close: boolean) {
  if (saving.value || !creationScope.value || !allowed('query')) return
  // Query/close only this original creation; a different legacy write's uncertainty stays intact.
  const token = intent.capture(); intent.busy.value = true
  const scope = { ...creationScope.value }; creationError.value = ''; creationResult.value = null
  const guard = () => { if (!intent.current(token) || !allowed('query')) throw new Error('操作画面が変わりました。') }
  try {
    const result = await resolveMachineCreation(scope, close, guard)
    if (!intent.current(token)) return
    creationResult.value = result; if (result.status === 'COMMITTED') void search()
  } catch (error) {
    if (intent.current(token) && !forbidden(error, true)) creationError.value = '元の結果を確認できません。権限を確認してください。操作番号は保持されます。'
  } finally {
    if (intent.current(token)) { try { syncCreationPending() } catch { creationScope.value = null; creationError.value = '保存された操作番号を確認してください。' } }
    intent.finish(token)
  }
}
function creationStorageChanged(event: StorageEvent) {
  if (event.key !== null && !event.key.startsWith('cp6_machine_creation_v1:') || saving.value || !creationScope.value) return
  creationResult.value = null
  if (!isEdit.value) { dialogVisible.value = false; resetForm() }
  try { syncCreationPending() } catch { creationScope.value = null; creationError.value = '保存された操作番号を確認してください。' }
}
async function onDelete(row: MachineDto) {
  if (!allowed('del')) return
  const token = intent.begin(); if (!token) return
  const code = row.machineCd
  try {
    await ElMessageBox.confirm(t('設備 {machineCd} を削除しますか？', { machineCd: code }), t('確認'), { type: 'warning' })
    if (!intent.current(token) || !allowed('del')) return
    await machineApi.delete(code)
    if (!intent.current(token)) return
    ElMessage.success(t('削除しました')); void search()
  } catch (error) {
    if (intent.current(token) && !forbidden(error, true)) intent.failed(token, error)
  } finally { intent.finish(token) }
}
function openDowntime(row: MachineDto) {
  if (!allowed('downtime') || intent.busy.value || intent.uncertain.value) return
  Object.assign(dtForm, emptyDt(), { machineCd: row.machineCd }); dtDialogVisible.value = true
}
async function onRegisterDowntime() {
  if (!allowed('downtime') || !dtForm.machineCd) return
  const token = intent.begin(); if (!token) return
  const input = { ...dtForm }
  try {
    const res = await machineApi.registerDowntime(input)
    if (!intent.current(token)) return
    ElMessage.success(t('登録しました（{downtimeNo}）', { downtimeNo: res.data?.downtimeNo }))
    dtDialogVisible.value = false; void search()
  } catch (error) {
    if (intent.current(token) && !forbidden(error, true)) intent.failed(token, error)
  } finally { intent.finish(token) }
}

onMounted(() => { void search(); void loadCreationScope(); window.addEventListener('storage', creationStorageChanged) })
onBeforeUnmount(() => { window.removeEventListener('storage', creationStorageChanged) })
</script>

<style scoped>
.machine-list { padding: 16px; }
.creation-recovery { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; margin-block: 12px; }
.search-card { margin-bottom: 12px; }
.grid-card { margin-bottom: 12px; }

.status-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
  gap: 10px;
}
.machine-cell {
  padding: 12px; border-radius: 8px; cursor: pointer;
  color: white;
  text-align: center;
  transition: transform 0.1s;
  position: relative;
  box-shadow: 0 2px 6px rgba(0,0,0,0.15);
}
.machine-cell:hover { transform: translateY(-2px); }
.machine-cell::before {
  content: ''; position: absolute; top: 8px; right: 8px;
  width: 10px; height: 10px; border-radius: 50%; background: white;
  animation: blink 1.2s infinite;
}
@keyframes blink {
  0%, 50% { opacity: 1; }
  51%, 100% { opacity: 0.3; }
}
.light-green { background: linear-gradient(135deg, #67C23A, #95D475); }
.light-red { background: linear-gradient(135deg, #F56C6C, #FA9C9C); }
.light-yellow { background: linear-gradient(135deg, #E6A23C, #F0BC6F); }
.light-blue { background: linear-gradient(135deg, #409EFF, #66B1FF); }
.light-gray { background: linear-gradient(135deg, #909399, #B1B3B8); }

.machine-cd { font-weight: 700; font-size: 14px; }
.machine-name { font-size: 12px; opacity: 0.9; margin-top: 4px; }
.machine-status { font-size: 11px; opacity: 0.85; margin-top: 4px; }
.machine-wo { font-size: 10px; margin-top: 4px; }
.machine-oee { font-size: 14px; font-weight: 600; margin-top: 4px; }

.empty { padding: 40px; color: #909399; text-align: center; }
</style>
