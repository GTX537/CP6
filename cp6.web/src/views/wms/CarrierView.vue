<template>
  <div class="wms-carrier">
    <el-card shadow="never" class="search-card">
      <el-form :model="query" inline size="small">
        <el-form-item :label="t('wms.carrier.fld.no')"><el-input v-model="query.shipmentNo" clearable style="width: 180px" /></el-form-item>
        <el-form-item :label="t('wms.carrier.fld.tracking')"><el-input v-model="query.trackingNo" clearable style="width: 200px" /></el-form-item>
        <el-form-item :label="t('wms.carrier.fld.carrier')">
          <el-select v-model="query.carrierCd" clearable style="width: 120px">
            <el-option v-for="(l, v) in carrierMap" :key="v" :label="l" :value="v" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('wms.common.status')">
          <el-select v-model="query.status" clearable style="width: 130px">
            <el-option v-for="(l, v) in statusMap" :key="v" :label="l" :value="Number(v)" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="reload" :loading="loading">{{ t('wms.common.search') }}</el-button>
          <el-button @click="openCreate">{{ t('wms.common.create') }}</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <el-table :data="rows" border stripe size="small" max-height="650" highlight-current-row @row-click="onRowClick">
        <el-table-column prop="shipmentNo" :label="t('wms.carrier.fld.no')" width="180" />
        <el-table-column :label="t('wms.common.status')" width="110">
          <template #default="{ row }"><el-tag :type="statusTag(row.status)" size="small">{{ statusMap[row.status] }}</el-tag></template>
        </el-table-column>
        <el-table-column :label="t('wms.carrier.fld.carrier')" width="100">
          <template #default="{ row }">{{ carrierMap[row.carrierCd] || row.carrierCd }}</template>
        </el-table-column>
        <el-table-column prop="trackingNo" :label="t('wms.carrier.fld.tracking')" width="220" />
        <el-table-column prop="packageNo" :label="t('wms.carrier.fld.pkg')" width="160" />
        <el-table-column prop="customerCd" :label="t('wms.carrier.fld.customer')" width="100" />
        <el-table-column prop="shipToAddress" :label="t('wms.carrier.fld.address')" min-width="180" show-overflow-tooltip />
        <el-table-column prop="weightKg" :label="t('wms.carrier.fld.weight')" width="90" align="right">
          <template #default="{ row }">{{ row.weightKg != null ? formatQty(row.weightKg, 3) : '' }}</template>
        </el-table-column>
        <el-table-column prop="pickedUpAt" :label="t('wms.carrier.fld.pickedAt')" width="150" />
        <el-table-column prop="deliveredAt" :label="t('wms.carrier.fld.deliveredAt')" width="150" />
        <el-table-column :label="t('wms.common.action')" width="280" fixed="right">
          <template #default="{ row }">
            <el-button v-if="row.status === 0" link type="primary" size="small" @click.stop="onAct(row, 'pickup')">{{ t('wms.carrier.btn.pickup') }}</el-button>
            <el-button v-if="row.status === 1" link type="warning" size="small" @click.stop="onAct(row, 'transit')">{{ t('wms.carrier.btn.transit') }}</el-button>
            <el-button v-if="row.status === 1 || row.status === 2" link type="success" size="small" @click.stop="onAct(row, 'delivered')">{{ t('wms.carrier.btn.delivered') }}</el-button>
            <el-button v-if="row.status !== 3 && row.status !== 9" link type="danger" size="small" @click.stop="openFail(row)">{{ t('wms.carrier.btn.fail') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- 詳細：イベント履歴 -->
    <el-dialog v-model="detailDialog" :title="detailTarget?.shipmentNo + ' — ' + t('wms.carrier.fld.events')" width="700">
      <el-timeline v-if="events.length > 0">
        <el-timeline-item v-for="(e, idx) in events" :key="idx" :timestamp="formatTs(e.ts)" placement="top" :type="eventType(e.status)">
          <h4 style="margin: 0">{{ e.status || '—' }}</h4>
          <p style="margin: 4px 0 0; color: #606266">{{ e.message }}</p>
          <p v-if="e.location" style="margin: 2px 0 0; color: #909399; font-size: 12px">📍 {{ e.location }}</p>
        </el-timeline-item>
      </el-timeline>
      <el-empty v-else description="No events" />
      <template #footer>
        <el-button @click="openAddEvent">{{ t('wms.carrier.btn.addEvent') }}</el-button>
        <el-button @click="detailDialog = false">{{ t('wms.common.close') }}</el-button>
      </template>
    </el-dialog>

    <!-- 新建 -->
    <el-dialog v-model="createDialog" :title="t('wms.carrier.dlg.create')" width="600">
      <el-form v-if="editing" :model="editing" label-width="140px" size="small">
        <el-row :gutter="12">
          <el-col :span="12"><el-form-item :label="t('wms.carrier.fld.pkg')" required><el-input v-model="editing.packageNo" maxlength="20" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.carrier.fld.carrier')" required>
            <el-select v-model="editing.carrierCd"><el-option v-for="(l, v) in carrierMap" :key="v" :label="l" :value="v" /></el-select>
          </el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.carrier.fld.tracking')"><el-input v-model="editing.trackingNo" placeholder="auto if empty" maxlength="50" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.carrier.fld.service')"><el-input v-model="editing.serviceType" maxlength="20" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.carrier.fld.customer')"><el-input v-model="editing.customerCd" maxlength="20" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="'TEL'"><el-input v-model="editing.shipToTel" maxlength="30" /></el-form-item></el-col>
          <el-col :span="24"><el-form-item :label="t('wms.carrier.fld.address')"><el-input v-model="editing.shipToAddress" maxlength="200" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.carrier.fld.weight')"><el-input-number v-model="editing.weightKg" :min="0" :precision="3" controls-position="right" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.carrier.fld.fee')"><el-input-number v-model="editing.carrierFee" :min="0" :precision="2" controls-position="right" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="24"><el-form-item :label="t('wms.common.remarks')"><el-input v-model="editing.remarks" type="textarea" :rows="2" /></el-form-item></el-col>
        </el-row>
      </el-form>
      <template #footer>
        <el-button @click="createDialog = false">{{ t('wms.common.cancel') }}</el-button>
        <el-button type="primary" @click="onCreate" :loading="saving">{{ t('wms.common.save') }}</el-button>
      </template>
    </el-dialog>

    <!-- イベント追加 -->
    <el-dialog v-model="eventDialog" :title="t('wms.carrier.dlg.addEvent')" width="500">
      <el-form label-width="100px" size="small">
        <el-form-item :label="'Status'"><el-input v-model="eventForm.status" maxlength="20" /></el-form-item>
        <el-form-item :label="'Location'"><el-input v-model="eventForm.location" maxlength="100" /></el-form-item>
        <el-form-item :label="'Message'"><el-input v-model="eventForm.message" type="textarea" :rows="2" /></el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="eventDialog = false">{{ t('wms.common.cancel') }}</el-button>
        <el-button type="primary" @click="onAddEvent">{{ t('wms.common.save') }}</el-button>
      </template>
    </el-dialog>

    <!-- 失败 Dialog -->
    <el-dialog v-model="failDialog" :title="t('wms.carrier.btn.fail') + ' — ' + failTarget?.shipmentNo" width="420">
      <el-form label-width="100px" size="small">
        <el-form-item :label="t('wms.carrier.fld.reason')" required>
          <el-input v-model="failReason" type="textarea" :rows="2" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="failDialog = false">{{ t('wms.common.cancel') }}</el-button>
        <el-button type="danger" @click="onFail">{{ t('wms.common.save') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { carrierApi } from '@/api/wms/connectivity'
import type { CarrierShipment, CarrierSearchQuery, CarrierEvent } from '@/types/wms/wms'
import { formatQty, formatDateTime } from '@/utils/format'

const { t } = useI18n()
const query = reactive<CarrierSearchQuery>({ pageSize: 100 })
const rows = ref<CarrierShipment[]>([])
const loading = ref(false)
const saving = ref(false)

const createDialog = ref(false)
const editing = ref<any>(null)

const detailDialog = ref(false)
const detailTarget = ref<CarrierShipment | null>(null)
const events = ref<CarrierEvent[]>([])

const eventDialog = ref(false)
const eventForm = reactive<Partial<CarrierEvent>>({ status: '', location: '', message: '' })

const failDialog = ref(false)
const failTarget = ref<CarrierShipment | null>(null)
const failReason = ref('')

const carrierMap: Record<string, string> = {
  YAMATO: t('ヤマト運輸'), SAGAWA: t('佐川急便'), JP: t('日本郵便'), SELF: t('自社便'), OTHER: t('その他'),
}

const statusMap = computed<Record<number, string>>(() => ({
  0: t('wms.carrier.status.created'),
  1: t('wms.carrier.status.pickedup'),
  2: t('wms.carrier.status.transit'),
  3: t('wms.carrier.status.delivered'),
  9: t('wms.carrier.status.failed'),
}))

function statusTag(s: number): 'info' | 'warning' | 'primary' | 'success' | 'danger' {
  return ({ 0: 'info', 1: 'warning', 2: 'primary', 3: 'success', 9: 'danger' } as const)[s as 0] || 'info'
}
function eventType(s?: string): '' | 'primary' | 'success' | 'warning' | 'danger' {
  if (!s) return ''
  if (s.includes('Deliv')) return 'success'
  if (s.includes('Fail')) return 'danger'
  if (s.includes('Pick')) return 'warning'
  return 'primary'
}
function formatTs(ts: string) {
  return formatDateTime(ts)
}

async function reload() {
  loading.value = true
  try { rows.value = (await carrierApi.search(query)).data || [] }
  finally { loading.value = false }
}

function openCreate() {
  editing.value = { carrierCd: 'YAMATO', weightKg: 1.0, carrierFee: 0 }
  createDialog.value = true
}

async function onCreate() {
  saving.value = true
  try {
    const res = await carrierApi.create(editing.value)
    ElMessage.success(`${t('wms.common.success')}: ${res.data.shipmentNo}`)
    createDialog.value = false
    await reload()
  } finally { saving.value = false }
}

async function onAct(row: CarrierShipment, kind: 'pickup' | 'transit' | 'delivered') {
  if (kind === 'pickup') await carrierApi.pickUp(row.shipmentNo)
  if (kind === 'transit') await carrierApi.inTransit(row.shipmentNo)
  if (kind === 'delivered') await carrierApi.delivered(row.shipmentNo)
  ElMessage.success(t('wms.common.success'))
  await reload()
}

function onRowClick(row: CarrierShipment) {
  detailTarget.value = row
  try {
    events.value = row.eventsJson ? JSON.parse(row.eventsJson) : []
  } catch { events.value = [] }
  detailDialog.value = true
}

function openAddEvent() {
  eventForm.status = ''
  eventForm.location = ''
  eventForm.message = ''
  eventDialog.value = true
}

async function onAddEvent() {
  if (!detailTarget.value) return
  await carrierApi.addEvent(detailTarget.value.shipmentNo, eventForm)
  ElMessage.success(t('wms.common.success'))
  eventDialog.value = false
  // refresh
  const r = await carrierApi.get(detailTarget.value.shipmentNo)
  detailTarget.value = r.data
  try { events.value = r.data.eventsJson ? JSON.parse(r.data.eventsJson) : [] } catch { events.value = [] }
  await reload()
}

function openFail(row: CarrierShipment) {
  failTarget.value = row
  failReason.value = ''
  failDialog.value = true
}

async function onFail() {
  if (!failTarget.value || !failReason.value) return
  await carrierApi.fail(failTarget.value.shipmentNo, failReason.value)
  ElMessage.success(t('wms.common.success'))
  failDialog.value = false
  await reload()
}

onMounted(reload)
</script>

<style scoped>
.wms-carrier { padding: 16px; }
.search-card { margin-bottom: 12px; }
</style>
