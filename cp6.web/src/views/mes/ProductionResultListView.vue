<template>
  <div class="production-result-list">
    <el-card shadow="never" class="search-card">
      <el-form :model="query" label-width="100px" size="small" inline>
        <el-form-item :label="t('指図NO')">
          <el-input v-model="query.workOrderNo" style="width: 200px;" />
        </el-form-item>
        <el-form-item :label="t('工程CD')">
          <el-input v-model="query.processCd" style="width: 150px;" />
        </el-form-item>
        <el-form-item :label="t('作業者CD')">
          <el-input v-model="query.operatorCd" style="width: 150px;" />
        </el-form-item>
        <el-form-item :label="t('実績種別')">
          <el-select v-model="query.resultType" clearable style="width: 130px;">
            <el-option v-for="o in RESULT_TYPE_OPTIONS" :key="o.value" :label="o.label" :value="o.value" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('日付 From')">
          <el-date-picker v-model="query.dateFrom" type="datetime" value-format="YYYY-MM-DDTHH:mm:ss" style="width: 200px;" />
        </el-form-item>
        <el-form-item :label="t('日付 To')">
          <el-date-picker v-model="query.dateTo" type="datetime" value-format="YYYY-MM-DDTHH:mm:ss" style="width: 200px;" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :loading="loading" @click="search">検索</el-button>
          <el-button @click="reset">クリア</el-button>
          <el-button type="warning" @click="exportCsv">CSV出力</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <div style="margin-bottom: 8px;">
        <el-tag size="small">合計 {{ total }} 件</el-tag>
      </div>
      <el-table :data="rows" border stripe size="small" style="width: 100%;" max-height="600">
        <el-table-column prop="resultNo" :label="t('実績NO')" width="160" fixed />
        <el-table-column prop="workOrderNo" :label="t('指図NO')" width="160" />
        <el-table-column prop="productName" :label="t('製品名')" min-width="160" />
        <el-table-column prop="processCd" :label="t('工程CD')" width="110" />
        <el-table-column prop="processName" :label="t('工程名')" width="120" />
        <el-table-column :label="t('実績種別')" width="100" align="center">
          <template #default="{ row }">
            <el-tag :type="getResultTypeTag(row.resultType)" size="small">
              {{ getResultTypeLabel(row.resultType) }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="operatorCd" :label="t('作業者CD')" width="120" />
        <el-table-column prop="operatorName" :label="t('作業者名')" width="120" />
        <el-table-column prop="machineCd" :label="t('号機')" width="110" />
        <el-table-column :label="t('実績開始')" width="160">
          <template #default="{ row }">{{ formatDateTime(row.actualStartTime) }}</template>
        </el-table-column>
        <el-table-column :label="t('実績完了')" width="160">
          <template #default="{ row }">{{ formatDateTime(row.actualEndTime) }}</template>
        </el-table-column>
        <el-table-column prop="goodQty" :label="t('良品数')" width="100" align="right" />
        <el-table-column prop="defectQty" :label="t('不良数')" width="100" align="right" />
        <el-table-column prop="actualLossRate" :label="t('ロス率(%)')" width="100" align="right">
          <template #default="{ row }">{{ row.actualLossRate ?? '-' }}</template>
        </el-table-column>
        <el-table-column prop="defectReasonCd" :label="t('不良理由')" width="100" />
        <el-table-column prop="suspendReasonCd" :label="t('中断理由')" width="100" />
        <el-table-column prop="resultNote" :label="t('備考')" min-width="160" />
        <el-table-column :label="t('登録日時')" width="160">
          <template #default="{ row }">{{ formatDateTime(row.createDate) }}</template>
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
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
import { ref, reactive, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import { productionResultApi } from '@/api/mes'
import { RESULT_TYPE_OPTIONS, type ProductionResultDto, type ProductionResultSearchQuery } from '@/types/mes'

const loading = ref(false)
const rows = ref<ProductionResultDto[]>([])
const total = ref(0)

const defaultQuery = (): ProductionResultSearchQuery => ({
  workOrderNo: '',
  processCd: '',
  operatorCd: '',
  resultType: undefined,
  dateFrom: undefined,
  dateTo: undefined,
  pageIndex: 1,
  pageSize: 20,
})
const query = reactive<ProductionResultSearchQuery>(defaultQuery())

function getResultTypeLabel(v: number) {
  return RESULT_TYPE_OPTIONS.find(o => o.value === v)?.label ?? `${v}`
}
function getResultTypeTag(v: number) {
  const map: Record<number, string> = { 1: 'primary', 2: 'warning', 3: 'success', 4: 'success', 5: 'info' }
  return map[v] || 'info'
}
function formatDateTime(v?: string | null) {
  return v ? v.substring(0, 16).replace('T', ' ') : ''
}

async function search() {
  loading.value = true
  try {
    const res = await productionResultApi.search(query)
    rows.value = res.data?.items || []
    total.value = res.data?.total || 0
  } finally {
    loading.value = false
  }
}

function reset() {
  Object.assign(query, defaultQuery())
  search()
}

function exportCsv() {
  if (rows.value.length === 0) {
    ElMessage.warning('検索結果がありません')
    return
  }
  const headers = ['実績NO', '指図NO', '製品名', '工程CD', '工程名', '実績種別', '作業者CD', '作業者名', '号機', '実績開始', '実績完了', '良品数', '不良数', 'ロス率(%)', '不良理由', '中断理由', '備考']
  const lines = rows.value.map(r => [
    r.resultNo, r.workOrderNo, r.productName || '', r.processCd, r.processName || '',
    getResultTypeLabel(r.resultType), r.operatorCd, r.operatorName || '', r.machineCd || '',
    formatDateTime(r.actualStartTime), formatDateTime(r.actualEndTime),
    r.goodQty, r.defectQty, r.actualLossRate ?? '',
    r.defectReasonCd || '', r.suspendReasonCd || '', r.resultNote || '',
  ].map(v => `"${String(v).replace(/"/g, '""')}"`).join(','))
  const csv = '﻿' + [headers.join(','), ...lines].join('\r\n')
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `production-result_${new Date().toISOString().slice(0, 10)}.csv`
  a.click()
  URL.revokeObjectURL(url)
}

onMounted(() => {
  search()
})
</script>

<style scoped>
.production-result-list {
  padding: 16px;
}
.search-card {
  margin-bottom: 12px;
}
</style>
