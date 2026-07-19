<template>
  <div class="asset-category">
    <div class="page-header">
      <h2>{{ t('资产分类') }}</h2>
      <span class="subtitle">{{ t('资产分类与默认折旧规则（科目锚点）') }}</span>
    </div>

    <el-card shadow="never">
      <div class="table-toolbar">
        <el-button v-permission="'fin-asset-category:add'" type="primary" size="small" @click="openAdd">{{ t('新建') }}</el-button>
        <el-button size="small" @click="load">{{ t('刷新') }}</el-button>
        <el-tag size="small" type="info">{{ t('共 {n} 条', { n: rows.length }) }}</el-tag>
      </div>

      <el-table :data="rows" border stripe size="small" max-height="620" v-loading="loading">
        <el-table-column prop="code" :label="t('asset.field.code')" width="120" sortable />
        <el-table-column prop="name" :label="t('名称')" min-width="160" show-overflow-tooltip />
        <el-table-column :label="t('asset.field.method')" width="140">
          <template #default="{ row }">{{ t('asset.method.' + row.defaultMethod) }}</template>
        </el-table-column>
        <el-table-column prop="defaultUsefulLifeMonths" :label="t('asset.field.life')" width="120" align="right" />
        <el-table-column prop="defaultSalvageRate" :label="t('asset.field.salvageRate')" width="120" align="right" />
        <el-table-column :label="t('状态')" width="90" align="center">
          <template #default="{ row }">
            <el-tag :type="row.isActive ? 'success' : 'info'" size="small">{{ row.isActive ? t('启用') : t('停用') }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column :label="t('操作')" width="140" fixed="right">
          <template #default="{ row }">
            <el-button v-permission="'fin-asset-category:edit'" link type="primary" size="small" @click="openEdit(row)">{{ t('编辑') }}</el-button>
            <el-button v-permission="'fin-asset-category:delete'" link type="danger" size="small" @click="remove(row)">{{ t('删除') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="dlg" :title="editing.id ? t('编辑') : t('新建')" width="600">
      <el-form :model="editing" label-width="120px" size="small">
        <el-form-item :label="t('asset.field.code')">
          <el-input v-model="editing.code" :disabled="!!editing.id" />
        </el-form-item>
        <el-form-item :label="t('名称')"><el-input v-model="editing.name" /></el-form-item>
        <el-form-item :label="t('asset.field.method')">
          <el-select v-model="editing.defaultMethod" style="width:100%">
            <el-option v-for="m in [1,2,3,4]" :key="m" :value="m" :label="t('asset.method.' + m)" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('asset.field.life')">
          <el-input-number v-model="editing.defaultUsefulLifeMonths" :min="0" />
        </el-form-item>
        <el-form-item :label="t('asset.field.salvageRate')">
          <el-input-number v-model="editing.defaultSalvageRate" :min="0" :max="1" :step="0.01" :precision="4" />
        </el-form-item>
        <el-form-item :label="t('asset.field.assetAccount')">
          <el-select v-model="editing.assetAccountId" filterable style="width:100%">
            <el-option v-for="a in accounts" :key="a.id" :value="a.id" :label="`${a.code} ${a.name}`" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('asset.field.accumAccount')">
          <el-select v-model="editing.accumDeprecAccountId" filterable style="width:100%">
            <el-option v-for="a in accounts" :key="a.id" :value="a.id" :label="`${a.code} ${a.name}`" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('asset.field.expenseAccount')">
          <el-select v-model="editing.deprecExpenseAccountId" filterable style="width:100%">
            <el-option v-for="a in accounts" :key="a.id" :value="a.id" :label="`${a.code} ${a.name}`" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('启用')"><el-switch v-model="editing.isActive" /></el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dlg = false">{{ t('取消') }}</el-button>
        <el-button type="primary" @click="save">{{ t('保存') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, reactive } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { assetCategoryApi } from '@/api/fin/asset'
import { glAccountApi } from '@/api/fin/fin'
import type { AssetCategory } from '@/types/fin/asset'

const { t } = useI18n()
const rows = ref<AssetCategory[]>([])
const accounts = ref<any[]>([])
const loading = ref(false)
const dlg = ref(false)
const editing = reactive<AssetCategory>({
  code: '', name: '', level: 1, defaultMethod: 1, defaultUsefulLifeMonths: 60, defaultSalvageRate: 0,
  assetAccountId: '', accumDeprecAccountId: '', deprecExpenseAccountId: '', isActive: true,
})

async function load() {
  loading.value = true
  try { rows.value = (await assetCategoryApi.list()).data ?? [] }
  finally { loading.value = false }
}

async function loadAccounts() {
  accounts.value = ((await glAccountApi.list()).data ?? []).filter((a: any) => a.isLeaf)
}

function openAdd() {
  Object.assign(editing, {
    id: undefined, code: '', name: '', level: 1, defaultMethod: 1, defaultUsefulLifeMonths: 60,
    defaultSalvageRate: 0, assetAccountId: '', accumDeprecAccountId: '', deprecExpenseAccountId: '', isActive: true,
  })
  dlg.value = true
}

function openEdit(row: AssetCategory) { Object.assign(editing, row); dlg.value = true }

async function save() {
  try {
    if (editing.id) await assetCategoryApi.update(editing.id, editing)
    else await assetCategoryApi.create(editing)
    ElMessage.success(t('操作成功'))
    dlg.value = false
    await load()
  } catch (e: any) {
    ElMessage.error(t(e?.response?.data?.message ?? '操作失败'))
  }
}

async function remove(row: AssetCategory) {
  try {
    await ElMessageBox.confirm(t('确认删除'), t('删除'), { type: 'warning' })
    await assetCategoryApi.remove(row.id!)
    ElMessage.success(t('操作成功'))
    await load()
  } catch (e: any) {
    if (e === 'cancel') return
    ElMessage.error(t(e?.response?.data?.message ?? '操作失败'))
  }
}

onMounted(() => { load(); loadAccounts() })
</script>

<style scoped>
.asset-category { padding: 16px; }
.page-header { margin-bottom: 12px; }
.page-header h2 { margin: 0; color: #303133; font-size: 20px; font-weight: 650; }
.subtitle { color: #909399; font-size: 12px; }
.table-toolbar { margin-bottom: 8px; display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
</style>
