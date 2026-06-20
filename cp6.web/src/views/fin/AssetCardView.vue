<template>
  <div class="asset-card">
    <div class="page-header">
      <h2>{{ t('资产卡片') }}</h2>
      <span class="subtitle">{{ t('固定资产卡片台账（原值/累折/净值）') }}</span>
    </div>

    <el-card shadow="never">
      <div class="table-toolbar">
        <el-select v-model="categoryId" :placeholder="t('asset.field.category')" clearable filterable
          size="small" @change="load" style="width:200px">
          <el-option v-for="c in categories" :key="c.id" :value="c.id" :label="`${c.code} ${c.name}`" />
        </el-select>
        <el-select v-model="statusFilter" :placeholder="t('状态')" clearable size="small" @change="load" style="width:140px">
          <el-option v-for="s in [0,1,2,3]" :key="s" :value="s" :label="t('asset.status.' + s)" />
        </el-select>
        <el-button type="primary" size="small" @click="openAdd">{{ t('新建') }}</el-button>
        <el-button size="small" @click="load">{{ t('刷新') }}</el-button>
        <el-tag size="small" type="info">{{ t('共 {n} 条', { n: rows.length }) }}</el-tag>
      </div>

      <el-table :data="rows" border stripe size="small" max-height="620" v-loading="loading">
        <el-table-column prop="assetNo" :label="t('asset.field.assetNo')" width="180" />
        <el-table-column prop="name" :label="t('名称')" min-width="160" show-overflow-tooltip />
        <el-table-column prop="originalValue" :label="t('asset.field.originalValue')" align="right" width="130" />
        <el-table-column prop="accumulatedDepreciation" :label="t('asset.field.accumulated')" align="right" width="130" />
        <el-table-column :label="t('asset.field.netValue')" align="right" width="130">
          <template #default="{ row }">{{ (row.originalValue - row.accumulatedDepreciation).toFixed(2) }}</template>
        </el-table-column>
        <el-table-column :label="t('状态')" width="110" align="center">
          <template #default="{ row }">
            <el-tag :type="statusTag(row.status)" size="small">{{ t('asset.status.' + row.status) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column :label="t('操作')" width="180" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" v-if="row.status === 0" @click="activate(row)">{{ t('asset.action.activate') }}</el-button>
            <el-button link size="small" @click="showSchedule(row)">{{ t('asset.action.schedule') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="dlg" :title="t('新建')" width="640">
      <el-form :model="editing" label-width="140px" size="small">
        <el-form-item :label="t('asset.field.category')">
          <el-select v-model="editing.categoryId" filterable style="width:100%" @change="onCategoryChange">
            <el-option v-for="c in categories" :key="c.id" :value="c.id" :label="`${c.code} ${c.name}`" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('名称')"><el-input v-model="editing.name" /></el-form-item>
        <el-form-item :label="t('asset.field.originalValue')">
          <el-input-number v-model="editing.originalValue" :min="0" :precision="2" style="width:100%" />
        </el-form-item>
        <el-form-item :label="t('asset.field.method')">
          <el-select v-model="editing.method" style="width:100%">
            <el-option v-for="m in [1,2,3,4]" :key="m" :value="m" :label="t('asset.method.' + m)" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('asset.field.life')">
          <el-input-number v-model="editing.usefulLifeMonths" :min="0" />
        </el-form-item>
        <el-form-item :label="t('asset.field.salvageRate')">
          <el-input-number v-model="editing.salvageRate" :min="0" :max="1" :step="0.01" :precision="4" />
        </el-form-item>
        <el-form-item :label="t('asset.field.acquisitionDate')">
          <el-date-picker v-model="editing.acquisitionDate" value-format="YYYY-MM-DD" style="width:100%" />
        </el-form-item>
        <el-form-item v-if="editing.method === 4" :label="t('asset.field.totalWorkload')">
          <el-input-number v-model="editing.totalWorkload" :min="0" :precision="4" />
        </el-form-item>
        <el-form-item :label="t('asset.field.openingImport')"><el-switch v-model="editing.isOpeningImport" /></el-form-item>
        <el-form-item v-if="editing.isOpeningImport" :label="t('asset.field.accumulated')">
          <el-input-number v-model="editing.accumulatedDepreciation" :min="0" :precision="2" />
        </el-form-item>
        <el-form-item v-if="editing.isOpeningImport" :label="t('asset.field.depreciatedPeriods')">
          <el-input-number v-model="editing.depreciatedPeriods" :min="0" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dlg = false">{{ t('取消') }}</el-button>
        <el-button type="primary" @click="save">{{ t('保存') }}</el-button>
      </template>
    </el-dialog>

    <el-drawer v-model="scheduleDrawer" :title="t('asset.action.schedule')" size="50%">
      <el-table :data="schedule" border stripe size="small" max-height="640">
        <el-table-column prop="periodIndex" label="#" width="60" />
        <el-table-column prop="yearMonth" :label="t('asset.field.period')" />
        <el-table-column prop="amount" :label="t('asset.field.amount')" align="right" />
        <el-table-column prop="accumulated" :label="t('asset.field.accumulated')" align="right" />
        <el-table-column prop="netValue" :label="t('asset.field.netValue')" align="right" />
      </el-table>
    </el-drawer>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, reactive } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { assetCardApi, assetCategoryApi } from '@/api/fin/asset'
import type { AssetCard, AssetCategory, DepreciationScheduleRow } from '@/types/fin/asset'

const { t } = useI18n()
const rows = ref<AssetCard[]>([])
const categories = ref<AssetCategory[]>([])
const categoryId = ref<string>('')
const statusFilter = ref<number | undefined>()
const loading = ref(false)
const dlg = ref(false)
const scheduleDrawer = ref(false)
const schedule = ref<DepreciationScheduleRow[]>([])

const editing = reactive<AssetCard>({
  name: '', categoryId: '', originalValue: 0, salvageRate: 0, salvageValue: 0,
  method: 1, usefulLifeMonths: 0, acquisitionDate: '',
  accumulatedDepreciation: 0, depreciatedPeriods: 0, status: 0, isOpeningImport: false,
})

// 状态标签配色：0 草稿=info / 1 在用=success / 2 已处置=warning / 其它=danger
function statusTag(status: number): 'success' | 'info' | 'warning' | 'danger' {
  return status === 1 ? 'success' : status === 0 ? 'info' : status === 2 ? 'warning' : 'danger'
}

async function load() {
  loading.value = true
  try { rows.value = (await assetCardApi.list(categoryId.value || undefined, statusFilter.value)).data ?? [] }
  finally { loading.value = false }
}
async function loadCategories() {
  categories.value = (await assetCategoryApi.list()).data ?? []
}

function onCategoryChange(id: string) {
  const c = categories.value.find(x => x.id === id)
  if (!c) return
  if (!editing.method) editing.method = c.defaultMethod
  if (!editing.usefulLifeMonths) editing.usefulLifeMonths = c.defaultUsefulLifeMonths
  if (!editing.salvageRate) editing.salvageRate = c.defaultSalvageRate
}

function openAdd() {
  Object.assign(editing, {
    id: undefined, name: '', categoryId: '', originalValue: 0, salvageRate: 0, salvageValue: 0,
    method: 1, usefulLifeMonths: 0, acquisitionDate: new Date().toISOString().slice(0, 10),
    accumulatedDepreciation: 0, depreciatedPeriods: 0, status: 0, isOpeningImport: false,
    totalWorkload: null,
  })
  dlg.value = true
}

async function save() {
  try {
    await assetCardApi.create(editing)
    ElMessage.success(t('操作成功'))
    dlg.value = false
    await load()
  } catch (e: any) {
    ElMessage.error(t(e?.response?.data?.message ?? '操作失败'))
  }
}

async function activate(row: AssetCard) {
  try { await assetCardApi.activate(row.id!); ElMessage.success(t('操作成功')); await load() }
  catch (e: any) { ElMessage.error(t(e?.response?.data?.message ?? '操作失败')) }
}

async function showSchedule(row: AssetCard) {
  schedule.value = (await assetCardApi.schedule(row.id!)).data ?? []
  scheduleDrawer.value = true
}

onMounted(() => { load(); loadCategories() })
</script>

<style scoped>
.asset-card { padding: 16px; }
.page-header { margin-bottom: 12px; }
.page-header h2 { margin: 0; color: #303133; font-size: 20px; font-weight: 650; }
.subtitle { color: #909399; font-size: 12px; }
.table-toolbar { margin-bottom: 8px; display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
</style>
