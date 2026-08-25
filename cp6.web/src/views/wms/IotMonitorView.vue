<!--
  IoT センサー監視 —— 監視ダッシュボード特殊页（WMS 批次4）：非表格监控形态，不强套 CpListPage。
  token 化 + 基础件替换：状态 pill el-tag→CpTag、空态 el-empty→CpEmpty、内联灰色→var(--cp-*)；
  新建/投入弹窗迁 CpFormDialog；履歴弹窗（只读表）保留 el-dialog；30 秒轮询 / アラート面板 / 行クリック履歴 全保留。
-->
<template>
  <div class="wms-iot">
    <!-- ヘッダ：アラート + 全体ボタン -->
    <el-card shadow="never" class="alert-card">
      <div class="alert-hd">
        <div>
          <h3 style="margin: 0">{{ t('wms.iot.title') }}</h3>
          <div class="sub">{{ alerts.length }} alerts · {{ sensors.length }} sensors</div>
        </div>
        <div>
          <el-button :icon="MagicStick" type="warning" @click="simulate" :loading="simBusy">{{ t('wms.iot.btn.simulate') }}</el-button>
          <el-button :icon="Refresh" circle @click="reload" :loading="loading" />
        </div>
      </div>

      <el-divider v-if="alerts.length > 0" />
      <el-alert v-for="a in alerts" :key="a.sensorId" type="error" :closable="false" show-icon style="margin-top: 4px">
        <strong>[{{ a.sensorType }}] {{ a.sensorName || a.sensorId }}</strong> @ {{ a.warehouseCd }}/{{ a.locationCd || '—' }}
        ·  value: <b>{{ a.lastValue }}</b>
        · {{ a.alertMessage || '' }}
      </el-alert>
      <CpEmpty v-if="alerts.length === 0" :text="t('wms.iot.msg.noAlerts')" />
    </el-card>

    <el-card shadow="never">
      <template #header>
        <div class="card-hd">
          <span>{{ t('wms.iot.tab.sensors') }}</span>
          <el-button text type="primary" @click="openCreate" style="margin-left: auto">{{ t('wms.iot.btn.create') }}</el-button>
        </div>
      </template>
      <el-table :data="sensors" border stripe size="small" max-height="500" @row-click="onRowClick">
        <el-table-column prop="sensorId" :label="t('wms.iot.fld.id')" width="160" />
        <el-table-column :label="t('wms.iot.fld.type')" width="100">
          <template #default="{ row }">{{ typeMap[row.sensorType] || row.sensorType }}</template>
        </el-table-column>
        <el-table-column prop="sensorName" :label="t('wms.iot.fld.name')" min-width="140" show-overflow-tooltip />
        <el-table-column prop="warehouseCd" :label="t('wms.common.warehouse')" width="100" />
        <el-table-column prop="locationCd" :label="t('wms.common.location')" width="140" />
        <el-table-column :label="t('wms.iot.fld.min') + ' / ' + t('wms.iot.fld.max')" width="160">
          <template #default="{ row }">{{ row.minThreshold ?? '—' }} / {{ row.maxThreshold ?? '—' }} {{ row.unit || '' }}</template>
        </el-table-column>
        <el-table-column :label="t('wms.iot.fld.lastValue')" width="120" align="right">
          <template #default="{ row }">
            <CpTag v-if="row.lastValue != null" :tone="isAlert(row) ? 'danger' : 'ok'">{{ row.lastValue }} {{ row.unit || '' }}</CpTag>
            <span v-else>—</span>
          </template>
        </el-table-column>
        <el-table-column prop="lastReadAt" :label="t('wms.iot.fld.lastRead')" width="180" :formatter="formatDateTimeCell" />
        <el-table-column :label="t('wms.common.action')" width="220" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click.stop="openPost(row)">{{ t('wms.iot.btn.postReading') }}</el-button>
            <el-button link size="small" @click.stop="onRowClick(row)">{{ t('wms.iot.btn.viewHistory') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- 履歴 Dialog（只读表，保留 el-dialog） -->
    <el-dialog v-model="historyDialog" :title="(historyTarget?.sensorId ?? '') + ' — ' + t('wms.iot.tab.history')" width="800">
      <el-table :data="readings" border stripe size="small" max-height="450">
        <el-table-column prop="readAt" label="ReadAt" width="180" :formatter="formatDateTimeCell" />
        <el-table-column prop="value" :label="t('wms.iot.fld.value')" width="120" align="right">
          <template #default="{ row }">{{ row.value }} {{ historyTarget?.unit || '' }}</template>
        </el-table-column>
        <el-table-column :label="t('wms.iot.fld.alert')" width="80" align="center">
          <template #default="{ row }">
            <CpTag v-if="row.isAlert" tone="danger">⚠</CpTag>
          </template>
        </el-table-column>
        <el-table-column prop="alertMessage" label="Message" min-width="220" show-overflow-tooltip />
      </el-table>
    </el-dialog>

    <!-- 新建 -->
    <CpFormDialog
      v-model="createDialog"
      :title="t('wms.iot.dlg.create')"
      width="560"
      :form="createForm"
      :rules="createRules"
      :submit="onCreate"
      :labels="{ cancel: t('wms.common.cancel'), confirm: t('wms.common.save') }"
      @saved="reload"
    >
      <el-row :gutter="12">
        <el-col :span="12"><el-form-item :label="t('wms.iot.fld.type')" prop="sensorType">
          <el-select v-model="createForm.sensorType" @change="onTypeChange">
            <el-option v-for="(l, v) in typeMap" :key="v" :label="l" :value="v" />
          </el-select>
        </el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.iot.fld.name')"><el-input v-model="createForm.sensorName" maxlength="100" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.common.warehouse')" prop="warehouseCd"><el-input v-model="createForm.warehouseCd" maxlength="10" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.common.location')"><el-input v-model="createForm.locationCd" maxlength="30" /></el-form-item></el-col>
        <el-col :span="8"><el-form-item :label="t('wms.iot.fld.unit')"><el-input v-model="createForm.unit" maxlength="10" /></el-form-item></el-col>
        <el-col :span="8"><el-form-item :label="t('wms.iot.fld.min')"><el-input-number v-model="createForm.minThreshold" :precision="2" controls-position="right" style="width: 100%" /></el-form-item></el-col>
        <el-col :span="8"><el-form-item :label="t('wms.iot.fld.max')"><el-input-number v-model="createForm.maxThreshold" :precision="2" controls-position="right" style="width: 100%" /></el-form-item></el-col>
        <el-col :span="24"><el-form-item :label="t('wms.iot.fld.enabled')"><el-switch v-model="createForm.isEnabled" /></el-form-item></el-col>
        <el-col :span="24"><el-form-item :label="t('wms.common.remarks')"><el-input v-model="createForm.remarks" type="textarea" :rows="2" /></el-form-item></el-col>
      </el-row>
    </CpFormDialog>

    <!-- 投入 -->
    <CpFormDialog
      v-model="postDialog"
      :title="t('wms.iot.dlg.postReading') + ' — ' + (postTarget?.sensorId ?? '')"
      width="400"
      :form="postForm"
      :submit="onPost"
      :labels="{ cancel: t('wms.common.cancel'), confirm: t('wms.common.save') }"
      @saved="reload"
    >
      <el-form-item :label="t('wms.iot.fld.value')" prop="value">
        <el-input-number v-model="postForm.value" :precision="2" controls-position="right" style="width: 100%" />
      </el-form-item>
      <el-form-item v-if="postTarget" :label="'Range'">
        <span>{{ postTarget.minThreshold ?? '—' }} 〜 {{ postTarget.maxThreshold ?? '—' }} {{ postTarget.unit || '' }}</span>
      </el-form-item>
    </CpFormDialog>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted } from 'vue'
import { ElMessage, type FormRules } from 'element-plus'
import { Refresh, MagicStick } from '@element-plus/icons-vue'
import { useI18n } from 'vue-i18n'
import CpTag from '@/components/base/CpTag.vue'
import CpEmpty from '@/components/base/CpEmpty.vue'
import CpFormDialog from '@/components/templates/CpFormDialog.vue'
import { iotApi } from '@/api/wms/connectivity'
import type { IotSensor, IotSensorReading, IotAlert } from '@/types/wms/wms'
import { formatDateTimeCell } from '@/utils/format'

const { t } = useI18n()
const loading = ref(false)
const simBusy = ref(false)

const sensors = ref<IotSensor[]>([])
const alerts = ref<IotAlert[]>([])

const historyDialog = ref(false)
const historyTarget = ref<IotSensor | null>(null)
const readings = ref<IotSensorReading[]>([])

const typeMap = computed<Record<string, string>>(() => ({
  TEMP: t('wms.iot.type.temp'),
  HUMID: t('wms.iot.type.humid'),
  SHOCK: t('wms.iot.type.shock'),
  SHELF: t('wms.iot.type.shelf'),
}))

function isAlert(row: IotSensor): boolean {
  if (row.lastValue == null) return false
  if (row.minThreshold != null && row.lastValue < row.minThreshold) return true
  if (row.maxThreshold != null && row.lastValue > row.maxThreshold) return true
  return false
}

async function reload() {
  loading.value = true
  try {
    const [s, a] = await Promise.all([iotApi.searchSensors(), iotApi.alerts()])
    sensors.value = s.data || []
    alerts.value = a.data || []
  } finally { loading.value = false }
}

async function simulate() {
  simBusy.value = true
  try {
    const r = await iotApi.simulate(3)
    ElMessage.success(`${t('wms.iot.msg.simulated')}: ${r.data.generated}`)
    await reload()
  } finally { simBusy.value = false }
}

// —— 新建 ——
const createDialog = ref(false)
const createForm = reactive<Record<string, unknown>>({
  sensorType: 'TEMP', sensorName: '', warehouseCd: '', locationCd: '',
  unit: '℃', minThreshold: 2, maxThreshold: 8, isEnabled: true, remarks: '',
})
const createRules = computed<FormRules>(() => ({
  sensorType: [{ required: true, message: t('wms.common.required'), trigger: 'change' }],
  warehouseCd: [{ required: true, message: t('wms.common.required'), trigger: 'blur' }],
}))
function openCreate() {
  Object.assign(createForm, {
    sensorType: 'TEMP', sensorName: '', warehouseCd: '', locationCd: '',
    unit: '℃', minThreshold: 2, maxThreshold: 8, isEnabled: true, remarks: '',
  })
  createDialog.value = true
}
function onTypeChange() {
  const defaults: Record<string, { unit: string; min: number; max: number }> = {
    TEMP:  { unit: '℃', min: 2, max: 8 },
    HUMID: { unit: '%', min: 30, max: 70 },
    SHOCK: { unit: 'G', min: 0, max: 3 },
    SHELF: { unit: 'ON-OFF', min: 0, max: 1 },
  }
  const d = defaults[createForm.sensorType as string] ?? { unit: '℃', min: 2, max: 8 }
  createForm.unit = d.unit
  createForm.minThreshold = d.min
  createForm.maxThreshold = d.max
}
async function onCreate() {
  const res = await iotApi.createSensor(createForm)
  ElMessage.success(`${t('wms.common.success')}: ${res.data.sensorId}`)
}

// —— 投入 ——
const postDialog = ref(false)
const postTarget = ref<IotSensor | null>(null)
const postForm = reactive<Record<string, unknown>>({ value: 0 })
function openPost(row: IotSensor) {
  postTarget.value = row
  postForm.value = row.lastValue ?? 0
  postDialog.value = true
}
async function onPost() {
  if (!postTarget.value) return
  await iotApi.postReading(postTarget.value.sensorId, Number(postForm.value))
  ElMessage.success(t('wms.common.success'))
}

// —— 履歴 ——
async function onRowClick(row: IotSensor) {
  historyTarget.value = row
  const r = await iotApi.getReadings(row.sensorId, undefined, undefined, 200)
  readings.value = r.data || []
  historyDialog.value = true
}

// 自动 30 秒刷新一次
let timer: number | undefined
onMounted(() => {
  reload()
  timer = window.setInterval(reload, 30000)
})
onUnmounted(() => { if (timer) window.clearInterval(timer) })
</script>

<style scoped>
.wms-iot { padding: 16px; }
.alert-card { margin-bottom: 12px; }
.alert-hd { display: flex; align-items: center; justify-content: space-between; }
.alert-hd .sub { color: var(--cp-muted); font-size: 12px; }
.card-hd { display: flex; align-items: center; }
</style>
