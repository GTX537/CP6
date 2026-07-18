<template>
  <div class="machine-list">
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
        <el-button v-if="isEdit" v-permission="'mes-machine:edit'" type="primary" :loading="saving" @click="onSave">{{ t('保存') }}</el-button>
        <el-button v-else type="primary" :loading="saving" @click="onSave">{{ t('保存') }}</el-button>
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
        <el-button type="primary" :loading="saving" @click="onRegisterDowntime">{{ t('登録') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
import { ref, reactive, computed, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Check } from '@element-plus/icons-vue'
import { machineApi } from '@/api/mes/mes'
import {
  MACHINE_STATUS_OPTIONS,
  DOWNTIME_TYPE_OPTIONS,
  type MachineDto,
  type MachineSearchQuery,
  type MachineDowntimeDto,
} from '@/types/mes/mes'

const loading = ref(false)
const saving = ref(false)
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

async function search() {
  loading.value = true
  try {
    const res = await machineApi.search(query)
    rows.value = res.data || []
  } finally {
    loading.value = false
  }
}

function reset() {
  Object.assign(query, defaultQuery())
  search()
}

function openCreate() {
  Object.assign(form, emptyForm())
  isEdit.value = false
  dialogVisible.value = true
}

function openEdit(row: MachineDto) {
  Object.assign(form, emptyForm(), row)
  isEdit.value = true
  dialogVisible.value = true
}

function resetForm() { Object.assign(form, emptyForm()) }

async function onSave() {
  if (!form.machineCd || !form.machineName) {
    ElMessage.error(t('設備CDと設備名は必須です'))
    return
  }
  saving.value = true
  try {
    if (isEdit.value) {
      await machineApi.update(form.machineCd, form)
      ElMessage.success(t('ME-MSG-041: 更新しました'))
    } else {
      await machineApi.create(form)
      ElMessage.success(t('ME-MSG-041: 登録しました'))
    }
    dialogVisible.value = false
    search()
  } finally {
    saving.value = false
  }
}

async function onDelete(row: MachineDto) {
  try {
    await ElMessageBox.confirm(t('設備 {machineCd} を削除しますか？', { machineCd: row.machineCd }), t('確認'), { type: 'warning' })
    await machineApi.delete(row.machineCd)
    ElMessage.success(t('削除しました'))
    search()
  } catch (e) { /* cancelled */ }
}

function openDowntime(row: MachineDto) {
  Object.assign(dtForm, emptyDt(), { machineCd: row.machineCd })
  dtDialogVisible.value = true
}

async function onRegisterDowntime() {
  if (!dtForm.machineCd) return
  saving.value = true
  try {
    const res = await machineApi.registerDowntime(dtForm)
    ElMessage.success(t('登録しました（{downtimeNo}）', { downtimeNo: res.data?.downtimeNo }))
    dtDialogVisible.value = false
    search()
  } finally {
    saving.value = false
  }
}

onMounted(() => { search() })
</script>

<style scoped>
.machine-list { padding: 16px; }
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
