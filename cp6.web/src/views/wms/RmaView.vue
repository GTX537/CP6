<template>
  <div class="wms-rma">
    <el-card v-if="mode === 'list'" shadow="never" class="search-card">
      <el-form :model="query" inline size="small">
        <el-form-item :label="t('wms.rma.fld.no')"><el-input v-model="query.rmaNo" clearable style="width: 180px" /></el-form-item>
        <el-form-item :label="t('wms.outbound.fld.customerCd')"><el-input v-model="query.customerCd" clearable style="width: 130px" /></el-form-item>
        <el-form-item :label="t('wms.rma.fld.originalShipping')"><el-input v-model="query.originalShippingNo" clearable style="width: 180px" /></el-form-item>
        <el-form-item :label="t('wms.common.status')">
          <el-select v-model="query.status" clearable style="width: 140px">
            <el-option v-for="(l, v) in statusMap" :key="v" :label="l" :value="Number(v)" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="reload" :loading="loading">{{ t('wms.common.search') }}</el-button>
          <el-button @click="openCreate">{{ t('wms.common.create') }}</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card v-if="mode === 'list'" shadow="never">
      <el-table :data="rows" border stripe size="small" max-height="650" highlight-current-row>
        <el-table-column prop="rmaNo" :label="t('wms.rma.fld.no')" width="180" />
        <el-table-column :label="t('wms.common.status')" width="120">
          <template #default="{ row }"><el-tag :type="statusTagOf(row.status)" size="small">{{ statusMap[row.status] }}</el-tag></template>
        </el-table-column>
        <el-table-column prop="customerCd" :label="t('wms.outbound.fld.customerCd')" width="100" />
        <el-table-column prop="customerName" :label="t('wms.outbound.fld.customerName')" min-width="140" show-overflow-tooltip />
        <el-table-column prop="originalShippingNo" :label="t('wms.rma.fld.originalShipping')" width="180" />
        <el-table-column prop="appliedDate" :label="t('wms.rma.fld.appliedDate')" width="120">
          <template #default="{ row }">{{ row.appliedDate?.slice(0, 10) }}</template>
        </el-table-column>
        <el-table-column prop="warehouseCd" :label="t('wms.common.warehouse')" width="90" />
        <el-table-column prop="returnReason" :label="t('wms.rma.fld.returnReason')" min-width="180" show-overflow-tooltip />
        <el-table-column :label="t('wms.common.action')" width="100" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="openDetail(row.rmaNo)">{{ t('wms.common.open') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- Detail / Create -->
    <template v-if="mode === 'detail' && current">
      <el-card shadow="never">
        <template #header>
          <div style="display: flex; align-items: center; gap: 12px">
            <span style="font-weight: 600">{{ isNew ? t('wms.rma.titleNew') : `${t('wms.rma.title')} [${current.rmaNo}]` }}</span>
            <el-tag v-if="!isNew" :type="statusTagOf(current.status)" size="small">{{ statusMap[current.status] }}</el-tag>
          </div>
        </template>
        <el-form :model="current" label-width="160px" size="small">
          <el-row :gutter="16">
            <el-col :span="8"><el-form-item :label="t('wms.outbound.fld.customerCd')" required><el-input v-model="current.customerCd" :disabled="!isNew" maxlength="20" /></el-form-item></el-col>
            <el-col :span="8"><el-form-item :label="t('wms.outbound.fld.customerName')"><el-input v-model="current.customerName" :disabled="!isNew" maxlength="100" /></el-form-item></el-col>
            <el-col :span="8"><el-form-item :label="t('wms.rma.fld.appliedDate')"><el-date-picker v-model="current.appliedDate" type="date" value-format="YYYY-MM-DD" :disabled="!isNew" style="width: 100%" /></el-form-item></el-col>
            <el-col :span="8"><el-form-item :label="t('wms.common.warehouse')" required><el-input v-model="current.warehouseCd" :disabled="!isNew" maxlength="10" /></el-form-item></el-col>
            <el-col :span="16"><el-form-item :label="t('wms.rma.fld.originalShipping')"><el-input v-model="current.originalShippingNo" :disabled="!isNew" maxlength="20" /></el-form-item></el-col>
            <el-col :span="24"><el-form-item :label="t('wms.rma.fld.returnReason')"><el-input v-model="current.returnReason" type="textarea" :rows="2" :disabled="!isNew" /></el-form-item></el-col>
          </el-row>
        </el-form>
      </el-card>

      <el-card shadow="never" style="margin-top: 12px">
        <template #header>
          <div style="display: flex; justify-content: space-between; align-items: center">
            <span style="font-weight: 600">{{ t('wms.common.detail') }}</span>
            <el-button v-if="isNew" type="primary" size="small" @click="addLine">{{ t('wms.common.addLine') }}</el-button>
          </div>
        </template>
        <el-table :data="current.details" border size="small" max-height="500">
          <el-table-column type="index" :label="t('wms.common.line')" width="50" align="center" />
          <el-table-column :label="t('wms.common.product')" min-width="120">
            <template #default="{ row }">
              <el-input v-if="isNew" v-model="row.productCd" maxlength="20" />
              <span v-else>{{ row.productCd }}</span>
            </template>
          </el-table-column>
          <el-table-column :label="t('wms.common.productName')" min-width="160">
            <template #default="{ row }">
              <el-input v-if="isNew" v-model="row.productName" maxlength="100" />
              <span v-else>{{ row.productName }}</span>
            </template>
          </el-table-column>
          <el-table-column :label="t('wms.common.lot')" width="140">
            <template #default="{ row }">
              <el-input v-if="isNew" v-model="row.lotNo" maxlength="30" />
              <span v-else>{{ row.lotNo || '—' }}</span>
            </template>
          </el-table-column>
          <el-table-column :label="t('wms.common.qty')" width="110">
            <template #default="{ row }">
              <el-input-number v-if="isNew" v-model="row.qty" :min="0" :precision="2" controls-position="right" style="width: 100%" />
              <span v-else>{{ formatQty(row.qty) }}</span>
            </template>
          </el-table-column>
          <el-table-column :label="t('wms.rma.col.condition')" width="120">
            <template #default="{ row }">
              <el-select v-model="row.conditionLevel" :disabled="!isNew" style="width: 100%">
                <el-option v-for="(l, v) in conditionMap" :key="v" :label="l" :value="v" />
              </el-select>
            </template>
          </el-table-column>
          <el-table-column :label="t('wms.rma.col.judgement')" width="140">
            <template #default="{ row }">
              <el-select v-model="row.judgement" :disabled="!canJudge" clearable style="width: 100%">
                <el-option v-for="(l, v) in judgementMap" :key="v" :label="l" :value="v" />
              </el-select>
            </template>
          </el-table-column>
          <el-table-column :label="t('wms.rma.col.destLoc')" width="160">
            <template #default="{ row }">
              <el-input v-model="row.destLocationCd" :disabled="!canJudge" maxlength="30" />
            </template>
          </el-table-column>
          <el-table-column label="TXN" width="100">
            <template #default="{ row }">
              <el-tooltip v-if="row.inboundTxnNo" :content="row.inboundTxnNo"><el-tag size="small">IN</el-tag></el-tooltip>
              <el-tooltip v-if="row.dispositionTxnNo" :content="row.dispositionTxnNo"><el-tag type="success" size="small" style="margin-left: 4px">DISP</el-tag></el-tooltip>
            </template>
          </el-table-column>
          <el-table-column v-if="isNew" :label="t('wms.common.action')" width="80" fixed="right">
            <template #default="{ $index }">
              <el-button link type="danger" size="small" @click="removeLine($index)">{{ t('wms.common.delete') }}</el-button>
            </template>
          </el-table-column>
        </el-table>
      </el-card>

      <el-affix position="bottom" :offset="0">
        <div class="action-bar">
          <el-button @click="mode = 'list'">{{ t('wms.common.back') }}</el-button>
          <el-button v-if="isNew" type="primary" @click="onSave" :loading="saving">{{ t('wms.common.save') }}</el-button>
          <el-button v-if="canReceive" type="primary" @click="onReceive">{{ t('wms.rma.btn.receive') }}</el-button>
          <el-button v-if="canStartInsp" type="primary" @click="onStartInsp">{{ t('wms.rma.btn.startInspection') }}</el-button>
          <el-button v-if="canJudge" type="success" @click="onJudge">{{ t('wms.rma.btn.judge') }}</el-button>
          <el-button v-if="canClose" type="success" plain @click="onClose">{{ t('wms.rma.btn.close') }}</el-button>
          <el-button v-if="canCancel" type="danger" plain @click="onCancel">{{ t('wms.outbound.btn.cancel') }}</el-button>
        </div>
      </el-affix>
    </template>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { rmaApi } from '@/api/wms/rma'
import type { RmaHeader, RmaDetail, RmaSearchQuery, RmaDispositionInput } from '@/types/wms/wms'

const { t } = useI18n()

const mode = ref<'list' | 'detail'>('list')
const query = reactive<RmaSearchQuery>({ pageSize: 100 })
const rows = ref<RmaHeader[]>([])
const loading = ref(false)

const current = ref<RmaHeader | null>(null)
const saving = ref(false)

const statusMap = computed<Record<number, string>>(() => ({
  0: t('wms.rma.status.applied'),
  1: t('wms.rma.status.authorized'),
  2: t('wms.rma.status.received'),
  3: t('wms.rma.status.inspecting'),
  4: t('wms.rma.status.judged'),
  5: t('wms.rma.status.closed'),
  9: t('wms.rma.status.cancelled'),
}))
const conditionMap = computed<Record<string, string>>(() => ({
  NEW: t('wms.rma.cond.new'),
  OPEN: t('wms.rma.cond.open'),
  DAMAGED: t('wms.rma.cond.damaged'),
}))
const judgementMap = computed<Record<string, string>>(() => ({
  RESELL: t('wms.rma.judge.resell'),
  REPAIR: t('wms.rma.judge.repair'),
  SCRAP: t('wms.rma.judge.scrap'),
  SUPPLIER_RETURN: t('wms.rma.judge.supplierReturn'),
}))

const isNew = computed(() => current.value !== null && !current.value.rmaNo)
const canReceive = computed(() => current.value && current.value.status === 1)
const canStartInsp = computed(() => current.value && current.value.status === 2)
const canJudge = computed(() => current.value && (current.value.status === 2 || current.value.status === 3))
const canClose = computed(() => current.value && current.value.status === 4)
const canCancel = computed(() => current.value && current.value.status !== 5 && current.value.status !== 9 && !isNew.value)

function statusTagOf(s: number): 'info' | 'primary' | 'warning' | 'success' | 'danger' {
  return ({ 0: 'info', 1: 'primary', 2: 'warning', 3: 'warning', 4: 'success', 5: 'success', 9: 'danger' } as const)[s as 0] || 'info'
}
function formatQty(n: number) { return Number(n || 0).toLocaleString('ja-JP', { maximumFractionDigits: 4 }) }

async function reload() {
  loading.value = true
  try { rows.value = (await rmaApi.search(query)).data || [] }
  finally { loading.value = false }
}

function openCreate() {
  current.value = {
    customerCd: '',
    appliedDate: new Date().toISOString().slice(0, 10),
    warehouseCd: '',
    status: 0,
    details: [],
  }
  mode.value = 'detail'
}

async function openDetail(no: string) {
  const res = await rmaApi.get(no)
  current.value = res.data
  mode.value = 'detail'
}

function addLine() {
  if (!current.value) return
  current.value.details.push({
    lineNo: current.value.details.length + 1, productCd: '', lotNo: '', qty: 0,
    conditionLevel: 'OPEN',
  } as RmaDetail)
}
function removeLine(idx: number) {
  if (!current.value) return
  current.value.details.splice(idx, 1)
  current.value.details.forEach((d, i) => (d.lineNo = i + 1))
}

async function onSave() {
  if (!current.value) return
  if (!current.value.customerCd || !current.value.warehouseCd) { ElMessage.warning(t('wms.common.required')); return }
  if (current.value.details.length === 0) { ElMessage.warning(t('wms.inbound.msg.noDetail')); return }
  saving.value = true
  try {
    const res = await rmaApi.create(current.value)
    ElMessage.success(`${t('wms.common.success')}: ${res.data.rmaNo}`)
    await openDetail(res.data.rmaNo)
  } finally { saving.value = false }
}

async function onReceive() {
  if (!current.value) return
  try {
    await ElMessageBox.confirm(t('wms.inbound.msg.confirmAsk'), t('wms.common.confirm'), { type: 'warning' })
    await rmaApi.receive(current.value.rmaNo!)
    ElMessage.success(t('wms.common.success'))
    await openDetail(current.value.rmaNo!)
  } catch { /* */ }
}

async function onStartInsp() {
  if (!current.value) return
  await rmaApi.startInspection(current.value.rmaNo!)
  ElMessage.success(t('wms.common.success'))
  await openDetail(current.value.rmaNo!)
}

async function onJudge() {
  if (!current.value) return
  // 全行に判定があるか確認
  const missing = current.value.details.filter(d => !d.judgement)
  if (missing.length > 0) { ElMessage.warning(t('wms.rma.msg.judgementRequired')); return }
  try {
    await ElMessageBox.confirm(t('wms.outbound.msg.allocateAsk'), t('wms.common.confirm'), { type: 'warning' })
    const inputs: RmaDispositionInput[] = current.value.details.map(d => ({
      lineNo: d.lineNo, judgement: d.judgement!, destLocationCd: d.destLocationCd,
    }))
    await rmaApi.judge(current.value.rmaNo!, inputs)
    ElMessage.success(t('wms.common.success'))
    await openDetail(current.value.rmaNo!)
  } catch { /* */ }
}

async function onClose() {
  if (!current.value) return
  await rmaApi.close(current.value.rmaNo!)
  ElMessage.success(t('wms.common.success'))
  await openDetail(current.value.rmaNo!)
}

async function onCancel() {
  if (!current.value) return
  try {
    await ElMessageBox.confirm(t('wms.inbound.msg.cancelAsk'), t('wms.common.confirm'), { type: 'warning' })
    await rmaApi.cancel(current.value.rmaNo!)
    ElMessage.success(t('wms.common.success'))
    await openDetail(current.value.rmaNo!)
  } catch { /* */ }
}

onMounted(reload)
</script>

<style scoped>
.wms-rma { padding: 16px; padding-bottom: 60px; }
.search-card { margin-bottom: 12px; }
.action-bar { background: var(--el-bg-color); border-top: 1px solid var(--el-border-color-lighter); padding: 12px 16px; text-align: right; }
.action-bar > * { margin-left: 8px; }
</style>
