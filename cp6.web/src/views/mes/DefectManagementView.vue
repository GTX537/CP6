<template>
  <div class="defect-management">
    <el-card shadow="never" class="search-card">
      <el-form :model="query" label-width="100px" size="small" inline>
        <el-form-item :label="t('不良NO')">
          <el-input v-model="query.defectNo" style="width: 170px;" />
        </el-form-item>
        <el-form-item :label="t('指図NO')">
          <el-input v-model="query.workOrderNo" style="width: 170px;" />
        </el-form-item>
        <el-form-item :label="t('大分類')">
          <el-select v-model="query.categoryCd" clearable style="width: 150px;" @change="onCategoryChange">
            <el-option v-for="c in categories" :key="c" :label="`${c} - ${categoryNameMap[c]}`" :value="c" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('小分類')">
          <el-select v-model="query.detailCd" clearable style="width: 150px;">
            <el-option v-for="d in detailsByCategory" :key="d.detailCd" :label="`${d.detailCd} - ${d.detailName}`" :value="d.detailCd" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('担当者CD')">
          <el-input v-model="query.assigneeCd" style="width: 130px;" />
        </el-form-item>
        <el-form-item :label="t('発生日 From')">
          <el-date-picker v-model="query.occurDateFrom" type="date" value-format="YYYY-MM-DD" style="width: 150px;" />
        </el-form-item>
        <el-form-item :label="t('発生日 To')">
          <el-date-picker v-model="query.occurDateTo" type="date" value-format="YYYY-MM-DD" style="width: 150px;" />
        </el-form-item>
        <el-form-item :label="t('ステータス')">
          <el-checkbox-group v-model="query.statuses">
            <el-checkbox v-for="s in DEFECT_STATUS_OPTIONS" :key="s.value" :value="s.value">{{ s.label }}</el-checkbox>
          </el-checkbox-group>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :loading="loading" @click="search">{{ t('検索') }}</el-button>
          <el-button @click="reset">{{ t('クリア') }}</el-button>
          <el-button type="success" @click="openCreate">{{ t('手動起票') }}</el-button>
          <el-button type="warning" @click="exportCsv">{{ t('CSV出力') }}</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <div style="margin-bottom: 8px;">
        <el-tag size="small">{{ t('合計 {total} 件', { total }) }}</el-tag>
      </div>
      <el-table :data="rows" border stripe size="small" style="width: 100%;" max-height="600">
        <el-table-column prop="defectNo" :label="t('不良NO')" width="160" fixed />
        <el-table-column :label="t('ステータス')" width="100" align="center">
          <template #default="{ row }">
            <el-tag :color="getStatusColor(row.status)" effect="dark" size="small">{{ getStatusLabel(row.status) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="workOrderNo" :label="t('指図NO')" width="160" />
        <el-table-column prop="productName" :label="t('製品名')" min-width="160" />
        <el-table-column prop="processCd" :label="t('工程')" width="80" />
        <el-table-column :label="t('大分類')" width="120">
          <template #default="{ row }">{{ row.categoryCd }} {{ row.categoryName }}</template>
        </el-table-column>
        <el-table-column :label="t('小分類')" width="160">
          <template #default="{ row }">{{ row.detailCd }} {{ row.detailName }}</template>
        </el-table-column>
        <el-table-column prop="defectQty" :label="t('不良数')" width="100" align="right" />
        <el-table-column prop="occurDate" :label="t('発生日')" width="110">
          <template #default="{ row }">{{ formatDate(row.occurDate) }}</template>
        </el-table-column>
        <el-table-column prop="reporterCd" :label="t('発見者')" width="100" />
        <el-table-column prop="assigneeCd" :label="t('担当者')" width="100" />
        <el-table-column :label="t('処置期限')" width="110">
          <template #default="{ row }">{{ formatDate(row.dueDate) }}</template>
        </el-table-column>
        <el-table-column :label="t('完了日')" width="110">
          <template #default="{ row }">{{ formatDate(row.completedDate) }}</template>
        </el-table-column>
        <el-table-column prop="inspectionNo" :label="t('検査NO')" width="160" />
        <el-table-column prop="defectDescription" :label="t('不良内容')" min-width="220" />
        <el-table-column :label="t('操作')" width="160" align="center" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="openEdit(row)">{{ t('編集') }}</el-button>
            <el-button link type="danger" size="small" @click="onDelete(row)">{{ t('削除') }}</el-button>
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

    <!-- 編集ダイアログ -->
    <el-dialog v-model="dialogVisible" :title="form.defectNo ? t('不良品 編集') : t('不良品 手動起票')" width="800px" @closed="resetForm">
      <el-form :model="form" label-width="110px" size="small">
        <el-row :gutter="16">
          <el-col :span="12">
            <el-form-item :label="t('指図NO')" required>
              <el-input v-model="form.workOrderNo" :disabled="!!form.defectNo" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item :label="t('工程CD')">
              <el-input v-model="form.processCd" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item :label="t('発生日')">
              <el-date-picker v-model="form.occurDate" type="date" value-format="YYYY-MM-DD" style="width: 100%;" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item :label="t('発見者CD')">
              <el-input v-model="form.reporterCd" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item :label="t('不良大分類')" required>
              <el-select v-model="form.categoryCd" filterable @change="onFormCategoryChange">
                <el-option v-for="c in categories" :key="c" :label="`${c} - ${categoryNameMap[c]}`" :value="c" />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item :label="t('不良小分類')">
              <el-select v-model="form.detailCd" clearable filterable>
                <el-option v-for="d in formDetailsByCategory" :key="d.detailCd"
                  :label="`${d.detailCd} - ${d.detailName}`" :value="d.detailCd" />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item :label="t('不良数')" required>
              <el-input-number v-model="form.defectQty" :min="0" :precision="2" style="width: 100%;" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item :label="t('ステータス')">
              <el-select v-model="form.status">
                <el-option v-for="o in DEFECT_STATUS_OPTIONS" :key="o.value" :label="o.label" :value="o.value" />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="24">
            <el-form-item :label="t('不良内容')" required>
              <el-input v-model="form.defectDescription" type="textarea" :rows="2" />
            </el-form-item>
          </el-col>
          <el-col :span="24">
            <el-form-item :label="t('原因分析(5Why)')">
              <el-input v-model="form.causeAnalysis" type="textarea" :rows="3" />
            </el-form-item>
          </el-col>
          <el-col :span="24">
            <el-form-item :label="t('是正処置')">
              <el-input v-model="form.correctiveAction" type="textarea" :rows="3" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item :label="t('担当者CD')">
              <el-input v-model="form.assigneeCd" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item :label="t('処置期限')">
              <el-date-picker v-model="form.dueDate" type="date" value-format="YYYY-MM-DD" style="width: 100%;" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item :label="t('完了日')">
              <el-date-picker v-model="form.completedDate" type="date" value-format="YYYY-MM-DD" style="width: 100%;" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item :label="t('検査NO')">
              <el-input v-model="form.inspectionNo" :disabled="!!form.defectNo" />
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
        <el-button type="primary" :loading="saving" @click="onSave">{{ t('保存') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
import { ref, reactive, computed, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { defectRecordApi } from '@/api/mes/mes'
import {
  DEFECT_STATUS_OPTIONS,
  type DefectRecordDto,
  type DefectRecordSearchQuery,
  type DefectCategoryDto,
} from '@/types/mes/mes'

const loading = ref(false)
const saving = ref(false)
const rows = ref<DefectRecordDto[]>([])
const total = ref(0)

// 不良分類マスタ
const allCategories = ref<DefectCategoryDto[]>([])
const categories = computed(() => Array.from(new Set(allCategories.value.map(c => c.categoryCd))))
const categoryNameMap = computed(() => {
  const map: Record<string, string> = {}
  allCategories.value.forEach(c => {
    if (c.categoryName) map[c.categoryCd] = c.categoryName
  })
  return map
})
const detailsByCategory = computed(() =>
  allCategories.value.filter(c => c.categoryCd === query.categoryCd)
)
const formDetailsByCategory = computed(() =>
  allCategories.value.filter(c => c.categoryCd === form.categoryCd)
)

const defaultQuery = (): DefectRecordSearchQuery => ({
  defectNo: '',
  workOrderNo: '',
  processCd: '',
  categoryCd: '',
  detailCd: '',
  statuses: [],
  assigneeCd: '',
  occurDateFrom: undefined,
  occurDateTo: undefined,
  pageIndex: 1,
  pageSize: 20,
})
const query = reactive<DefectRecordSearchQuery>(defaultQuery())

const dialogVisible = ref(false)

const emptyForm = (): DefectRecordDto => ({
  defectNo: '',
  workOrderNo: '',
  productCd: '',
  productName: '',
  processCd: '',
  inspectionNo: '',
  occurDate: new Date().toISOString().slice(0, 10),
  reporterCd: '',
  categoryCd: '',
  detailCd: '',
  defectQty: 0,
  defectDescription: '',
  causeAnalysis: '',
  correctiveAction: '',
  assigneeCd: '',
  dueDate: null,
  completedDate: null,
  status: 0,
  remarks: '',
})

const form = reactive<DefectRecordDto>(emptyForm())

function getStatusLabel(v: number) { return DEFECT_STATUS_OPTIONS.find(o => o.value === v)?.label ?? `${v}` }
function getStatusColor(v: number) { return DEFECT_STATUS_OPTIONS.find(o => o.value === v)?.color ?? '#909399' }
function formatDate(v?: string | null) { return v ? v.substring(0, 10) : '' }

function onCategoryChange() { query.detailCd = '' }
function onFormCategoryChange() { form.detailCd = '' }

async function search() {
  loading.value = true
  try {
    const res = await defectRecordApi.search(query)
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

function openCreate() {
  Object.assign(form, emptyForm())
  dialogVisible.value = true
}

async function openEdit(row: DefectRecordDto) {
  const res = await defectRecordApi.get(row.defectNo)
  if (res.code === 0 && res.data) {
    Object.assign(form, emptyForm(), res.data)
    dialogVisible.value = true
  }
}

function resetForm() {
  Object.assign(form, emptyForm())
}

async function onSave() {
  if (!form.workOrderNo) { ElMessage.error(t('指図NOを入力してください')); return }
  if (!form.categoryCd) { ElMessage.error(t('ME-MSG-030: 不良分類が未選択です')); return }
  if (!form.defectDescription) { ElMessage.error(t('ME-MSG-031: 不良内容が未入力です')); return }

  saving.value = true
  try {
    if (form.defectNo) {
      await defectRecordApi.update(form.defectNo, form)
      ElMessage.success(t('ME-MSG-041: 更新しました'))
    } else {
      const res = await defectRecordApi.create(form)
      ElMessage.success(t('ME-MSG-041: 起票しました（{defectNo}）', { defectNo: res.data?.defectNo }))
    }
    dialogVisible.value = false
    search()
  } finally {
    saving.value = false
  }
}

async function onDelete(row: DefectRecordDto) {
  try {
    await ElMessageBox.confirm(t('不良 {defectNo} を削除しますか？', { defectNo: row.defectNo }), t('確認'), { type: 'warning' })
    await defectRecordApi.delete(row.defectNo)
    ElMessage.success(t('削除しました'))
    search()
  } catch (e) { /* cancelled */ }
}

function exportCsv() {
  if (rows.value.length === 0) { ElMessage.warning(t('検索結果がありません')); return }
  const headers = [t('不良NO'), t('ステータス'), t('指図NO'), t('製品名'), t('工程'), t('大分類'), t('小分類'), t('不良数'), t('発生日'), t('発見者'), t('担当者'), t('処置期限'), t('完了日'), t('検査NO'), t('不良内容'), t('原因分析'), t('是正処置')]
  const lines = rows.value.map(r => [
    r.defectNo, getStatusLabel(r.status), r.workOrderNo, r.productName || '', r.processCd || '',
    `${r.categoryCd} ${r.categoryName || ''}`, `${r.detailCd || ''} ${r.detailName || ''}`.trim(),
    r.defectQty, formatDate(r.occurDate), r.reporterCd || '', r.assigneeCd || '',
    formatDate(r.dueDate), formatDate(r.completedDate), r.inspectionNo || '',
    r.defectDescription, r.causeAnalysis || '', r.correctiveAction || '',
  ].map(v => `"${String(v).replace(/"/g, '""')}"`).join(','))
  const csv = '﻿' + [headers.join(','), ...lines].join('\r\n')
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `defect_${new Date().toISOString().slice(0, 10)}.csv`
  a.click()
  URL.revokeObjectURL(url)
}

onMounted(async () => {
  const res = await defectRecordApi.getCategories()
  allCategories.value = res.data || []
  search()
})
</script>

<style scoped>
.defect-management { padding: 16px; }
.search-card { margin-bottom: 12px; }
</style>
