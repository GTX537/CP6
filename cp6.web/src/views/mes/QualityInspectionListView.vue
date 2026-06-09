<template>
  <div class="quality-inspection-list">
    <el-card shadow="never" class="search-card">
      <el-form :model="query" label-width="100px" size="small" inline>
        <el-form-item :label="t('検査NO')">
          <el-input v-model="query.inspectionNo" style="width: 180px;" />
        </el-form-item>
        <el-form-item :label="t('指図NO')">
          <el-input v-model="query.workOrderNo" style="width: 180px;" />
        </el-form-item>
        <el-form-item :label="t('工程CD')">
          <el-input v-model="query.processCd" style="width: 130px;" />
        </el-form-item>
        <el-form-item :label="t('検査者CD')">
          <el-input v-model="query.inspectorCd" style="width: 130px;" />
        </el-form-item>
        <el-form-item :label="t('検査種類')">
          <el-select v-model="query.inspectionType" clearable style="width: 130px;">
            <el-option v-for="o in INSPECTION_TYPE_OPTIONS" :key="o.value" :label="o.label" :value="o.value" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('総合判定')">
          <el-select v-model="query.overallResult" clearable style="width: 130px;">
            <el-option v-for="o in OVERALL_RESULT_OPTIONS" :key="o.value" :label="o.label" :value="o.value" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('検査日 From')">
          <el-date-picker v-model="query.dateFrom" type="date" value-format="YYYY-MM-DD" style="width: 150px;" />
        </el-form-item>
        <el-form-item :label="t('検査日 To')">
          <el-date-picker v-model="query.dateTo" type="date" value-format="YYYY-MM-DD" style="width: 150px;" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :loading="loading" @click="search">検索</el-button>
          <el-button @click="reset">クリア</el-button>
          <el-button type="success" @click="goNew">新規検査</el-button>
          <el-button type="warning" @click="exportCsv">CSV出力</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <div style="margin-bottom: 8px;">
        <el-tag size="small">合計 {{ total }} 件</el-tag>
      </div>
      <el-table :data="rows" border stripe size="small" style="width: 100%;" max-height="600">
        <el-table-column prop="inspectionNo" :label="t('検査NO')" width="160" fixed />
        <el-table-column prop="workOrderNo" :label="t('指図NO')" width="160" />
        <el-table-column prop="productCd" :label="t('製品CD')" width="130" />
        <el-table-column prop="productName" :label="t('製品名')" min-width="160" />
        <el-table-column prop="processCd" :label="t('工程')" width="100" />
        <el-table-column :label="t('検査種類')" width="100" align="center">
          <template #default="{ row }">{{ getTypeLabel(row.inspectionType) }}</template>
        </el-table-column>
        <el-table-column prop="inspectionDate" :label="t('検査日')" width="110">
          <template #default="{ row }">{{ formatDate(row.inspectionDate) }}</template>
        </el-table-column>
        <el-table-column prop="inspectorCd" :label="t('検査者')" width="100" />
        <el-table-column :label="t('項目数 / 合格')" width="120" align="center">
          <template #default="{ row }">{{ row.passCount }} / {{ row.itemCount }}</template>
        </el-table-column>
        <el-table-column :label="t('合格率')" width="140" align="center">
          <template #default="{ row }">
            <el-progress :percentage="row.passRate || 0" :stroke-width="10" />
          </template>
        </el-table-column>
        <el-table-column :label="t('総合判定')" width="100" align="center">
          <template #default="{ row }">
            <el-tag v-if="row.overallResult" :color="getResultColor(row.overallResult)" effect="dark" size="small">
              {{ getResultLabel(row.overallResult) }}
            </el-tag>
            <span v-else>-</span>
          </template>
        </el-table-column>
        <el-table-column :label="t('処置')" width="110" align="center">
          <template #default="{ row }">{{ getDispositionLabel(row.dispositionAction) }}</template>
        </el-table-column>
        <el-table-column :label="t('登録日時')" width="160">
          <template #default="{ row }">{{ formatDateTime(row.createDate) }}</template>
        </el-table-column>
        <el-table-column :label="t('操作')" width="100" align="center" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="goDetail(row)">詳細</el-button>
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
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
import { ref, reactive, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { qualityInspectionApi } from '@/api/mes/mes'
import {
  INSPECTION_TYPE_OPTIONS,
  OVERALL_RESULT_OPTIONS,
  DISPOSITION_OPTIONS,
  type QualityInspectionDto,
  type QualityInspectionSearchQuery,
} from '@/types/mes/mes'

const router = useRouter()
const loading = ref(false)
const rows = ref<QualityInspectionDto[]>([])
const total = ref(0)

const defaultQuery = (): QualityInspectionSearchQuery => ({
  inspectionNo: '',
  workOrderNo: '',
  processCd: '',
  inspectorCd: '',
  inspectionType: undefined,
  overallResult: undefined,
  dateFrom: undefined,
  dateTo: undefined,
  pageIndex: 1,
  pageSize: 20,
})
const query = reactive<QualityInspectionSearchQuery>(defaultQuery())

function getTypeLabel(v: number) { return INSPECTION_TYPE_OPTIONS.find(o => o.value === v)?.label ?? `${v}` }
function getResultLabel(v: number) { return OVERALL_RESULT_OPTIONS.find(o => o.value === v)?.label ?? '' }
function getResultColor(v: number) { return OVERALL_RESULT_OPTIONS.find(o => o.value === v)?.color ?? '#909399' }
function getDispositionLabel(v: number | null | undefined) {
  if (!v) return '-'
  return DISPOSITION_OPTIONS.find(o => o.value === v)?.label ?? `${v}`
}
function formatDate(v?: string | null) { return v ? v.substring(0, 10) : '' }
function formatDateTime(v?: string | null) { return v ? v.substring(0, 16).replace('T', ' ') : '' }

async function search() {
  loading.value = true
  try {
    const res = await qualityInspectionApi.search(query)
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

function goNew() { router.push('/mes/quality-inspection') }
function goDetail(row: QualityInspectionDto) {
  router.push({ path: '/mes/quality-inspection', query: { no: row.inspectionNo } })
}

function exportCsv() {
  if (rows.value.length === 0) {
    ElMessage.warning('検索結果がありません')
    return
  }
  const headers = ['検査NO', '指図NO', '製品CD', '製品名', '工程', '検査種類', '検査日', '検査者', '項目数', '合格数', '合格率(%)', '総合判定', '処置']
  const lines = rows.value.map(r => [
    r.inspectionNo, r.workOrderNo, r.productCd || '', r.productName || '', r.processCd || '',
    getTypeLabel(r.inspectionType), formatDate(r.inspectionDate), r.inspectorCd,
    r.itemCount, r.passCount, r.passRate,
    r.overallResult ? getResultLabel(r.overallResult) : '',
    getDispositionLabel(r.dispositionAction),
  ].map(v => `"${String(v).replace(/"/g, '""')}"`).join(','))
  const csv = '﻿' + [headers.join(','), ...lines].join('\r\n')
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `quality-inspection_${new Date().toISOString().slice(0, 10)}.csv`
  a.click()
  URL.revokeObjectURL(url)
}

onMounted(() => { search() })
</script>

<style scoped>
.quality-inspection-list { padding: 16px; }
.search-card { margin-bottom: 12px; }
</style>
