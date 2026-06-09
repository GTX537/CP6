<template>
  <div class="outbound-routing">
    <div class="page-header">
      <h2>{{ t('wms.outboundRouting.title') }}</h2>
      <span class="subtitle">{{ t('wms.outboundRouting.subtitle') }}</span>
    </div>

    <el-card shadow="never">
      <div class="table-toolbar">
        <el-button type="primary" size="small" @click="openCreate">
          {{ t('wms.outboundRouting.btn.create') }}
        </el-button>
        <el-button size="small" @click="reload">{{ t('wms.outboundRouting.btn.refresh') }}</el-button>
        <el-tag size="small" type="info">{{ t('wms.common.total') }}: {{ rows.length }}</el-tag>
      </div>

      <el-table :data="rows" border stripe size="small" max-height="560" v-loading="loading">
        <el-table-column prop="sortOrder" :label="t('wms.outboundRouting.col.sortOrder')" width="90" align="right" />
        <el-table-column prop="ruleName" :label="t('wms.outboundRouting.col.ruleName')" min-width="180" show-overflow-tooltip />
        <el-table-column prop="customerCd" :label="t('wms.outboundRouting.col.customerCd')" width="120">
          <template #default="{ row }">{{ row.customerCd || t('wms.outboundRouting.any') }}</template>
        </el-table-column>
        <el-table-column prop="productCdPrefix" :label="t('wms.outboundRouting.col.productPrefix')" width="130">
          <template #default="{ row }">{{ row.productCdPrefix || t('wms.outboundRouting.any') }}</template>
        </el-table-column>
        <el-table-column prop="outboundType" :label="t('wms.outboundRouting.col.outboundType')" width="120">
          <template #default="{ row }">
            {{ row.outboundType == null ? t('wms.outboundRouting.any') : t(`wms.outboundRouting.type.${row.outboundType}`) }}
          </template>
        </el-table-column>
        <el-table-column prop="targetWarehouseCd" :label="t('wms.outboundRouting.col.target')" width="130" show-overflow-tooltip />
        <el-table-column prop="enabled" :label="t('wms.outboundRouting.col.enabled')" width="100">
          <template #default="{ row }">
            <el-tag :type="row.enabled ? 'success' : 'info'" size="small">
              {{ row.enabled ? t('wms.outboundRouting.on') : t('wms.outboundRouting.off') }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="remarks" :label="t('wms.outboundRouting.col.remarks')" min-width="160" show-overflow-tooltip />
        <el-table-column :label="t('wms.outboundRouting.col.action')" width="150" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="openEdit(row)">
              {{ t('wms.outboundRouting.btn.edit') }}
            </el-button>
            <el-button link type="danger" size="small" @click="remove(row)">
              {{ t('wms.outboundRouting.btn.delete') }}
            </el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- 候補倉庫プレビュー（設定検証ツール） -->
    <el-card shadow="never" class="preview-card">
      <template #header>{{ t('wms.outboundRouting.preview.title') }}</template>
      <el-form :model="preview" inline size="small">
        <el-form-item :label="t('wms.outboundRouting.preview.productCd')">
          <el-input v-model="preview.productCd" style="width: 140px" />
        </el-form-item>
        <el-form-item :label="t('wms.outboundRouting.preview.customerCd')">
          <el-input v-model="preview.customerCd" clearable style="width: 130px" />
        </el-form-item>
        <el-form-item :label="t('wms.outboundRouting.preview.outboundType')">
          <el-select v-model="preview.outboundType" style="width: 130px">
            <el-option :label="t('wms.outboundRouting.type.1')" :value="1" />
            <el-option :label="t('wms.outboundRouting.type.2')" :value="2" />
            <el-option :label="t('wms.outboundRouting.type.3')" :value="3" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('wms.outboundRouting.preview.fallback')">
          <el-input v-model="preview.fallbackWarehouseCd" clearable style="width: 120px" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :loading="previewLoading" @click="runPreview">
            {{ t('wms.outboundRouting.preview.btn') }}
          </el-button>
        </el-form-item>
      </el-form>
      <div v-if="previewResult.length" class="preview-result">
        <span class="preview-label">{{ t('wms.outboundRouting.preview.order') }}:</span>
        <el-tag
          v-for="(wh, idx) in previewResult"
          :key="wh + idx"
          :type="idx === 0 ? 'success' : 'info'"
          size="small"
          class="preview-tag"
        >
          {{ idx + 1 }}. {{ wh }}
        </el-tag>
      </div>
    </el-card>

    <!-- 作成／編集ダイアログ -->
    <el-dialog v-model="dialogVisible" :title="dialogTitle" width="560">
      <el-form :model="form" label-width="120px">
        <el-form-item :label="t('wms.outboundRouting.col.ruleName')" required>
          <el-input v-model="form.ruleName" maxlength="100" />
        </el-form-item>
        <el-form-item :label="t('wms.outboundRouting.col.sortOrder')">
          <el-input-number v-model="form.sortOrder" :min="0" :max="9999" />
          <span class="hint">{{ t('wms.outboundRouting.hint.sortOrder') }}</span>
        </el-form-item>
        <el-form-item :label="t('wms.outboundRouting.col.customerCd')">
          <el-input v-model="form.customerCd" clearable :placeholder="t('wms.outboundRouting.any')" maxlength="20" />
        </el-form-item>
        <el-form-item :label="t('wms.outboundRouting.col.productPrefix')">
          <el-input v-model="form.productCdPrefix" clearable :placeholder="t('wms.outboundRouting.any')" maxlength="20" />
        </el-form-item>
        <el-form-item :label="t('wms.outboundRouting.col.outboundType')">
          <el-select v-model="form.outboundType" clearable :placeholder="t('wms.outboundRouting.any')" style="width: 100%">
            <el-option :label="t('wms.outboundRouting.type.1')" :value="1" />
            <el-option :label="t('wms.outboundRouting.type.2')" :value="2" />
            <el-option :label="t('wms.outboundRouting.type.3')" :value="3" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('wms.outboundRouting.col.target')" required>
          <el-select v-model="form.targetWarehouseCd" filterable allow-create style="width: 100%">
            <el-option v-for="w in warehouses" :key="w.warehouseCd" :label="`${w.warehouseCd} ${w.warehouseName}`" :value="w.warehouseCd" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('wms.outboundRouting.col.enabled')">
          <el-switch v-model="form.enabled" />
        </el-form-item>
        <el-form-item :label="t('wms.outboundRouting.col.remarks')">
          <el-input v-model="form.remarks" type="textarea" :rows="2" maxlength="500" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false" :disabled="saving">{{ t('wms.outboundRouting.btn.cancel') }}</el-button>
        <el-button type="primary" :loading="saving" @click="submit">{{ t('wms.outboundRouting.btn.confirm') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { outboundRoutingApi } from '@/api/wms/outboundRouting'
import { warehouseApi } from '@/api/wms/warehouse'
import type { OutboundRoutingRule } from '@/types/wms/outboundRouting'

const { t } = useI18n()

const rows = ref<OutboundRoutingRule[]>([])
const warehouses = ref<{ warehouseCd: string; warehouseName: string }[]>([])
const loading = ref(false)
const saving = ref(false)

const dialogVisible = ref(false)
const editingId = ref<string | null>(null)
const dialogTitle = computed(() =>
  editingId.value ? t('wms.outboundRouting.dlg.editTitle') : t('wms.outboundRouting.dlg.createTitle'),
)

function emptyForm(): OutboundRoutingRule {
  return {
    ruleName: '',
    sortOrder: 100,
    customerCd: null,
    productCdPrefix: null,
    outboundType: null,
    targetWarehouseCd: '',
    enabled: true,
    remarks: null,
  }
}
const form = reactive<OutboundRoutingRule>(emptyForm())

const preview = reactive({ productCd: '', customerCd: '', outboundType: 2, fallbackWarehouseCd: '' })
const previewResult = ref<string[]>([])
const previewLoading = ref(false)

async function reload() {
  loading.value = true
  try {
    const res = await outboundRoutingApi.list(true)
    rows.value = (res?.data || []) as OutboundRoutingRule[]
  } finally {
    loading.value = false
  }
}

async function loadWarehouses() {
  try {
    const res = await warehouseApi.search({})
    warehouses.value = (res?.data || []) as any
  } catch {
    warehouses.value = []
  }
}

function openCreate() {
  editingId.value = null
  Object.assign(form, emptyForm())
  dialogVisible.value = true
}

function openEdit(row: OutboundRoutingRule) {
  editingId.value = row.id || null
  Object.assign(form, { ...emptyForm(), ...row })
  dialogVisible.value = true
}

async function submit() {
  if (!form.ruleName?.trim() || !form.targetWarehouseCd?.trim()) {
    ElMessage.warning(t('wms.outboundRouting.msg.required'))
    return
  }
  saving.value = true
  try {
    const payload: OutboundRoutingRule = {
      ...form,
      customerCd: form.customerCd || null,
      productCdPrefix: form.productCdPrefix || null,
      outboundType: form.outboundType ?? null,
      remarks: form.remarks || null,
    }
    if (editingId.value) {
      await outboundRoutingApi.update(editingId.value, payload)
      ElMessage.success(t('wms.outboundRouting.msg.updated'))
    } else {
      await outboundRoutingApi.create(payload)
      ElMessage.success(t('wms.outboundRouting.msg.created'))
    }
    dialogVisible.value = false
    await reload()
  } finally {
    saving.value = false
  }
}

async function remove(row: OutboundRoutingRule) {
  await ElMessageBox.confirm(
    t('wms.outboundRouting.msg.deleteConfirm', { name: row.ruleName }),
    t('wms.outboundRouting.btn.delete'),
    { type: 'warning' },
  )
  if (!row.id) return
  await outboundRoutingApi.remove(row.id)
  ElMessage.success(t('wms.outboundRouting.msg.deleted'))
  await reload()
}

async function runPreview() {
  if (!preview.productCd?.trim()) {
    ElMessage.warning(t('wms.outboundRouting.preview.needProduct'))
    return
  }
  previewLoading.value = true
  try {
    const res = await outboundRoutingApi.preview({
      productCd: preview.productCd,
      customerCd: preview.customerCd || undefined,
      outboundType: preview.outboundType,
      fallbackWarehouseCd: preview.fallbackWarehouseCd || undefined,
    })
    previewResult.value = (res?.data || []) as string[]
  } finally {
    previewLoading.value = false
  }
}

onMounted(() => {
  reload()
  loadWarehouses()
})
</script>

<style scoped>
.outbound-routing {
  padding: 12px;
}
.page-header {
  margin-bottom: 12px;
}
.page-header h2 {
  margin: 0;
  color: #303133;
  font-size: 20px;
  font-weight: 650;
}
.subtitle {
  color: #909399;
  font-size: 12px;
}
.table-toolbar {
  margin-bottom: 8px;
  display: flex;
  gap: 8px;
  align-items: center;
}
.preview-card {
  margin-top: 12px;
}
.preview-result {
  margin-top: 8px;
}
.preview-label {
  color: #606266;
  font-size: 13px;
  margin-right: 8px;
}
.preview-tag {
  margin-right: 6px;
}
.hint {
  color: #909399;
  font-size: 12px;
  margin-left: 8px;
}
</style>
