<template>
  <div class="pur-pr">
    <div class="page-header">
      <h2>{{ t('采购申请') }}</h2>
      <span class="subtitle">{{ t('需求入口：手工/缺料/工单 → 送审 → PR转PO(有建议供应商)，无供应商行走询价') }}</span>
    </div>

    <el-card shadow="never">
      <div class="table-toolbar">
        <el-select v-model="status" size="small" style="width: 130px" clearable :placeholder="t('全部状态')" @change="reload">
          <el-option v-for="(lbl, k) in PR_STATUS_LABEL" :key="k" :value="Number(k)" :label="t(lbl)" />
        </el-select>
        <el-select v-model="source" size="small" style="width: 130px" clearable :placeholder="t('全部来源')" @change="reload">
          <el-option v-for="o in PR_SOURCE_OPTIONS" :key="o.value" :value="o.value" :label="t(o.label)" />
        </el-select>
        <el-button v-permission="'pur-pr:add'" type="primary" size="small" @click="openCreate">{{ t('新建申请') }}</el-button>
        <el-button size="small" @click="reload">{{ t('刷新') }}</el-button>
        <el-tag size="small" type="info">{{ t('共 {n} 条', { n: rows.length }) }}</el-tag>
      </div>

      <el-table :data="rows" border stripe size="small" max-height="620" v-loading="loading">
        <el-table-column prop="prNo" :label="t('申请单号')" width="160" />
        <el-table-column :label="t('申请日期')" width="110">
          <template #default="{ row }">{{ (row.requestDate || '').slice(0, 10) }}</template>
        </el-table-column>
        <el-table-column prop="requesterId" :label="t('申请人')" width="100" />
        <el-table-column :label="t('来源')" width="100" align="center">
          <template #default="{ row }">{{ t(PR_SOURCE_LABEL[row.source] || row.source || '') }}</template>
        </el-table-column>
        <el-table-column :label="t('行数')" width="70" align="center">
          <template #default="{ row }">{{ row.lines?.length || 0 }}</template>
        </el-table-column>
        <el-table-column :label="t('状态')" width="100" align="center">
          <template #default="{ row }">
            <el-tag :type="PR_STATUS_TAG[row.status] || 'info'" size="small">{{ t(PR_STATUS_LABEL[row.status] || '') }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column :label="t('操作')" width="200" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="openDetail(row)">{{ t('查看') }}</el-button>
            <el-button v-if="row.status === 0" v-permission="'pur-pr:submit'" link type="success" size="small" @click="doSubmit(row)">{{ t('送审') }}</el-button>
            <el-button v-if="row.status === 2" v-permission="'pur-pr:convert'" link type="warning" size="small" @click="doConvert(row)">{{ t('转采购订单') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- 建单 -->
    <el-dialog v-model="createVisible" :title="t('新建申请')" width="860">
      <el-form :model="form" label-width="90px" size="small">
        <el-row :gutter="12">
          <el-col :span="8"><el-form-item :label="t('申请日期')"><el-date-picker v-model="form.requestDate" type="date" value-format="YYYY-MM-DD" style="width:100%" /></el-form-item></el-col>
          <el-col :span="16"><el-form-item :label="t('备注')"><el-input v-model="form.remarks" maxlength="500" /></el-form-item></el-col>
        </el-row>

        <div class="lines-head">
          <span>{{ t('明细行') }}</span>
          <el-button link type="primary" size="small" @click="addLine">{{ t('添加行') }}</el-button>
        </div>
        <el-table :data="form.lines" border size="small">
          <el-table-column type="index" :label="t('行')" width="44" />
          <el-table-column :label="t('物料')" min-width="150">
            <template #default="{ row }"><el-input v-model="row.itemId" size="small" maxlength="40" /></template>
          </el-table-column>
          <el-table-column :label="t('数量')" width="120">
            <template #default="{ row }"><el-input-number v-model="row.qty" :min="0" size="small" controls-position="right" style="width:100%" /></template>
          </el-table-column>
          <el-table-column :label="t('单位')" width="80">
            <template #default="{ row }"><el-input v-model="row.unitCd" size="small" maxlength="10" /></template>
          </el-table-column>
          <el-table-column :label="t('要求交期')" width="140">
            <template #default="{ row }"><el-date-picker v-model="row.requiredDate" type="date" size="small" value-format="YYYY-MM-DD" style="width:100%" /></template>
          </el-table-column>
          <el-table-column :label="t('估价')" width="120">
            <template #default="{ row }"><el-input-number v-model="row.estPrice" :min="0" :precision="4" size="small" controls-position="right" style="width:100%" /></template>
          </el-table-column>
          <el-table-column :label="t('建议供应商')" width="130">
            <template #default="{ row }"><el-input v-model="row.suggestSupplierId" size="small" maxlength="20" :placeholder="t('留空走询价')" /></template>
          </el-table-column>
          <el-table-column :label="t('操作')" width="56">
            <template #default="{ $index }"><el-button link type="danger" size="small" @click="form.lines.splice($index, 1)">{{ t('删') }}</el-button></template>
          </el-table-column>
        </el-table>
        <div class="hint">{{ t('有建议供应商的行转PO时按供应商分组拆单；留空的行留待询价(RFQ)比价') }}</div>
      </el-form>
      <template #footer>
        <el-button @click="createVisible = false" :disabled="saving">{{ t('取消') }}</el-button>
        <el-button type="primary" :loading="saving" @click="submit">{{ t('确定') }}</el-button>
      </template>
    </el-dialog>

    <!-- 明细 -->
    <el-dialog v-model="detailVisible" :title="t('采购申请') + ' ' + (detail?.prNo || '')" width="900">
      <template v-if="detail">
        <el-descriptions :column="3" size="small" border>
          <el-descriptions-item :label="t('申请单号')">{{ detail.prNo }}</el-descriptions-item>
          <el-descriptions-item :label="t('申请人')">{{ detail.requesterId }}</el-descriptions-item>
          <el-descriptions-item :label="t('申请日期')">{{ (detail.requestDate || '').slice(0, 10) }}</el-descriptions-item>
          <el-descriptions-item :label="t('来源')">{{ t(PR_SOURCE_LABEL[detail.source!] || detail.source || '') }}</el-descriptions-item>
          <el-descriptions-item :label="t('来源单号')">{{ detail.sourceRefNo || '—' }}</el-descriptions-item>
          <el-descriptions-item :label="t('状态')"><el-tag :type="PR_STATUS_TAG[detail.status!] || 'info'" size="small">{{ t(PR_STATUS_LABEL[detail.status!] || '') }}</el-tag></el-descriptions-item>
          <el-descriptions-item :label="t('备注')" :span="3">{{ detail.remarks || '—' }}</el-descriptions-item>
        </el-descriptions>
        <el-table :data="detail.lines" border size="small" style="margin-top: 10px">
          <el-table-column prop="lineNo" :label="t('行')" width="46" />
          <el-table-column prop="itemId" :label="t('物料')" min-width="130" show-overflow-tooltip />
          <el-table-column prop="qty" :label="t('数量')" width="80" align="right" />
          <el-table-column prop="unitCd" :label="t('单位')" width="64" align="center" />
          <el-table-column :label="t('要求交期')" width="110">
            <template #default="{ row }">{{ (row.requiredDate || '').slice(0, 10) }}</template>
          </el-table-column>
          <el-table-column prop="estPrice" :label="t('估价')" width="90" align="right" />
          <el-table-column :label="t('建议供应商')" width="120">
            <template #default="{ row }">{{ row.suggestSupplierId || t('待询价') }}</template>
          </el-table-column>
          <el-table-column :label="t('已转PO')" width="140">
            <template #default="{ row }">{{ row.convertedPoNo || '—' }}</template>
          </el-table-column>
        </el-table>
      </template>
      <template #footer>
        <el-button v-if="detail?.status === 0" v-permission="'pur-pr:submit'" type="success" size="small" @click="doSubmit(detail!)">{{ t('送审') }}</el-button>
        <el-button v-if="detail?.status === 2" v-permission="'pur-pr:convert'" type="warning" size="small" @click="doConvert(detail!)">{{ t('转采购订单') }}</el-button>
        <el-button size="small" @click="detailVisible = false">{{ t('关闭') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { prApi } from '@/api/pur/pur'
import {
  PR_STATUS_LABEL, PR_STATUS_TAG, PR_SOURCE_LABEL, PR_SOURCE_OPTIONS,
  type PurchaseRequest, type PrCreateForm, type PrLineCreateForm,
} from '@/types/pur/pur'

const { t } = useI18n()

const rows = ref<PurchaseRequest[]>([])
const loading = ref(false)
const saving = ref(false)
const status = ref<number | undefined>(undefined)
const source = ref<string | undefined>(undefined)
const createVisible = ref(false)
const detailVisible = ref(false)
const detail = ref<PurchaseRequest | null>(null)

function emptyLine(): PrLineCreateForm {
  return { itemId: '', qty: 1, unitCd: '', requiredDate: null, estPrice: null, suggestSupplierId: null }
}
function emptyForm(): PrCreateForm {
  return { requestDate: new Date().toISOString().slice(0, 10), remarks: '', lines: [emptyLine()] }
}
const form = reactive<PrCreateForm>(emptyForm())

async function reload() {
  loading.value = true
  try {
    const res = await prApi.list(status.value, source.value)
    rows.value = res?.data || []
  } finally {
    loading.value = false
  }
}

function openCreate() {
  Object.assign(form, emptyForm())
  form.lines = [emptyLine()]
  createVisible.value = true
}
function addLine() { form.lines.push(emptyLine()) }

async function submit() {
  if (form.lines.length === 0 || form.lines.some(l => !l.itemId?.trim() || (l.qty ?? 0) <= 0)) {
    ElMessage.warning(t('每行需填物料且数量大于0')); return
  }
  saving.value = true
  try {
    await prApi.create({ ...form })
    ElMessage.success(t('已新建'))
    createVisible.value = false
    await reload()
  } finally {
    saving.value = false
  }
}

async function openDetail(row: PurchaseRequest) {
  if (!row.prNo) return
  const res = await prApi.get(row.prNo)
  detail.value = res?.data || null
  detailVisible.value = true
}

async function doSubmit(row: PurchaseRequest) {
  if (!row.prNo) return
  await prApi.submit(row.prNo)
  ElMessage.success(t('已送审'))
  detailVisible.value = false
  await reload()
}

async function doConvert(row: PurchaseRequest) {
  if (!row.prNo) return
  await ElMessageBox.confirm(t('将有建议供应商的行按供应商分组转为采购订单？'), t('提示'), { type: 'warning' })
  const res = await prApi.convert(row.prNo)
  ElMessage.success(t('已转 {n} 张采购订单', { n: res?.data?.length || 0 }))
  detailVisible.value = false
  await reload()
}

onMounted(reload)
</script>

<style scoped>
.pur-pr { padding: 16px; }
.page-header { margin-bottom: 12px; }
.page-header h2 { margin: 0; color: #303133; font-size: 20px; font-weight: 650; }
.subtitle { color: #909399; font-size: 12px; }
.table-toolbar { margin-bottom: 8px; display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
.lines-head { display: flex; justify-content: space-between; align-items: center; margin: 8px 0; font-weight: 600; }
.hint { color: #909399; font-size: 12px; margin-top: 6px; }
</style>
