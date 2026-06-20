<template>
  <div class="page">
    <el-card>
      <div class="toolbar" style="margin-bottom:12px">
        <el-button type="primary" @click="openAdd">{{ t('common.add') }}</el-button>
      </div>
      <el-table :data="rows" border>
        <el-table-column prop="code" :label="t('asset.field.code')" width="120" />
        <el-table-column prop="name" :label="t('common.name')" />
        <el-table-column :label="t('asset.field.method')" width="140">
          <template #default="{ row }">{{ t('asset.method.' + row.defaultMethod) }}</template>
        </el-table-column>
        <el-table-column prop="defaultUsefulLifeMonths" :label="t('asset.field.life')" width="120" align="right" />
        <el-table-column prop="defaultSalvageRate" :label="t('asset.field.salvageRate')" width="120" align="right" />
        <el-table-column :label="t('common.active')" width="80">
          <template #default="{ row }">{{ row.isActive ? '✓' : '–' }}</template>
        </el-table-column>
        <el-table-column :label="t('common.action')" width="160">
          <template #default="{ row }">
            <el-button size="small" @click="openEdit(row)">{{ t('common.edit') }}</el-button>
            <el-button size="small" type="danger" @click="remove(row)">{{ t('common.delete') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="dlg" :title="editing.id ? t('common.edit') : t('common.add')" width="600px">
      <el-form :model="editing" label-width="120px">
        <el-form-item :label="t('asset.field.code')">
          <el-input v-model="editing.code" :disabled="!!editing.id" />
        </el-form-item>
        <el-form-item :label="t('common.name')"><el-input v-model="editing.name" /></el-form-item>
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
        <el-form-item :label="t('common.active')"><el-switch v-model="editing.isActive" /></el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dlg = false">{{ t('common.cancel') }}</el-button>
        <el-button type="primary" @click="save">{{ t('common.save') }}</el-button>
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
const dlg = ref(false)
const editing = reactive<AssetCategory>({
  code: '', name: '', level: 1, defaultMethod: 1, defaultUsefulLifeMonths: 60, defaultSalvageRate: 0,
  assetAccountId: '', accumDeprecAccountId: '', deprecExpenseAccountId: '', isActive: true,
})

async function load() { rows.value = (await assetCategoryApi.list()).data ?? [] }

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
    ElMessage.success(t('common.ok'))
    dlg.value = false
    await load()
  } catch (e: any) {
    ElMessage.error(t(e?.response?.data?.message ?? 'common.fail'))
  }
}

async function remove(row: AssetCategory) {
  try {
    await ElMessageBox.confirm(t('common.confirmDelete'), t('common.delete'), { type: 'warning' })
    await assetCategoryApi.remove(row.id!)
    ElMessage.success(t('common.ok'))
    await load()
  } catch (e: any) {
    if (e === 'cancel') return
    ElMessage.error(t(e?.response?.data?.message ?? 'common.fail'))
  }
}

onMounted(() => { load(); loadAccounts() })
</script>
