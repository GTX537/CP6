<template>
  <div class="asset-deprec">
    <div class="page-header">
      <h2>{{ t('折旧计提') }}</h2>
      <span class="subtitle">{{ t('按期试算→计提→过账（汇总凭证）') }}</span>
    </div>

    <el-card shadow="never">
      <div class="table-toolbar">
        <el-select v-model="periodId" :placeholder="t('asset.field.period')" filterable size="small" style="width:240px"
          @change="onPeriodChange">
          <el-option v-for="p in periods" :key="p.id"
            :label="`${p.year}-${String(p.month).padStart(2, '0')}`" :value="p.id" />
        </el-select>
        <el-button size="small" @click="loadPreview" :disabled="!periodId">{{ t('试算') }}</el-button>
        <el-button v-permission="'fin-asset-deprec:run'" type="primary" size="small" @click="run" :disabled="!periodId">{{ t('asset.action.run') }}</el-button>
        <el-tag v-if="preview.length" size="small" type="info">{{ t('共 {n} 条', { n: preview.length }) }}</el-tag>
      </div>

      <el-table :data="preview" border stripe size="small" max-height="420" v-loading="loadingPreview">
        <el-table-column prop="assetNo" :label="t('asset.field.assetNo')" width="160" />
        <el-table-column prop="assetName" :label="t('名称')" min-width="160" show-overflow-tooltip />
        <el-table-column :label="t('asset.field.method')" width="140">
          <template #default="{ row }">{{ t('asset.method.' + row.method) }}</template>
        </el-table-column>
        <el-table-column prop="depreciationAmount" :label="t('asset.field.amount')" align="right" width="130" />
        <el-table-column prop="openingAccumulated" :label="t('asset.field.opening')" align="right" width="130" />
        <el-table-column prop="closingAccumulated" :label="t('asset.field.closing')" align="right" width="130" />
      </el-table>
    </el-card>

    <el-card shadow="never" style="margin-top:12px">
      <div class="table-toolbar">
        <strong>{{ t('asset.action.runs') }}</strong>
        <el-button size="small" @click="loadRuns">{{ t('刷新') }}</el-button>
      </div>
      <el-table :data="runs" border stripe size="small" max-height="420" v-loading="loadingRuns">
        <el-table-column prop="no" :label="t('asset.field.runNo')" width="180" />
        <el-table-column prop="periodYearMonth" :label="t('asset.field.period')" width="120" />
        <el-table-column :label="t('asset.field.runStatus')" width="110" align="center">
          <template #default="{ row }">
            <el-tag :type="runStatusTag(row.status)" size="small">{{ t('asset.runStatus.' + row.status) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="assetCount" :label="t('asset.field.count')" align="right" width="80" />
        <el-table-column prop="totalAmount" :label="t('asset.field.amount')" align="right" width="130" />
        <el-table-column :label="t('操作')" width="180" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" v-if="row.status === 0" v-permission="'fin-asset-deprec:post'" @click="post(row.id)">{{ t('asset.action.post') }}</el-button>
            <el-button link type="danger" size="small" v-if="row.status === 1" v-permission="'fin-asset-deprec:reverse'" @click="reverse(row.id)">{{ t('asset.action.reverse') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { assetDeprecApi } from '@/api/fin/asset'
import { periodApi } from '@/api/fin/fin'
import type { DepreciationEntryDto, DepreciationRun } from '@/types/fin/asset'

const { t } = useI18n()
const periods = ref<any[]>([])
const periodId = ref('')
const preview = ref<DepreciationEntryDto[]>([])
const runs = ref<DepreciationRun[]>([])
const loadingPreview = ref(false)
const loadingRuns = ref(false)

// 批次状态配色：0 草稿=info / 1 已过账=success / 2 已反冲=danger
function runStatusTag(status: number): 'success' | 'info' | 'danger' {
  return status === 1 ? 'success' : status === 0 ? 'info' : 'danger'
}

async function loadPeriods() { periods.value = (await periodApi.list()).data ?? [] }
async function loadPreview() {
  loadingPreview.value = true
  try { preview.value = (await assetDeprecApi.preview(periodId.value)).data ?? [] }
  finally { loadingPreview.value = false }
}
async function loadRuns() {
  loadingRuns.value = true
  try { runs.value = (await assetDeprecApi.list(periodId.value || undefined)).data ?? [] }
  finally { loadingRuns.value = false }
}

function onPeriodChange() { preview.value = []; loadRuns() }

async function run() {
  try { await assetDeprecApi.run(periodId.value); ElMessage.success(t('操作成功')); await loadRuns() }
  catch (e: any) { ElMessage.error(t(e?.response?.data?.message ?? '操作失败')) }
}

async function post(id: string) {
  try { await assetDeprecApi.post(id); ElMessage.success(t('操作成功')); await loadRuns() }
  catch (e: any) { ElMessage.error(t(e?.response?.data?.message ?? '操作失败')) }
}

async function reverse(id: string) {
  try {
    const { value } = await ElMessageBox.prompt(t('asset.action.reverseReason'), t('asset.action.reverse'))
    await assetDeprecApi.reverse(id, value)
    ElMessage.success(t('操作成功'))
    await loadRuns()
  } catch (e: any) {
    if (e === 'cancel') return
    ElMessage.error(t(e?.response?.data?.message ?? '操作失败'))
  }
}

onMounted(() => { loadPeriods() })
</script>

<style scoped>
.asset-deprec { padding: 16px; }
.page-header { margin-bottom: 12px; }
.page-header h2 { margin: 0; color: #303133; font-size: 20px; font-weight: 650; }
.subtitle { color: #909399; font-size: 12px; }
.table-toolbar { margin-bottom: 8px; display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
</style>
