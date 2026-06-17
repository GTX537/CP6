<template>
  <div class="plan-item-policy">
    <div class="page-header">
      <h2>{{ t('计划主数据') }}</h2>
      <span class="subtitle">{{ t('安全库存') }} / {{ t('采购提前期') }} / {{ t('批量规则') }} / {{ t('自制采购') }}</span>
    </div>

    <el-card shadow="never">
      <div class="table-toolbar">
        <el-input v-model="keyword" size="small" style="width: 200px" :placeholder="t('物料编码')" clearable @keyup.enter="reload" />
        <el-button type="primary" size="small" @click="reload">{{ t('查询') }}</el-button>
        <el-button size="small" @click="openCreate">{{ t('新增') }}</el-button>
        <el-tag size="small" type="info">{{ t('共 {n} 条', { n: rows.length }) }}</el-tag>
      </div>

      <el-table :data="rows" border stripe size="small" max-height="600" v-loading="loading">
        <el-table-column prop="itemCd" :label="t('物料编码')" width="180" show-overflow-tooltip />
        <el-table-column :label="t('自制采购')" width="100" align="center">
          <template #default="{ row }">
            <el-tag size="small" :type="row.makeOrBuy === 1 ? 'success' : 'info'">{{ t(makeOrBuyLabel(row.makeOrBuy)) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="safetyStock" :label="t('安全库存')" width="120" align="right" />
        <el-table-column prop="purchaseLeadDays" :label="t('采购提前期')" width="120" align="right" />
        <el-table-column :label="t('批量规则')" width="120" align="center">
          <template #default="{ row }">{{ t(lotRuleLabel(row.lotRule)) }}</template>
        </el-table-column>
        <el-table-column prop="moqQty" :label="t('最小起订量')" width="120" align="right" />
        <el-table-column prop="multipleQty" :label="t('订货倍数')" width="120" align="right" />
        <el-table-column :label="t('操作')" width="140" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="openEdit(row)">{{ t('编辑') }}</el-button>
            <el-button link type="danger" size="small" @click="doDelete(row)">{{ t('删除') }}</el-button>
          </template>
        </el-table-column>
        <template #empty><span>{{ t('暂无数据') }}</span></template>
      </el-table>
    </el-card>

    <el-dialog v-model="dialogVisible" :title="t('计划主数据')" width="560">
      <el-form :model="form" label-width="100px" size="small">
        <el-form-item :label="t('物料编码')" required>
          <el-input v-model="form.itemCd" maxlength="20" :disabled="editing" />
        </el-form-item>
        <el-row :gutter="12">
          <el-col :span="12"><el-form-item :label="t('自制采购')">
            <el-select v-model="form.makeOrBuy" style="width:100%">
              <el-option :label="t('采购')" :value="2" />
              <el-option :label="t('自制')" :value="1" />
            </el-select>
          </el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('批量规则')">
            <el-select v-model="form.lotRule" style="width:100%">
              <el-option :label="t('按需')" :value="1" />
              <el-option :label="t('最小量')" :value="2" />
              <el-option :label="t('订货倍数')" :value="3" />
              <el-option :label="t('整卷取整')" :value="4" />
            </el-select>
          </el-form-item></el-col>
        </el-row>
        <el-row :gutter="12">
          <el-col :span="12"><el-form-item :label="t('安全库存')"><el-input-number v-model="form.safetyStock" :min="0" controls-position="right" style="width:100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('采购提前期')"><el-input-number v-model="form.purchaseLeadDays" :min="0" controls-position="right" style="width:100%" /></el-form-item></el-col>
        </el-row>
        <el-row :gutter="12">
          <el-col :span="12"><el-form-item :label="t('最小起订量')"><el-input-number v-model="form.moqQty" :min="0" controls-position="right" style="width:100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('订货倍数')"><el-input-number v-model="form.multipleQty" :min="0" controls-position="right" style="width:100%" /></el-form-item></el-col>
        </el-row>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false" :disabled="saving">{{ t('取消') }}</el-button>
        <el-button type="primary" :loading="saving" @click="submit">{{ t('确定') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { itemPolicyApi } from '@/api/plan/plan'
import type { ItemPolicy } from '@/types/plan/plan'

const { t } = useI18n()
const rows = ref<ItemPolicy[]>([])
const loading = ref(false)
const saving = ref(false)
const keyword = ref('')
const dialogVisible = ref(false)
const editing = ref(false)

function makeOrBuyLabel(v: number) { return v === 1 ? '自制' : '采购' }
function lotRuleLabel(v: number) { return v === 2 ? '最小量' : v === 3 ? '订货倍数' : v === 4 ? '整卷取整' : '按需' }

function emptyForm(): ItemPolicy {
  return { itemCd: '', safetyStock: 0, purchaseLeadDays: 0, lotRule: 1, moqQty: 0, multipleQty: 0, makeOrBuy: 2 }
}
const form = reactive<ItemPolicy>(emptyForm())

async function reload() {
  loading.value = true
  try {
    const res = await itemPolicyApi.list(keyword.value.trim() || undefined)
    rows.value = res?.data || []
  } finally {
    loading.value = false
  }
}

function openCreate() {
  Object.assign(form, emptyForm())
  editing.value = false
  dialogVisible.value = true
}

function openEdit(row: ItemPolicy) {
  Object.assign(form, { ...row, moqQty: row.moqQty ?? 0, multipleQty: row.multipleQty ?? 0 })
  editing.value = true
  dialogVisible.value = true
}

async function submit() {
  if (!form.itemCd?.trim()) { ElMessage.warning(t('物料编码')); return }
  saving.value = true
  try {
    await itemPolicyApi.save({ ...form, itemCd: form.itemCd.trim() })
    ElMessage.success(t('已保存'))
    dialogVisible.value = false
    await reload()
  } finally {
    saving.value = false
  }
}

async function doDelete(row: ItemPolicy) {
  await ElMessageBox.confirm(t('确认删除'), t('提示'), { type: 'warning' })
  await itemPolicyApi.remove(row.itemCd)
  ElMessage.success(t('已删除'))
  await reload()
}

reload()
</script>

<style scoped>
.plan-item-policy { padding: 16px; }
.page-header { margin-bottom: 12px; }
.page-header h2 { margin: 0; color: #303133; font-size: 20px; font-weight: 650; }
.subtitle { color: #909399; font-size: 12px; }
.table-toolbar { margin-bottom: 8px; display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
</style>
