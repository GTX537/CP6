<template>
  <div class="pur-supplier-price">
    <div class="page-header">
      <h2>{{ t('供应商价表') }}</h2>
      <span class="subtitle">{{ t('供应商×物料 阶梯价，建采购单时按“满足起订量∧当时有效取最大档”带价') }}</span>
    </div>

    <el-card shadow="never">
      <div class="table-toolbar">
        <el-input v-model="supplierId" size="small" style="width: 160px" :placeholder="t('供应商编码')" clearable @keyup.enter="reload" />
        <el-input v-model="itemId" size="small" style="width: 160px" :placeholder="t('物料编码（可选）')" clearable @keyup.enter="reload" />
        <el-button type="primary" size="small" @click="reload">{{ t('查询') }}</el-button>
        <el-button v-permission="'pur-supplier-price:add'" size="small" :disabled="!supplierId.trim()" @click="openCreate">{{ t('新增价档') }}</el-button>
        <el-tag size="small" type="info">{{ t('共 {n} 条', { n: rows.length }) }}</el-tag>
      </div>

      <el-table :data="rows" border stripe size="small" max-height="560" v-loading="loading">
        <el-table-column prop="itemId" :label="t('物料')" width="160" show-overflow-tooltip />
        <el-table-column prop="price" :label="t('单价')" width="120" align="right" />
        <el-table-column :label="t('币种')" width="80">
          <template #default="{ row }">{{ row.currencyCd || 'JPY' }}</template>
        </el-table-column>
        <el-table-column prop="minQty" :label="t('起订量')" width="110" align="right" />
        <el-table-column :label="t('生效日')" width="120">
          <template #default="{ row }">{{ (row.validFrom || '').slice(0, 10) }}</template>
        </el-table-column>
        <el-table-column :label="t('失效日')" width="120">
          <template #default="{ row }">{{ row.validTo ? row.validTo.slice(0, 10) : t('长期') }}</template>
        </el-table-column>
        <el-table-column :label="t('来源')" width="90" align="center">
          <template #default="{ row }">
            <el-tag size="small" :type="row.source === 'rfq' ? 'warning' : 'info'">{{ t(row.source === 'rfq' ? '询价回写' : '手工维护') }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column :label="t('操作')" width="80" fixed="right">
          <template #default="{ row }">
            <el-button v-permission="'pur-supplier-price:delete'" link type="danger" size="small" @click="doDelete(row)">{{ t('删除') }}</el-button>
          </template>
        </el-table-column>
        <template #empty>
          <span>{{ supplierId.trim() ? t('暂无数据') : t('请先输入供应商编码查询') }}</span>
        </template>
      </el-table>
    </el-card>

    <el-card shadow="never" style="margin-top: 12px">
      <div class="lines-head"><span>{{ t('带价试算') }}</span></div>
      <div class="table-toolbar">
        <el-input v-model="probe.itemId" size="small" style="width: 160px" :placeholder="t('物料编码')" />
        <el-input-number v-model="probe.qty" :min="0" size="small" controls-position="right" style="width: 130px" :placeholder="t('数量')" />
        <el-date-picker v-model="probe.onDate" type="date" size="small" value-format="YYYY-MM-DD" :placeholder="t('基准日（可选）')" style="width: 150px" />
        <el-button size="small" :disabled="!supplierId.trim() || !probe.itemId.trim()" @click="doResolve">{{ t('解析单价') }}</el-button>
        <el-tag v-if="probe.result !== undefined" size="small" :type="probe.result === null ? 'danger' : 'success'">
          {{ probe.result === null ? t('无适用采购价') : t('解析单价') + '：' + probe.result }}
        </el-tag>
      </div>
    </el-card>

    <el-dialog v-model="dialogVisible" :title="t('新增价档')" width="560">
      <el-form :model="form" label-width="90px" size="small">
        <el-form-item :label="t('供应商')"><el-input v-model="form.supplierId" disabled /></el-form-item>
        <el-form-item :label="t('物料')" required><el-input v-model="form.itemId" maxlength="40" /></el-form-item>
        <el-row :gutter="12">
          <el-col :span="12"><el-form-item :label="t('单价')" required><el-input-number v-model="form.price" :min="0" :precision="4" controls-position="right" style="width:100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('币种')"><el-input v-model="form.currencyCd" maxlength="3" :placeholder="t('留空=本位币')" /></el-form-item></el-col>
        </el-row>
        <el-row :gutter="12">
          <el-col :span="12"><el-form-item :label="t('起订量')"><el-input-number v-model="form.minQty" :min="0" controls-position="right" style="width:100%" /></el-form-item></el-col>
        </el-row>
        <el-row :gutter="12">
          <el-col :span="12"><el-form-item :label="t('生效日')" required><el-date-picker v-model="form.validFrom" type="date" value-format="YYYY-MM-DD" style="width:100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('失效日')"><el-date-picker v-model="form.validTo" type="date" value-format="YYYY-MM-DD" style="width:100%" :placeholder="t('留空=长期')" /></el-form-item></el-col>
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
import { supplierPriceApi } from '@/api/pur/pur'
import type { SupplierPrice } from '@/types/pur/pur'

const { t } = useI18n()
const rows = ref<SupplierPrice[]>([])
const loading = ref(false)
const saving = ref(false)
const supplierId = ref('')
const itemId = ref('')
const dialogVisible = ref(false)

const probe = reactive<{ itemId: string; qty: number; onDate: string | null; result?: number | null }>(
  { itemId: '', qty: 1, onDate: null, result: undefined },
)

function emptyForm(): SupplierPrice {
  const today = new Date().toISOString().slice(0, 10)
  return { supplierId: supplierId.value.trim(), itemId: '', price: 0, currencyCd: '', minQty: 0, validFrom: today, validTo: null, source: 'manual' }
}
const form = reactive<SupplierPrice>(emptyForm())

async function reload() {
  if (!supplierId.value.trim()) { rows.value = []; return }
  loading.value = true
  try {
    const res = await supplierPriceApi.list(supplierId.value.trim(), itemId.value.trim() || undefined)
    rows.value = res?.data || []
  } finally {
    loading.value = false
  }
}

function openCreate() {
  Object.assign(form, emptyForm())
  dialogVisible.value = true
}

async function submit() {
  if (!form.itemId?.trim()) { ElMessage.warning(t('物料必填')); return }
  if ((form.price ?? 0) < 0) { ElMessage.warning(t('单价不可为负')); return }
  saving.value = true
  try {
    await supplierPriceApi.save({ ...form, currencyCd: form.currencyCd || null, validTo: form.validTo || null })
    ElMessage.success(t('已保存'))
    dialogVisible.value = false
    await reload()
  } finally {
    saving.value = false
  }
}

async function doDelete(row: SupplierPrice) {
  if (!row.id) return
  await ElMessageBox.confirm(t('确认删除该价档？'), t('提示'), { type: 'warning' })
  await supplierPriceApi.remove(row.id)
  ElMessage.success(t('已删除'))
  await reload()
}

async function doResolve() {
  const res = await supplierPriceApi.resolve(supplierId.value.trim(), probe.itemId.trim(), probe.qty || 0, probe.onDate || undefined)
  probe.result = res?.data?.price ?? null
}
</script>

<style scoped>
.pur-supplier-price { padding: 16px; }
.page-header { margin-bottom: 12px; }
.page-header h2 { margin: 0; color: #303133; font-size: 20px; font-weight: 650; }
.subtitle { color: #909399; font-size: 12px; }
.table-toolbar { margin-bottom: 8px; display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
.lines-head { display: flex; justify-content: space-between; align-items: center; margin: 4px 0 8px; font-weight: 600; }
</style>
