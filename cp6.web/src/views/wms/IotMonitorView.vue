<template>
  <div class="wms-iot">
    <!-- ヘッダ：アラート + 全体ボタン -->
    <el-card shadow="never" class="alert-card">
      <div class="alert-hd">
        <div>
          <h3 style="margin: 0">{{ t('wms.iot.title') }}</h3>
          <div style="color:#909399; font-size: 12px">{{ alerts.length }} alerts · {{ sensors.length }} sensors</div>
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
      <el-empty v-if="alerts.length === 0" :description="t('wms.iot.msg.noAlerts')" :image-size="60" />
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
            <el-tag v-if="row.lastValue != null" :type="isAlert(row) ? 'danger' : 'success'">{{ row.lastValue }} {{ row.unit || '' }}</el-tag>
            <span v-else>—</span>
          </template>
        </el-table-column>
        <el-table-column prop="lastReadAt" :label="t('wms.iot.fld.lastRead')" width="160" />
        <el-table-column :label="t('wms.common.action')" width="220" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click.stop="openPost(row)">{{ t('wms.iot.btn.postReading') }}</el-button>
            <el-button link size="small" @click.stop="onRowClick(row)">{{ t('wms.iot.btn.viewHistory') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- 履歴 Dialog -->
    <el-dialog v-model="historyDialog" :title="historyTarget?.sensorId + ' — ' + t('wms.iot.tab.history')" width="800">
      <el-table :data="readings" border stripe size="small" max-height="450">
        <el-table-column prop="readAt" label="ReadAt" width="180" />
        <el-table-column prop="value" :label="t('wms.iot.fld.value')" width="120" align="right">
          <template #default="{ row }">{{ row.value }} {{ historyTarget?.unit || '' }}</template>
        </el-table-column>
        <el-table-column :label="t('wms.iot.fld.alert')" width="80" align="center">
          <template #default="{ row }">
            <el-tag v-if="row.isAlert" type="danger" size="small">⚠</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="alertMessage" label="Message" min-width="220" show-overflow-tooltip />
      </el-table>
    </el-dialog>

    <!-- 新建 -->
    <el-dialog v-model="createDialog" :title="t('wms.iot.dlg.create')" width="560">
      <el-form v-if="editing" :model="editing" label-width="140px" size="small">
        <el-row :gutter="12">
          <el-col :span="12"><el-form-item :label="t('wms.iot.fld.type')" required>
            <el-select v-model="editing.sensorType" @change="onTypeChange">
              <el-option v-for="(l, v) in typeMap" :key="v" :label="l" :value="v" />
            </el-select>
          </el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.iot.fld.name')"><el-input v-model="editing.sensorName" maxlength="100" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.common.warehouse')" required><el-input v-model="editing.warehouseCd" maxlength="10" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.common.location')"><el-input v-model="editing.locationCd" maxlength="30" /></el-form-item></el-col>
          <el-col :span="8"><el-form-item :label="t('wms.iot.fld.unit')"><el-input v-model="editing.unit" maxlength="10" /></el-form-item></el-col>
          <el-col :span="8"><el-form-item :label="t('wms.iot.fld.min')"><el-input-number v-model="editing.minThreshold" :precision="2" controls-position="right" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="8"><el-form-item :label="t('wms.iot.fld.max')"><el-input-number v-model="editing.maxThreshold" :precision="2" controls-position="right" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="24"><el-form-item :label="t('wms.iot.fld.enabled')"><el-switch v-model="editing.isEnabled" /></el-form-item></el-col>
          <el-col :span="24"><el-form-item :label="t('wms.common.remarks')"><el-input v-model="editing.remarks" type="textarea" :rows="2" /></el-form-item></el-col>
        </el-row>
      </el-form>
      <template #footer>
        <el-button @click="createDialog = false">{{ t('wms.common.cancel') }}</el-button>
        <el-button type="primary" @click="onCreate" :loading="saving">{{ t('wms.common.save') }}</el-button>
      </template>
    </el-dialog>

    <!-- 投入 -->
    <el-dialog v-model="postDialog" :title="t('wms.iot.dlg.postReading') + ' — ' + postTarget?.sensorId" width="400">
      <el-form label-width="100px" size="small">
        <el-form-item :label="t('wms.iot.fld.value')" required>
          <el-input-number v-model="postValue" :precision="2" controls-position="right" style="width: 100%" />
        </el-form-item>
        <el-form-item v-if="postTarget" :label="'Range'">
          <span>{{ postTarget.minThreshold ?? '—' }} 〜 {{ postTarget.maxThreshold ?? '—' }} {{ postTarget.unit || '' }}</span>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="postDialog = false">{{ t('wms.common.cancel') }}</el-button>
        <el-button type="primary" @click="onPost">{{ t('wms.common.save') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted } from 'vue'
import { ElMessage } from 'element-plus'
import { Refresh, MagicStick } from '@element-plus/icons-vue'
import { useI18n } from 'vue-i18n'
import { iotApi } from '@/api/wms/connectivity'
import type { IotSensor, IotSensorReading, IotAlert } from '@/types/wms'

const { t } = useI18n()
const loading = ref(false)
const simBusy = ref(false)
const saving = ref(false)

const sensors = ref<IotSensor[]>([])
const alerts = ref<IotAlert[]>([])

const createDialog = ref(false)
const editing = ref<any>(null)

const postDialog = ref(false)
const postTarget = ref<IotSensor | null>(null)
const postValue = ref(0)

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

function openCreate() {
  editing.value = {
    sensorType: 'TEMP', sensorName: '', warehouseCd: '', locationCd: '',
    unit: '℃', minThreshold: 2, maxThreshold: 8, isEnabled: true,
  }
  createDialog.value = true
}

function onTypeChange() {
  const defaults: Record<string, any> = {
    TEMP:  { unit: '℃', min: 2, max: 8 },
    HUMID: { unit: '%', min: 30, max: 70 },
    SHOCK: { unit: 'G', min: 0, max: 3 },
    SHELF: { unit: 'ON-OFF', min: 0, max: 1 },
  }
  const d = defaults[editing.value.sensorType] || defaults.TEMP
  editing.value.unit = d.unit
  editing.value.minThreshold = d.min
  editing.value.maxThreshold = d.max
}

async function onCreate() {
  saving.value = true
  try {
    const res = await iotApi.createSensor(editing.value)
    ElMessage.success(`${t('wms.common.success')}: ${res.data.sensorId}`)
    createDialog.value = false
    await reload()
  } finally { saving.value = false }
}

function openPost(row: IotSensor) {
  postTarget.value = row
  postValue.value = row.lastValue ?? 0
  postDialog.value = true
}

async function onPost() {
  if (!postTarget.value) return
  await iotApi.postReading(postTarget.value.sensorId, postValue.value)
  ElMessage.success(t('wms.common.success'))
  postDialog.value = false
  await reload()
}

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
.card-hd { display: flex; align-items: center; }
</style>
