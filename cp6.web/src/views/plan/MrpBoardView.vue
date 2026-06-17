<template>
  <div class="plan-mrp-board">
    <div class="page-header">
      <h2>{{ t('MRP运算看板') }}</h2>
      <span class="subtitle">{{ t('净需求') }} · {{ t('计划订单') }} · {{ t('转单') }}</span>
    </div>

    <el-card shadow="never">
      <div class="table-toolbar">
        <el-button type="primary" size="small" :loading="running" @click="runMrp">{{ t('按开口受注运算') }}</el-button>
        <span class="lbl">{{ t('运算批次') }}：</span>
        <el-select v-model="currentRunId" size="small" style="width: 220px" @change="loadRun" :placeholder="t('无运算结果')">
          <el-option v-for="r in runs" :key="r.id" :label="r.runNo" :value="r.id" />
        </el-select>
        <el-button size="small" :disabled="!currentRunId" @click="loadRun">{{ t('查询') }}</el-button>
      </div>
    </el-card>

    <el-card shadow="never" style="margin-top: 12px">
      <div class="lines-head"><span>{{ t('计划订单') }}</span><el-tag size="small" type="info">{{ t('共 {n} 条', { n: orders.length }) }}</el-tag></div>
      <el-table :data="orders" border stripe size="small" max-height="360" v-loading="loading">
        <el-table-column prop="itemCd" :label="t('物料')" width="160" show-overflow-tooltip />
        <el-table-column :label="t('类型')" width="90" align="center">
          <template #default="{ row }">
            <el-tag size="small" :type="row.type === 2 ? 'success' : 'warning'">{{ t(typeLabel(row.type)) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="qty" :label="t('数量')" width="120" align="right" />
        <el-table-column :label="t('需求日')" width="120"><template #default="{ row }">{{ (row.requiredDate || '').slice(0, 10) }}</template></el-table-column>
        <el-table-column :label="t('下达日')" width="120"><template #default="{ row }">{{ (row.releaseDate || '').slice(0, 10) }}</template></el-table-column>
        <el-table-column :label="t('状态')" width="100" align="center">
          <template #default="{ row }">
            <el-tag size="small" :type="statusTagType(row.status)">{{ t(statusLabel(row.status)) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column :label="t('下游单号')" width="130" show-overflow-tooltip>
          <template #default="{ row }">{{ row.convertedDocNo || '—' }}</template>
        </el-table-column>
        <el-table-column :label="t('操作')" width="200" fixed="right">
          <template #default="{ row }">
            <el-button v-if="row.status === 0" link type="primary" size="small" @click="confirm(row)">{{ t('确认') }}</el-button>
            <el-button v-if="row.status === 1" link type="success" size="small" @click="convert(row)">{{ t('转单') }}</el-button>
            <el-button v-if="row.status === 0 || row.status === 1" link type="danger" size="small" @click="ignore(row)">{{ t('忽略') }}</el-button>
          </template>
        </el-table-column>
        <template #empty><span>{{ t('无运算结果') }}</span></template>
      </el-table>
    </el-card>

    <el-card shadow="never" style="margin-top: 12px">
      <div class="lines-head"><span>{{ t('净需求') }}</span></div>
      <el-table :data="netReqs" border stripe size="small" max-height="320">
        <el-table-column prop="itemCd" :label="t('物料')" width="160" show-overflow-tooltip />
        <el-table-column prop="gross" :label="t('毛需求')" width="110" align="right" />
        <el-table-column prop="onHand" :label="t('库存')" width="100" align="right" />
        <el-table-column prop="inTransit" :label="t('在途')" width="100" align="right" />
        <el-table-column prop="inWip" :label="t('在制')" width="100" align="right" />
        <el-table-column prop="firmPlanned" :label="t('已确认供给')" width="120" align="right" />
        <el-table-column prop="safetyStock" :label="t('安全库存')" width="110" align="right" />
        <el-table-column prop="net" :label="t('净需求')" width="110" align="right">
          <template #default="{ row }"><strong>{{ row.net }}</strong></template>
        </el-table-column>
        <template #empty><span>{{ t('无运算结果') }}</span></template>
      </el-table>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { mrpApi } from '@/api/plan/plan'
import type { MrpRun, PlannedOrder, NetRequirement } from '@/types/plan/plan'

const { t } = useI18n()
const runs = ref<MrpRun[]>([])
const currentRunId = ref<string>('')
const orders = ref<PlannedOrder[]>([])
const netReqs = ref<NetRequirement[]>([])
const running = ref(false)
const loading = ref(false)

function typeLabel(v: number) { return v === 2 ? '生产' : '采购' }
function statusLabel(v: number) { return v === 1 ? '已确认' : v === 2 ? '已转单' : v === 9 ? '已忽略' : '建议' }
function statusTagType(v: number) { return v === 1 ? 'success' : v === 2 ? 'primary' : v === 9 ? 'info' : 'warning' }

async function loadRuns() {
  const res = await mrpApi.runs()
  runs.value = res?.data || []
  const first = runs.value[0]
  if (!currentRunId.value && first) {
    currentRunId.value = first.id
    await loadRun()
  }
}

async function loadRun() {
  if (!currentRunId.value) { orders.value = []; netReqs.value = []; return }
  loading.value = true
  try {
    const [po, nr] = await Promise.all([
      mrpApi.plannedOrders(currentRunId.value),
      mrpApi.netRequirements(currentRunId.value),
    ])
    orders.value = po?.data || []
    netReqs.value = nr?.data || []
  } finally {
    loading.value = false
  }
}

async function runMrp() {
  running.value = true
  try {
    const res = await mrpApi.run({ fromOpenOrders: true })
    ElMessage.success(res?.data?.runNo || t('运行MRP'))
    await loadRuns()
    currentRunId.value = res?.data?.runId || currentRunId.value
    await loadRun()
  } catch (e: any) {
    ElMessage.error(e?.response?.data?.message || t('无运算结果'))
  } finally {
    running.value = false
  }
}

async function confirm(row: PlannedOrder) {
  await mrpApi.confirm(row.id)
  ElMessage.success(t('确认'))
  await loadRun()
}

async function convert(row: PlannedOrder) {
  const res = await mrpApi.convert(row.id)
  ElMessage.success(res?.data?.docNo || t('转单'))
  await loadRun()
}

async function ignore(row: PlannedOrder) {
  await ElMessageBox.confirm(t('忽略') + ' ?', t('提示'), { type: 'warning' })
  await mrpApi.ignore(row.id)
  ElMessage.success(t('忽略'))
  await loadRun()
}

loadRuns()
</script>

<style scoped>
.plan-mrp-board { padding: 16px; }
.page-header { margin-bottom: 12px; }
.page-header h2 { margin: 0; color: #303133; font-size: 20px; font-weight: 650; }
.subtitle { color: #909399; font-size: 12px; }
.table-toolbar { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
.lbl { color: #606266; font-size: 13px; }
.lines-head { display: flex; justify-content: space-between; align-items: center; margin: 4px 0 8px; font-weight: 600; }
</style>
