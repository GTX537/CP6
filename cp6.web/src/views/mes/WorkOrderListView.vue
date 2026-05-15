<template>
  <div class="work-order-list">
    <!-- 検索条件 -->
    <el-card shadow="never" class="search-card">
      <el-form :model="query" label-width="100px" size="small" inline>
        <el-form-item label="拠点CD">
          <el-input v-model="query.baseCd" style="width: 120px" placeholder="拠点CD" />
        </el-form-item>
        <el-form-item label="指図NO">
          <el-input v-model="query.workOrderNo" style="width: 180px" placeholder="WOyyyyMMdd-NNNN" />
        </el-form-item>
        <el-form-item label="手配NO">
          <el-input v-model="query.orderNo" style="width: 180px" placeholder="手配NO/受注Web NO" />
        </el-form-item>
        <el-form-item label="製品CD">
          <el-input v-model="query.productCd" style="width: 150px" />
        </el-form-item>
        <el-form-item label="得意先CD">
          <el-input v-model="query.customerCd" style="width: 130px" />
        </el-form-item>
        <el-form-item label="納期 From">
          <el-date-picker v-model="query.deliveryDateFrom" type="date" value-format="YYYY-MM-DD" style="width: 150px" />
        </el-form-item>
        <el-form-item label="納期 To">
          <el-date-picker v-model="query.deliveryDateTo" type="date" value-format="YYYY-MM-DD" style="width: 150px" />
        </el-form-item>
        <el-form-item label="計画開始 From">
          <el-date-picker v-model="query.planStartDateFrom" type="date" value-format="YYYY-MM-DD" style="width: 150px" />
        </el-form-item>
        <el-form-item label="計画開始 To">
          <el-date-picker v-model="query.planStartDateTo" type="date" value-format="YYYY-MM-DD" style="width: 150px" />
        </el-form-item>
        <el-form-item label="優先度">
          <el-select v-model="query.priority" clearable style="width: 100px">
            <el-option v-for="o in PRIORITY_OPTIONS" :key="o.value" :label="o.label" :value="o.value" />
          </el-select>
        </el-form-item>
        <el-form-item label="工程CD">
          <el-input v-model="query.processCd" style="width: 120px" />
        </el-form-item>
        <el-form-item label="WG">
          <el-input v-model="query.wgCd" style="width: 100px" />
        </el-form-item>
        <el-form-item label="ステータス">
          <el-checkbox-group v-model="query.statuses">
            <el-checkbox v-for="s in WORK_ORDER_STATUS_OPTIONS" :key="s.value" :value="s.value">{{ s.label }}</el-checkbox>
          </el-checkbox-group>
        </el-form-item>
        <el-form-item>
          <el-checkbox v-model="query.delayedOnly">遅延のみ</el-checkbox>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :loading="loading" @click="search">検索</el-button>
          <el-button @click="resetQuery">クリア</el-button>
          <el-button type="success" @click="onCreate">新規作成</el-button>
          <el-button type="warning" @click="exportCsv">CSV出力</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <!-- 結果 -->
    <el-card shadow="never">
      <div style="margin-bottom: 8px;">
        <el-tag size="small">合計 {{ total }} 件</el-tag>
      </div>
      <el-table :data="rows" border stripe size="small" style="width: 100%" max-height="600">
        <el-table-column prop="workOrderNo" label="指図NO" width="160" fixed />
        <el-table-column label="ステータス" width="100" align="center">
          <template #default="{ row }">
            <el-tag :color="getStatusColor(row.status)" effect="dark" size="small">
              {{ getStatusLabel(row.status) }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="優先度" width="80" align="center">
          <template #default="{ row }">
            <el-tag v-if="row.priority === 3" type="danger" size="small">特急</el-tag>
            <el-tag v-else-if="row.priority === 2" type="warning" size="small">急ぎ</el-tag>
            <span v-else>通常</span>
          </template>
        </el-table-column>
        <el-table-column prop="orderNo1" label="手配NO" width="140" />
        <el-table-column prop="productCd" label="製品CD" width="130" />
        <el-table-column prop="productName" label="製品名" min-width="180" />
        <el-table-column prop="customerCd" label="得意先" width="100" />
        <el-table-column prop="productionQty" label="生産数量" width="100" align="right" />
        <el-table-column prop="completedQty" label="完了数量" width="100" align="right" />
        <el-table-column label="進捗率" width="120" align="center">
          <template #default="{ row }">
            <el-progress :percentage="row.progressRate || 0" :stroke-width="10" />
          </template>
        </el-table-column>
        <el-table-column prop="deliveryDate" label="客先納期" width="100">
          <template #default="{ row }">{{ formatDate(row.deliveryDate) }}</template>
        </el-table-column>
        <el-table-column prop="planStartDate" label="計画開始" width="100">
          <template #default="{ row }">{{ formatDate(row.planStartDate) }}</template>
        </el-table-column>
        <el-table-column prop="planEndDate" label="計画完了" width="100">
          <template #default="{ row }">{{ formatDate(row.planEndDate) }}</template>
        </el-table-column>
        <el-table-column prop="actualStartDate" label="実績開始" width="100">
          <template #default="{ row }">{{ formatDate(row.actualStartDate) }}</template>
        </el-table-column>
        <el-table-column prop="actualEndDate" label="実績完了" width="100">
          <template #default="{ row }">{{ formatDate(row.actualEndDate) }}</template>
        </el-table-column>
        <el-table-column label="遅延" width="80" align="center">
          <template #default="{ row }">
            <el-tag v-if="row.delayDays > 0" type="danger" size="small">{{ row.delayDays }}日</el-tag>
            <span v-else>-</span>
          </template>
        </el-table-column>
        <el-table-column label="工程" width="100" align="center">
          <template #default="{ row }">
            {{ row.completedProcessCount }} / {{ row.processCount }}
          </template>
        </el-table-column>
        <el-table-column prop="defectQty" label="不良数" width="80" align="right" />
        <el-table-column prop="lotNo" label="ロットNO" width="120" />
        <el-table-column prop="baseCd" label="拠点" width="80" />
        <el-table-column label="操作" width="180" align="center" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="goDetail(row)">詳細</el-button>
            <el-button link type="success" size="small" @click="goResult(row)">実績</el-button>
            <el-button v-if="row.status <= 1" link type="danger" size="small" @click="onDelete(row)">削除</el-button>
          </template>
        </el-table-column>
      </el-table>

      <div style="margin-top: 12px; text-align: right;">
        <el-pagination
          v-model:current-page="query.pageIndex"
          v-model:page-size="query.pageSize"
          :total="total"
          :page-sizes="[10, 20, 50, 100]"
          layout="total, sizes, prev, pager, next, jumper"
          background
          @current-change="search"
          @size-change="search"
        />
      </div>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { workOrderApi } from '@/api/mes'
import {
  WORK_ORDER_STATUS_OPTIONS,
  PRIORITY_OPTIONS,
  type WorkOrderDto,
  type WorkOrderSearchQuery,
} from '@/types/mes'

const router = useRouter()
const loading = ref(false)
const total = ref(0)
const rows = ref<WorkOrderDto[]>([])

const defaultQuery = (): WorkOrderSearchQuery => ({
  baseCd: '',
  workOrderNo: '',
  orderNo: '',
  productCd: '',
  customerCd: '',
  deliveryDateFrom: undefined,
  deliveryDateTo: undefined,
  planStartDateFrom: undefined,
  planStartDateTo: undefined,
  statuses: [],
  priority: undefined,
  processCd: '',
  wgCd: '',
  delayedOnly: false,
  pageIndex: 1,
  pageSize: 20,
})

const query = reactive<WorkOrderSearchQuery>(defaultQuery())

function getStatusLabel(v: number) {
  return WORK_ORDER_STATUS_OPTIONS.find(s => s.value === v)?.label ?? `${v}`
}
function getStatusColor(v: number) {
  return WORK_ORDER_STATUS_OPTIONS.find(s => s.value === v)?.color ?? '#909399'
}
function formatDate(v?: string | null) {
  if (!v) return ''
  return v.substring(0, 10)
}

async function search() {
  loading.value = true
  try {
    const res = await workOrderApi.search(query)
    rows.value = res.data?.items || []
    total.value = res.data?.total || 0
  } finally {
    loading.value = false
  }
}

function resetQuery() {
  Object.assign(query, defaultQuery())
  search()
}

function onCreate() {
  router.push('/mes/work-order')
}

function goDetail(row: WorkOrderDto) {
  router.push({ path: '/mes/work-order', query: { no: row.workOrderNo } })
}

function goResult(row: WorkOrderDto) {
  router.push({ path: '/mes/production-result', query: { no: row.workOrderNo } })
}

async function onDelete(row: WorkOrderDto) {
  try {
    await ElMessageBox.confirm(`指図 ${row.workOrderNo} を削除しますか？`, '確認', {
      type: 'warning',
    })
    await workOrderApi.delete(row.workOrderNo)
    ElMessage.success('削除しました')
    search()
  } catch (e: any) {
    if (e === 'cancel') return
  }
}

function exportCsv() {
  if (rows.value.length === 0) {
    ElMessage.warning('検索結果がありません')
    return
  }
  const headers = ['指図NO', 'ステータス', '優先度', '手配NO', '製品CD', '製品名', '得意先', '生産数量', '完了数量', '進捗率', '客先納期', '計画開始', '計画完了', '実績開始', '実績完了', '遅延日数', '工程数', '完了工程数', '不良数', 'ロットNO', '拠点']
  const lines = rows.value.map(r => [
    r.workOrderNo, getStatusLabel(r.status), r.priority, r.orderNo1 || '', r.productCd, r.productName || '',
    r.customerCd || '', r.productionQty, r.completedQty, r.progressRate,
    formatDate(r.deliveryDate), formatDate(r.planStartDate), formatDate(r.planEndDate),
    formatDate(r.actualStartDate), formatDate(r.actualEndDate),
    r.delayDays, r.processCount, r.completedProcessCount, r.defectQty,
    r.lotNo || '', r.baseCd || '',
  ].map(v => `"${String(v).replace(/"/g, '""')}"`).join(','))
  const csv = '﻿' + [headers.join(','), ...lines].join('\r\n')
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `work-order_${new Date().toISOString().slice(0, 10)}.csv`
  a.click()
  URL.revokeObjectURL(url)
}

onMounted(() => {
  search()
})
</script>

<style scoped>
.work-order-list {
  padding: 16px;
}
.search-card {
  margin-bottom: 12px;
}
</style>
