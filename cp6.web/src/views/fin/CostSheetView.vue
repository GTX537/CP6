<template>
  <div class="cost-sheet">
    <div class="page-header">
      <h2>{{ t('成本核算') }}</h2>
      <span class="subtitle">{{ t('料吃MES真实消耗×BOM单价，工费标准估算；完工结转 WIP→FG') }}</span>
    </div>

    <el-card shadow="never">
      <div class="table-toolbar">
        <el-select v-model="status" size="small" style="width: 140px" clearable :placeholder="t('全部状态')" @change="load">
          <el-option v-for="(lbl, v) in COST_SHEET_STATUS_LABEL" :key="v" :value="Number(v)" :label="t(lbl)" />
        </el-select>
        <el-button size="small" @click="load">{{ t('刷新') }}</el-button>
        <el-button size="small" type="primary" @click="openCollect">{{ t('归集成本') }}</el-button>
      </div>

      <el-table :data="rows" border stripe size="small" max-height="600" v-loading="loading">
        <el-table-column prop="no" :label="t('成本单号')" width="140" />
        <el-table-column prop="workOrderNo" :label="t('工单号')" width="140" />
        <el-table-column prop="productCd" :label="t('制品')" width="100" show-overflow-tooltip />
        <el-table-column :label="t('完工数')" width="90" align="right">
          <template #default="{ row }">{{ fmtQty(row.completedQty) }}</template>
        </el-table-column>
        <el-table-column :label="t('实际料')" width="120" align="right">
          <template #default="{ row }">{{ fmtAmt(row.materialActual) }}</template>
        </el-table-column>
        <el-table-column :label="t('标准料')" width="120" align="right">
          <template #default="{ row }">{{ fmtAmt(row.materialStandard) }}</template>
        </el-table-column>
        <el-table-column :label="t('直接人工')" width="110" align="right">
          <template #default="{ row }">{{ fmtAmt(row.laborStd) }}</template>
        </el-table-column>
        <el-table-column :label="t('制造费用')" width="110" align="right">
          <template #default="{ row }">{{ fmtAmt(row.overheadStd) }}</template>
        </el-table-column>
        <el-table-column :label="t('实际总成本')" width="130" align="right">
          <template #default="{ row }"><b>{{ fmtAmt(row.totalActual) }}</b></template>
        </el-table-column>
        <el-table-column :label="t('差异')" width="110" align="right">
          <template #default="{ row }">
            <span :class="row.variance > 0 ? 'over' : (row.variance < 0 ? 'under' : '')">{{ fmtAmt(row.variance) }}</span>
          </template>
        </el-table-column>
        <el-table-column :label="t('FG单位成本')" width="120" align="right">
          <template #default="{ row }">{{ fmtAmt(row.fgUnitCost) }}</template>
        </el-table-column>
        <el-table-column :label="t('状态')" width="90" align="center">
          <template #default="{ row }">
            <el-tag :type="COST_SHEET_STATUS_TAG[row.status] || ''" size="small">{{ t(COST_SHEET_STATUS_LABEL[row.status] || '') }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column :label="t('操作')" width="100" fixed="right">
          <template #default="{ row }">
            <el-button v-if="row.status === 1" size="small" type="success" link @click="settle(row)">{{ t('结转') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
      <el-empty v-if="!rows.length" :description="t('暂无成本单')" :image-size="80" />
    </el-card>

    <el-dialog v-model="collectVisible" :title="t('归集成本')" width="420px">
      <el-form label-width="120px">
        <el-form-item :label="t('工单号')" required>
          <el-input v-model="form.workOrderNo" :placeholder="t('请输入工单号')" />
        </el-form-item>
        <el-form-item :label="t('标准人工')">
          <el-input-number v-model="form.laborStd" :min="0" :precision="2" controls-position="right" style="width: 100%" />
        </el-form-item>
        <el-form-item :label="t('标准制造费用')">
          <el-input-number v-model="form.overheadStd" :min="0" :precision="2" controls-position="right" style="width: 100%" />
        </el-form-item>
        <el-alert type="info" :closable="false" :title="t('料成本自动取该工单 MES 实际消耗 × BOM 供给单价')" />
      </el-form>
      <template #footer>
        <el-button @click="collectVisible = false">{{ t('取消') }}</el-button>
        <el-button type="primary" :disabled="!form.workOrderNo" @click="doCollect">{{ t('确定') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { costApi } from '@/api/fin/fin'
import { COST_SHEET_STATUS_LABEL, COST_SHEET_STATUS_TAG, type CostSheet } from '@/types/fin/fin'

const { t } = useI18n()

const rows = ref<CostSheet[]>([])
const status = ref<number | undefined>(undefined)
const loading = ref(false)
const collectVisible = ref(false)
const form = reactive({ workOrderNo: '', laborStd: 0, overheadStd: 0 })

async function load() {
  loading.value = true
  try {
    const res = await costApi.list(status.value)
    rows.value = res?.data || []
  } finally {
    loading.value = false
  }
}

function openCollect() {
  form.workOrderNo = ''
  form.laborStd = 0
  form.overheadStd = 0
  collectVisible.value = true
}

async function doCollect() {
  const res = await costApi.collect(form.workOrderNo.trim(), form.laborStd, form.overheadStd)
  if (res?.code === 0) {
    ElMessage.success(t('已归集'))
    collectVisible.value = false
    await load()
  }
}

async function settle(row: CostSheet) {
  const res = await costApi.settle(row.workOrderNo)
  if (res?.code === 0) {
    ElMessage.success(t('已结转'))
    await load()
  }
}

function fmtAmt(v?: number): string { return (v || 0).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) }
function fmtQty(v?: number): string { return (v || 0).toLocaleString('en-US', { maximumFractionDigits: 4 }) }

onMounted(load)
</script>

<style scoped>
.cost-sheet { padding: 16px; }
.page-header { margin-bottom: 12px; }
.page-header h2 { margin: 0; color: #303133; font-size: 20px; font-weight: 650; }
.subtitle { color: #909399; font-size: 12px; }
.table-toolbar { margin-bottom: 8px; display: flex; gap: 8px; align-items: center; }
.over { color: #f56c6c; }
.under { color: #67c23a; }
</style>
