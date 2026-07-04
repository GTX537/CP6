<!--
  配送業者連携一覧 —— CpPageShell + CpListPage + CpFormDialog 迁移（WMS 批次4）。
  查询列表页：shipmentNo/trackingNo/carrierCd/status 四搜索项 → CpFilterBar；状態列 kind:'tag'+map；配送業者列纯 map；
  重量 col slot（formatQty 3）；作成/配送日は datetime 原样文本；操作列条件按钮 + 常驻「詳細」按钮打开イベント履歴弹窗。
  原「行クリック→詳細」被 CpListPage 无 row-click 透传所限，代偿为操作列「詳細」按钮（gap #16）。
  新建/イベント追加/失败三弹窗迁 CpFormDialog；詳細（timeline 只读）保留 el-dialog（token 化）。
-->
<template>
  <CpPageShell :title="t('wms.carrier.title')" :count="total">
    <template #actions>
      <el-button @click="openCreate">{{ t('wms.common.create') }}</el-button>
    </template>

    <CpListPage
      ref="listRef"
      :columns="columns"
      :fetch="fetchList"
      :search-fields="searchFields"
      :filter-labels="filterLabels"
      @total-change="total = $event"
    >
      <template #col-weightKg="{ row }">{{ row.weightKg != null ? formatQty(row.weightKg, 3) : '' }}</template>
      <template #col-_action="{ row }">
        <el-button link size="small" @click="openDetail(row)">{{ t('wms.carrier.fld.events') }}</el-button>
        <el-button v-if="row.status === 0" link type="primary" size="small" @click="onAct(row, 'pickup')">{{ t('wms.carrier.btn.pickup') }}</el-button>
        <el-button v-if="row.status === 1" link type="warning" size="small" @click="onAct(row, 'transit')">{{ t('wms.carrier.btn.transit') }}</el-button>
        <el-button v-if="row.status === 1 || row.status === 2" link type="success" size="small" @click="onAct(row, 'delivered')">{{ t('wms.carrier.btn.delivered') }}</el-button>
        <el-button v-if="row.status !== 3 && row.status !== 9" link type="danger" size="small" @click="openFail(row)">{{ t('wms.carrier.btn.fail') }}</el-button>
      </template>
    </CpListPage>

    <!-- 詳細：イベント履歴（只读 timeline，保留 el-dialog） -->
    <el-dialog v-model="detailDialog" :title="(detailTarget?.shipmentNo ?? '') + ' — ' + t('wms.carrier.fld.events')" width="700">
      <el-timeline v-if="events.length > 0">
        <el-timeline-item v-for="(e, idx) in events" :key="idx" :timestamp="formatTs(e.ts)" placement="top" :type="eventType(e.status)">
          <h4 style="margin: 0">{{ e.status || '—' }}</h4>
          <p class="ev-msg">{{ e.message }}</p>
          <p v-if="e.location" class="ev-loc">📍 {{ e.location }}</p>
        </el-timeline-item>
      </el-timeline>
      <CpEmpty v-else text="No events" />
      <template #footer>
        <el-button @click="openAddEvent">{{ t('wms.carrier.btn.addEvent') }}</el-button>
        <el-button @click="detailDialog = false">{{ t('wms.common.close') }}</el-button>
      </template>
    </el-dialog>

    <!-- 新建 -->
    <CpFormDialog
      v-model="createDialog"
      :title="t('wms.carrier.dlg.create')"
      width="600"
      :form="createForm"
      :rules="createRules"
      :submit="onCreate"
      :labels="{ cancel: t('wms.common.cancel'), confirm: t('wms.common.save') }"
      @saved="listRef?.reload()"
    >
      <el-row :gutter="12">
        <el-col :span="12"><el-form-item :label="t('wms.carrier.fld.pkg')" prop="packageNo"><el-input v-model="createForm.packageNo" maxlength="20" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.carrier.fld.carrier')" prop="carrierCd">
          <el-select v-model="createForm.carrierCd"><el-option v-for="(l, v) in carrierMap" :key="v" :label="l" :value="v" /></el-select>
        </el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.carrier.fld.tracking')"><el-input v-model="createForm.trackingNo" placeholder="auto if empty" maxlength="50" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.carrier.fld.service')"><el-input v-model="createForm.serviceType" maxlength="20" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.carrier.fld.customer')"><el-input v-model="createForm.customerCd" maxlength="20" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="'TEL'"><el-input v-model="createForm.shipToTel" maxlength="30" /></el-form-item></el-col>
        <el-col :span="24"><el-form-item :label="t('wms.carrier.fld.address')"><el-input v-model="createForm.shipToAddress" maxlength="200" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.carrier.fld.weight')"><el-input-number v-model="createForm.weightKg" :min="0" :precision="3" controls-position="right" style="width: 100%" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.carrier.fld.fee')"><el-input-number v-model="createForm.carrierFee" :min="0" :precision="2" controls-position="right" style="width: 100%" /></el-form-item></el-col>
        <el-col :span="24"><el-form-item :label="t('wms.common.remarks')"><el-input v-model="createForm.remarks" type="textarea" :rows="2" /></el-form-item></el-col>
      </el-row>
    </CpFormDialog>

    <!-- イベント追加 -->
    <CpFormDialog
      v-model="eventDialog"
      :title="t('wms.carrier.dlg.addEvent')"
      width="500"
      :form="eventForm"
      :submit="onAddEvent"
      :labels="{ cancel: t('wms.common.cancel'), confirm: t('wms.common.save') }"
    >
      <el-form-item :label="'Status'"><el-input v-model="eventForm.status" maxlength="20" /></el-form-item>
      <el-form-item :label="'Location'"><el-input v-model="eventForm.location" maxlength="100" /></el-form-item>
      <el-form-item :label="'Message'"><el-input v-model="eventForm.message" type="textarea" :rows="2" /></el-form-item>
    </CpFormDialog>

    <!-- 失败 -->
    <CpFormDialog
      v-model="failDialog"
      :title="t('wms.carrier.btn.fail') + ' — ' + (failTarget?.shipmentNo ?? '')"
      width="420"
      :form="failForm"
      :rules="failRules"
      :submit="onFail"
      :labels="{ cancel: t('wms.common.cancel'), confirm: t('wms.common.save') }"
      @saved="listRef?.reload()"
    >
      <el-form-item :label="t('wms.carrier.fld.reason')" prop="reason">
        <el-input v-model="failForm.reason" type="textarea" :rows="2" />
      </el-form-item>
    </CpFormDialog>
  </CpPageShell>
</template>

<script setup lang="ts">
import { ref, reactive, computed } from 'vue'
import { ElMessage, type FormRules } from 'element-plus'
import { useI18n } from 'vue-i18n'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import CpListPage, { type ListColumn, type ListFetch } from '@/components/templates/CpListPage.vue'
import { type FilterField } from '@/components/templates/CpFilterBar.vue'
import CpFormDialog from '@/components/templates/CpFormDialog.vue'
import CpEmpty from '@/components/base/CpEmpty.vue'
import { type Tone } from '@/components/base/CpTag.vue'
import { carrierApi } from '@/api/wms/connectivity'
import type { CarrierShipment, CarrierEvent } from '@/types/wms/wms'
import { formatQty, formatDateTime } from '@/utils/format'

const { t } = useI18n()

const total = ref<number>()
const listRef = ref<InstanceType<typeof CpListPage>>()

const carrierMap = computed<Record<string, string>>(() => ({
  YAMATO: t('ヤマト運輸'), SAGAWA: t('佐川急便'), JP: t('日本郵便'), SELF: t('自社便'), OTHER: t('その他'),
}))
const statusMap = computed<Record<number, string>>(() => ({
  0: t('wms.carrier.status.created'),
  1: t('wms.carrier.status.pickedup'),
  2: t('wms.carrier.status.transit'),
  3: t('wms.carrier.status.delivered'),
  9: t('wms.carrier.status.failed'),
}))
// 原 statusTag(info/warning/primary/success/danger) → 设计系统 Tone（保色）
function statusTone(s: number): Tone {
  return ({ 0: 'info', 1: 'warn', 2: 'info', 3: 'ok', 9: 'danger' } as const)[s as 0] || 'info'
}
function codeLabel(m: Record<string | number, string>, v: unknown): string {
  return m[v as number] || (v == null ? '' : String(v))
}
function eventType(s?: string): '' | 'primary' | 'success' | 'warning' | 'danger' {
  if (!s) return ''
  if (s.includes('Deliv')) return 'success'
  if (s.includes('Fail')) return 'danger'
  if (s.includes('Pick')) return 'warning'
  return 'primary'
}
function formatTs(ts: string) { return formatDateTime(ts) }

const columns = computed<ListColumn[]>(() => [
  { prop: 'shipmentNo', label: t('wms.carrier.fld.no'), kind: 'mono', width: 180 },
  { prop: 'status', label: t('wms.common.status'), width: 110, kind: 'tag',
    map: (v) => ({ label: codeLabel(statusMap.value, v), tone: statusTone(v as number) }) },
  { prop: 'carrierCd', label: t('wms.carrier.fld.carrier'), width: 100,
    map: (v) => ({ label: codeLabel(carrierMap.value, v) }) },
  { prop: 'trackingNo', label: t('wms.carrier.fld.tracking'), width: 220 },
  { prop: 'packageNo', label: t('wms.carrier.fld.pkg'), width: 160 },
  { prop: 'customerCd', label: t('wms.carrier.fld.customer'), width: 100 },
  { prop: 'shipToAddress', label: t('wms.carrier.fld.address'), minWidth: 180, overflowTooltip: true },
  { prop: 'weightKg', label: t('wms.carrier.fld.weight'), width: 90, align: 'right' },
  { prop: 'pickedUpAt', label: t('wms.carrier.fld.pickedAt'), width: 150 },
  { prop: 'deliveredAt', label: t('wms.carrier.fld.deliveredAt'), width: 150 },
  { prop: '_action', label: t('wms.common.action'), width: 320, fixed: 'right' },
])

const filterLabels = computed(() => ({
  search: t('wms.common.search'),
  reset: t('wms.common.clear'),
}))

const searchFields = computed<FilterField[]>(() => [
  { key: 'shipmentNo', label: t('wms.carrier.fld.no'), type: 'text' },
  { key: 'trackingNo', label: t('wms.carrier.fld.tracking'), type: 'text' },
  {
    key: 'carrierCd', label: t('wms.carrier.fld.carrier'), type: 'select',
    options: Object.entries(carrierMap.value).map(([v, l]) => ({ label: l, value: v })),
  },
  {
    key: 'status', label: t('wms.common.status'), type: 'select',
    options: Object.entries(statusMap.value).map(([v, l]) => ({ label: l, value: Number(v) })),
  },
])

const fetchList: ListFetch = async ({ page, size, filters }) => {
  const f = filters as Record<string, unknown>
  const q: Record<string, unknown> = { pageSize: 100 }
  if (f.shipmentNo) q.shipmentNo = String(f.shipmentNo)
  if (f.trackingNo) q.trackingNo = String(f.trackingNo)
  if (f.carrierCd) q.carrierCd = String(f.carrierCd)
  if (f.status !== undefined && f.status !== '') q.status = Number(f.status)
  const res = await carrierApi.search(q)
  const all = res.data || []
  const start = (page - 1) * size
  return { rows: all.slice(start, start + size), total: all.length }
}

// —— 詳細 / イベント ——
const detailDialog = ref(false)
const detailTarget = ref<CarrierShipment | null>(null)
const events = ref<CarrierEvent[]>([])
function openDetail(row: CarrierShipment) {
  detailTarget.value = row
  try { events.value = row.eventsJson ? JSON.parse(row.eventsJson) : [] }
  catch { events.value = [] }
  detailDialog.value = true
}

const eventDialog = ref(false)
const eventForm = reactive<Record<string, unknown>>({ status: '', location: '', message: '' })
function openAddEvent() {
  eventForm.status = ''
  eventForm.location = ''
  eventForm.message = ''
  eventDialog.value = true
}
async function onAddEvent() {
  if (!detailTarget.value) return
  await carrierApi.addEvent(detailTarget.value.shipmentNo, eventForm as Partial<CarrierEvent>)
  ElMessage.success(t('wms.common.success'))
  const r = await carrierApi.get(detailTarget.value.shipmentNo)
  detailTarget.value = r.data
  try { events.value = r.data.eventsJson ? JSON.parse(r.data.eventsJson) : [] } catch { events.value = [] }
  listRef.value?.reload()
}

// —— 行操作 ——
async function onAct(row: CarrierShipment, kind: 'pickup' | 'transit' | 'delivered') {
  if (kind === 'pickup') await carrierApi.pickUp(row.shipmentNo)
  if (kind === 'transit') await carrierApi.inTransit(row.shipmentNo)
  if (kind === 'delivered') await carrierApi.delivered(row.shipmentNo)
  ElMessage.success(t('wms.common.success'))
  listRef.value?.reload()
}

// —— 新建 ——
const createDialog = ref(false)
const createForm = reactive<Record<string, unknown>>({ carrierCd: 'YAMATO', weightKg: 1.0, carrierFee: 0 })
const createRules = computed<FormRules>(() => ({
  packageNo: [{ required: true, message: t('wms.common.required'), trigger: 'blur' }],
  carrierCd: [{ required: true, message: t('wms.common.required'), trigger: 'change' }],
}))
function openCreate() {
  Object.assign(createForm, {
    packageNo: '', carrierCd: 'YAMATO', trackingNo: '', serviceType: '', customerCd: '',
    shipToTel: '', shipToAddress: '', weightKg: 1.0, carrierFee: 0, remarks: '',
  })
  createDialog.value = true
}
async function onCreate() {
  const res = await carrierApi.create(createForm)
  ElMessage.success(`${t('wms.common.success')}: ${res.data.shipmentNo}`)
}

// —— 失败 ——
const failDialog = ref(false)
const failTarget = ref<CarrierShipment | null>(null)
const failForm = reactive<Record<string, unknown>>({ reason: '' })
const failRules = computed<FormRules>(() => ({
  reason: [{ required: true, message: t('wms.common.required'), trigger: 'blur' }],
}))
function openFail(row: CarrierShipment) {
  failTarget.value = row
  failForm.reason = ''
  failDialog.value = true
}
async function onFail() {
  await carrierApi.fail(failTarget.value!.shipmentNo, String(failForm.reason))
  ElMessage.success(t('wms.common.success'))
}
</script>

<style scoped>
.ev-msg { margin: 4px 0 0; color: var(--cp-ink); }
.ev-loc { margin: 2px 0 0; color: var(--cp-muted); font-size: 12px; }
</style>
