<template>
  <div class="page">
    <el-card>
      <div class="toolbar" style="display:flex;gap:8px;align-items:center;margin-bottom:12px">
        <el-select v-model="periodId" :placeholder="t('asset.field.period')" filterable style="width:240px"
          @change="onPeriodChange">
          <el-option v-for="p in periods" :key="p.id"
            :label="`${p.year}-${String(p.month).padStart(2, '0')}`" :value="p.id" />
        </el-select>
        <el-button @click="loadPreview" :disabled="!periodId">{{ t('common.preview') }}</el-button>
        <el-button type="primary" @click="run" :disabled="!periodId">{{ t('asset.action.run') }}</el-button>
      </div>
      <el-table :data="preview" border>
        <el-table-column prop="assetNo" :label="t('asset.field.assetNo')" width="160" />
        <el-table-column prop="assetName" :label="t('common.name')" />
        <el-table-column :label="t('asset.field.method')" width="140">
          <template #default="{ row }">{{ t('asset.method.' + row.method) }}</template>
        </el-table-column>
        <el-table-column prop="depreciationAmount" :label="t('asset.field.amount')" align="right" width="130" />
        <el-table-column prop="openingAccumulated" :label="t('asset.field.opening')" align="right" width="130" />
        <el-table-column prop="closingAccumulated" :label="t('asset.field.closing')" align="right" width="130" />
      </el-table>
    </el-card>

    <el-card style="margin-top:12px">
      <div class="toolbar" style="margin-bottom:12px">
        <strong>{{ t('asset.action.runs') }}</strong>
      </div>
      <el-table :data="runs" border>
        <el-table-column prop="no" :label="t('asset.field.runNo')" width="180" />
        <el-table-column prop="periodYearMonth" :label="t('asset.field.period')" width="120" />
        <el-table-column :label="t('asset.field.runStatus')" width="100">
          <template #default="{ row }">{{ t('asset.runStatus.' + row.status) }}</template>
        </el-table-column>
        <el-table-column prop="assetCount" :label="t('asset.field.count')" align="right" width="80" />
        <el-table-column prop="totalAmount" :label="t('asset.field.amount')" align="right" width="130" />
        <el-table-column :label="t('common.action')" width="220">
          <template #default="{ row }">
            <el-button size="small" v-if="row.status === 0" type="primary" @click="post(row.id)">{{ t('asset.action.post') }}</el-button>
            <el-button size="small" v-if="row.status === 1" type="danger" @click="reverse(row.id)">{{ t('asset.action.reverse') }}</el-button>
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

async function loadPeriods() { periods.value = (await periodApi.list()).data ?? [] }
async function loadPreview() { preview.value = (await assetDeprecApi.preview(periodId.value)).data ?? [] }
async function loadRuns() { runs.value = (await assetDeprecApi.list(periodId.value || undefined)).data ?? [] }

function onPeriodChange() { preview.value = []; loadRuns() }

async function run() {
  try { await assetDeprecApi.run(periodId.value); ElMessage.success(t('common.ok')); await loadRuns() }
  catch (e: any) { ElMessage.error(t(e?.response?.data?.message ?? 'common.fail')) }
}

async function post(id: string) {
  try { await assetDeprecApi.post(id); ElMessage.success(t('common.ok')); await loadRuns() }
  catch (e: any) { ElMessage.error(t(e?.response?.data?.message ?? 'common.fail')) }
}

async function reverse(id: string) {
  try {
    const { value } = await ElMessageBox.prompt(t('asset.action.reverseReason'), t('asset.action.reverse'))
    await assetDeprecApi.reverse(id, value)
    ElMessage.success(t('common.ok'))
    await loadRuns()
  } catch (e: any) {
    if (e === 'cancel') return
    ElMessage.error(t(e?.response?.data?.message ?? 'common.fail'))
  }
}

onMounted(() => { loadPeriods() })
</script>
