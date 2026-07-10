<!--
  出庫ルーティング設定 —— 查询列表页。CpPageShell(:count) + CpListPage(paginated=false、码值列 map)。
  候補倉庫プレビュー(設定検証ツール)と 作成/編集ダイアログ は複合フォームのため el-dialog/el-form 保持（token 化のみ）。
  変更後 listRef.reload()（:key 再マウント不使用）。
-->
<template>
  <CpPageShell :title="t('wms.outboundRouting.title')" :count="total">
    <template #actions>
      <el-button type="primary" @click="openCreate">{{ t('wms.outboundRouting.btn.create') }}</el-button>
      <el-button @click="listRef?.reload()">{{ t('wms.outboundRouting.btn.refresh') }}</el-button>
    </template>

    <p class="subtitle">{{ t('wms.outboundRouting.subtitle') }}</p>

    <CpListPage
      ref="listRef"
      :columns="columns"
      :fetch="fetchList"
      :paginated="false"
      @total-change="total = $event"
    >
      <template #col-_action="{ row }">
        <el-button link type="primary" size="small" @click="openEdit(row)">
          {{ t('wms.outboundRouting.btn.edit') }}
        </el-button>
        <el-button v-permission="'wms-outbound-routing:del'" link type="danger" size="small" @click="remove(row)">
          {{ t('wms.outboundRouting.btn.delete') }}
        </el-button>
      </template>
    </CpListPage>

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
        <CpTag
          v-for="(wh, idx) in previewResult"
          :key="wh + idx"
          :tone="idx === 0 ? 'ok' : 'info'"
          class="preview-tag"
        >
          {{ idx + 1 }}. {{ wh }}
        </CpTag>
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
  </CpPageShell>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import CpListPage, { type ListColumn, type ListFetch } from '@/components/templates/CpListPage.vue'
import CpTag from '@/components/base/CpTag.vue'
import { outboundRoutingApi } from '@/api/wms/outboundRouting'
import { warehouseApi } from '@/api/wms/warehouse'
import type { OutboundRoutingRule } from '@/types/wms/outboundRouting'

const { t } = useI18n()

const total = ref<number>()
const listRef = ref<InstanceType<typeof CpListPage>>()
const warehouses = ref<{ warehouseCd: string; warehouseName: string }[]>([])
const saving = ref(false)

const dialogVisible = ref(false)
const editingId = ref<string | null>(null)
const dialogTitle = computed(() =>
  editingId.value ? t('wms.outboundRouting.dlg.editTitle') : t('wms.outboundRouting.dlg.createTitle'),
)

// —— 列定义（customerCd/productCdPrefix/outboundType は any フォールバックを map で文案置換；enabled は kind:'tag'+map） ——
const columns = computed<ListColumn[]>(() => [
  { prop: 'sortOrder', label: t('wms.outboundRouting.col.sortOrder'), width: 90, kind: 'num' },
  { prop: 'ruleName', label: t('wms.outboundRouting.col.ruleName'), minWidth: 180, overflowTooltip: true },
  { prop: 'customerCd', label: t('wms.outboundRouting.col.customerCd'), width: 120,
    map: (v) => ({ label: v ? String(v) : t('wms.outboundRouting.any') }) },
  { prop: 'productCdPrefix', label: t('wms.outboundRouting.col.productPrefix'), width: 130,
    map: (v) => ({ label: v ? String(v) : t('wms.outboundRouting.any') }) },
  { prop: 'outboundType', label: t('wms.outboundRouting.col.outboundType'), width: 120,
    map: (v) => ({ label: v == null ? t('wms.outboundRouting.any') : t(`wms.outboundRouting.type.${v}`) }) },
  { prop: 'targetWarehouseCd', label: t('wms.outboundRouting.col.target'), width: 130, overflowTooltip: true },
  { prop: 'enabled', label: t('wms.outboundRouting.col.enabled'), width: 100, kind: 'tag',
    map: (v) => ({ label: v ? t('wms.outboundRouting.on') : t('wms.outboundRouting.off'), tone: v ? 'ok' : 'muted' }) },
  { prop: 'remarks', label: t('wms.outboundRouting.col.remarks'), minWidth: 160, overflowTooltip: true },
  { prop: '_action', label: t('wms.outboundRouting.col.action'), width: 150, fixed: 'right' },
])

// —— 取数：outboundRoutingApi.list(true) 扁平数组、无分页 → paginated=false 一次取全 ——
const fetchList: ListFetch = async () => {
  const res = await outboundRoutingApi.list(true)
  const all = (res?.data || []) as OutboundRoutingRule[]
  return { rows: all, total: all.length }
}

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
    listRef.value?.reload()
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
  listRef.value?.reload()
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

onMounted(loadWarehouses)
</script>

<style scoped>
.subtitle {
  color: var(--cp-muted);
  font-size: var(--cp-fs-xs);
  margin: -8px 0 0;
}
.preview-card {
  margin-top: 4px;
}
.preview-result {
  margin-top: 8px;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
}
.preview-label {
  color: var(--cp-muted);
  font-size: var(--cp-fs-sm);
  margin-right: 2px;
}
.hint {
  color: var(--cp-muted);
  font-size: var(--cp-fs-xs);
  margin-left: 8px;
}
</style>
