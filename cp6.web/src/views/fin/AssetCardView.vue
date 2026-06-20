<template>
  <div class="page">
    <el-card>
      <div class="toolbar" style="margin-bottom:12px;display:flex;gap:8px">
        <el-select v-model="categoryId" :placeholder="t('asset.field.category')" clearable filterable
          @change="load" style="width:200px">
          <el-option v-for="c in categories" :key="c.id" :value="c.id" :label="`${c.code} ${c.name}`" />
        </el-select>
        <el-select v-model="statusFilter" :placeholder="t('common.status')" clearable @change="load" style="width:160px">
          <el-option v-for="s in [0,1,2,3]" :key="s" :value="s" :label="t('asset.status.' + s)" />
        </el-select>
        <el-button type="primary" @click="openAdd">{{ t('common.add') }}</el-button>
      </div>
      <el-table :data="rows" border>
        <el-table-column prop="assetNo" :label="t('asset.field.assetNo')" width="180" />
        <el-table-column prop="name" :label="t('common.name')" />
        <el-table-column prop="originalValue" :label="t('asset.field.originalValue')" align="right" width="130" />
        <el-table-column prop="accumulatedDepreciation" :label="t('asset.field.accumulated')" align="right" width="130" />
        <el-table-column :label="t('asset.field.netValue')" align="right" width="130">
          <template #default="{ row }">{{ (row.originalValue - row.accumulatedDepreciation).toFixed(2) }}</template>
        </el-table-column>
        <el-table-column :label="t('common.status')" width="120">
          <template #default="{ row }">{{ t('asset.status.' + row.status) }}</template>
        </el-table-column>
        <el-table-column :label="t('common.action')" width="240">
          <template #default="{ row }">
            <el-button size="small" v-if="row.status === 0" type="primary" @click="activate(row)">{{ t('asset.action.activate') }}</el-button>
            <el-button size="small" @click="showSchedule(row)">{{ t('asset.action.schedule') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="dlg" :title="t('common.add')" width="640px">
      <el-form :model="editing" label-width="140px">
        <el-form-item :label="t('asset.field.category')">
          <el-select v-model="editing.categoryId" filterable style="width:100%" @change="onCategoryChange">
            <el-option v-for="c in categories" :key="c.id" :value="c.id" :label="`${c.code} ${c.name}`" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('common.name')"><el-input v-model="editing.name" /></el-form-item>
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
        <el-button @click="dlg = false">{{ t('common.cancel') }}</el-button>
        <el-button type="primary" @click="save">{{ t('common.save') }}</el-button>
      </template>
    </el-dialog>

    <el-drawer v-model="scheduleDrawer" :title="t('asset.action.schedule')" size="50%">
      <el-table :data="schedule" border>
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
const dlg = ref(false)
const scheduleDrawer = ref(false)
const schedule = ref<DepreciationScheduleRow[]>([])

const editing = reactive<AssetCard>({
  name: '', categoryId: '', originalValue: 0, salvageRate: 0, salvageValue: 0,
  method: 1, usefulLifeMonths: 0, acquisitionDate: '',
  accumulatedDepreciation: 0, depreciatedPeriods: 0, status: 0, isOpeningImport: false,
})

async function load() {
  rows.value = (await assetCardApi.list(categoryId.value || undefined, statusFilter.value)).data ?? []
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
    ElMessage.success(t('common.ok'))
    dlg.value = false
    await load()
  } catch (e: any) {
    ElMessage.error(t(e?.response?.data?.message ?? 'common.fail'))
  }
}

async function activate(row: AssetCard) {
  try { await assetCardApi.activate(row.id!); ElMessage.success(t('common.ok')); await load() }
  catch (e: any) { ElMessage.error(t(e?.response?.data?.message ?? 'common.fail')) }
}

async function showSchedule(row: AssetCard) {
  schedule.value = (await assetCardApi.schedule(row.id!)).data ?? []
  scheduleDrawer.value = true
}

onMounted(() => { load(); loadCategories() })
</script>
