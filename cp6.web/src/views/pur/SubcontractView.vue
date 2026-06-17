<template>
  <div class="pur-sub">
    <div class="page-header">
      <h2>{{ t('外注加工') }}</h2>
      <span class="subtitle">{{ t('外注闭环：外注PO(加工费)→发支給材(IssuedQty防吞料)→收成品成本(加工费+料)→防吞料对账') }}</span>
    </div>

    <el-card shadow="never">
      <div class="table-toolbar">
        <el-button size="small" @click="reload">{{ t('刷新') }}</el-button>
        <el-tag size="small" type="info">{{ t('共 {n} 条', { n: rows.length }) }}</el-tag>
        <span class="hint">{{ t('仅列外注委托单(Type=2)') }}</span>
      </div>

      <el-table :data="rows" border stripe size="small" max-height="620" v-loading="loading">
        <el-table-column prop="poNo" :label="t('采购订单号')" width="170" />
        <el-table-column prop="supplierId" :label="t('外协厂')" width="110" />
        <el-table-column prop="supplierName" :label="t('外协厂名')" min-width="120" show-overflow-tooltip />
        <el-table-column :label="t('订单日期')" width="110">
          <template #default="{ row }">{{ (row.orderDate || '').slice(0, 10) }}</template>
        </el-table-column>
        <el-table-column :label="t('成品行数')" width="90" align="center">
          <template #default="{ row }">{{ row.lines?.length || 0 }}</template>
        </el-table-column>
        <el-table-column :label="t('操作')" width="120" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="openWork(row)">{{ t('外注作业') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- 外注作业 -->
    <el-dialog v-model="workVisible" :title="t('外注作业') + ' ' + (current?.poNo || '')" width="940" top="5vh">
      <template v-if="current">
        <el-form inline size="small" class="line-pick">
          <el-form-item :label="t('选择成品行')">
            <el-select v-model="lineNo" style="width: 360px" @change="onLineChange">
              <el-option
                v-for="l in current.lines" :key="l.lineNo" :value="l.lineNo!"
                :label="`#${l.lineNo} ${l.itemId} ×${l.qty} @${t('加工费单价')}${l.unitPrice}`" />
            </el-select>
          </el-form-item>
        </el-form>

        <el-tabs v-model="tab" v-if="lineNo">
          <!-- 支給材 + 发料 -->
          <el-tab-pane :label="t('支給材')" name="consign">
            <div class="lines-head">
              <span>{{ t('登记支給材') }}</span>
              <el-button link type="primary" size="small" @click="addConsignRow">{{ t('添加行') }}</el-button>
            </div>
            <el-table :data="consignForm" border size="small">
              <el-table-column type="index" :label="t('行')" width="44" />
              <el-table-column :label="t('支給材物料')" min-width="150">
                <template #default="{ row }"><el-input v-model="row.consignItemId" size="small" maxlength="40" /></template>
              </el-table-column>
              <el-table-column :label="t('应发数量')" width="130">
                <template #default="{ row }"><el-input-number v-model="row.consignQty" :min="0" size="small" controls-position="right" style="width:100%" /></template>
              </el-table-column>
              <el-table-column :label="t('单位成本')" width="130">
                <template #default="{ row }"><el-input-number v-model="row.consignUnitCost" :min="0" :precision="4" size="small" controls-position="right" style="width:100%" /></template>
              </el-table-column>
              <el-table-column :label="t('操作')" width="56">
                <template #default="{ $index }"><el-button link type="danger" size="small" @click="consignForm.splice($index, 1)">{{ t('删') }}</el-button></template>
              </el-table-column>
            </el-table>
            <div class="row-actions">
              <el-button type="primary" size="small" :loading="saving" @click="saveConsign">{{ t('保存支給材') }}</el-button>
              <el-button type="success" size="small" :loading="saving" :disabled="!hasConsign" @click="issueAll">{{ t('一次发齐剩余') }}</el-button>
            </div>

            <div class="lines-head"><span>{{ t('已登记支給材(实发追踪)') }}</span></div>
            <el-table :data="consigns" border size="small" empty-text="—">
              <el-table-column prop="consignItemId" :label="t('支給材物料')" min-width="130" show-overflow-tooltip />
              <el-table-column prop="consignQty" :label="t('应发')" width="100" align="right" />
              <el-table-column prop="consignUnitCost" :label="t('单位成本')" width="100" align="right" />
              <el-table-column prop="issuedQty" :label="t('已发')" width="100" align="right" />
              <el-table-column :label="t('剩余')" width="100" align="right">
                <template #default="{ row }">{{ (row.consignQty - (row.issuedQty || 0)).toFixed(2) }}</template>
              </el-table-column>
              <el-table-column prop="wmsIssueNo" :label="t('WMS出库单号')" min-width="140" show-overflow-tooltip />
              <el-table-column :label="t('分批发料')" width="200">
                <template #default="{ row }">
                  <div class="batch-cell">
                    <el-input-number v-model="batchQty[row.consignItemId]" :min="0" size="small" controls-position="right" style="width:110px" />
                    <el-button link type="primary" size="small" @click="issueBatch(row)">{{ t('发料') }}</el-button>
                  </div>
                </template>
              </el-table-column>
            </el-table>
          </el-tab-pane>

          <!-- 成品成本核算 -->
          <el-tab-pane :label="t('成品成本核算')" name="cost">
            <el-form inline size="small">
              <el-form-item :label="t('收成品数')"><el-input-number v-model="finishedQty" :min="0" size="small" controls-position="right" /></el-form-item>
              <el-form-item><el-button type="primary" size="small" :loading="saving" @click="calcCost">{{ t('核算成品成本') }}</el-button></el-form-item>
            </el-form>
            <div class="hint">{{ t('成品成本=加工费(PO单价×成品数)+支給材成本(并入)，接财务成本会计') }}</div>
            <el-descriptions v-if="cost" :column="2" size="small" border style="margin-top:10px">
              <el-descriptions-item :label="t('加工费')">{{ cost.processingFee }}</el-descriptions-item>
              <el-descriptions-item :label="t('支給材成本')">{{ cost.consignCost }}</el-descriptions-item>
              <el-descriptions-item :label="t('成品成本')"><b>{{ cost.finishedCost }}</b></el-descriptions-item>
              <el-descriptions-item :label="t('成本凭证')">{{ cost.costVoucherNo || '—' }}</el-descriptions-item>
            </el-descriptions>
          </el-tab-pane>

          <!-- 防吞料对账 -->
          <el-tab-pane :label="t('防吞料对账')" name="reconcile">
            <el-form inline size="small">
              <el-form-item :label="t('收成品数')"><el-input-number v-model="recQty" :min="0" size="small" controls-position="right" /></el-form-item>
              <el-form-item :label="t('损耗容差(%)')"><el-input-number v-model="recTolPct" :min="0" :max="100" :precision="1" size="small" controls-position="right" /></el-form-item>
              <el-form-item><el-button type="warning" size="small" :loading="saving" @click="doReconcile">{{ t('对账') }}</el-button></el-form-item>
            </el-form>
            <el-alert v-if="reconcile" :type="reconcile.hasAnomaly ? 'error' : 'success'" :closable="false" show-icon style="margin:8px 0"
              :title="reconcile.hasAnomaly ? t('发现支給材超损耗容差，已挂起核查') : t('对账正常，无吞料')" />
            <el-table v-if="reconcile" :data="reconcile.lines" border size="small">
              <el-table-column prop="consignItemId" :label="t('支給材物料')" min-width="130" show-overflow-tooltip />
              <el-table-column prop="issuedQty" :label="t('实发')" width="100" align="right" />
              <el-table-column prop="expectedQty" :label="t('应耗')" width="100" align="right" />
              <el-table-column prop="variance" :label="t('差异')" width="100" align="right" />
              <el-table-column prop="allowedVariance" :label="t('容许差异')" width="100" align="right" />
              <el-table-column :label="t('是否异常')" width="100" align="center">
                <template #default="{ row }">
                  <el-tag :type="row.isAnomaly ? 'danger' : 'success'" size="small">{{ row.isAnomaly ? t('异常') : t('正常') }}</el-tag>
                </template>
              </el-table-column>
            </el-table>
          </el-tab-pane>
        </el-tabs>
      </template>
      <template #footer>
        <el-button size="small" @click="workVisible = false">{{ t('关闭') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { subcontractApi } from '@/api/pur/pur'
import type {
  PurchaseOrder, PoConsignMaterial, ConsignMaterialForm,
  SubcontractCostResult, ConsignReconcileResult,
} from '@/types/pur/pur'

const { t } = useI18n()

const rows = ref<PurchaseOrder[]>([])
const loading = ref(false)
const saving = ref(false)

const workVisible = ref(false)
const current = ref<PurchaseOrder | null>(null)
const lineNo = ref<number | undefined>(undefined)
const tab = ref('consign')

const consignForm = ref<ConsignMaterialForm[]>([])
const consigns = ref<PoConsignMaterial[]>([])
const batchQty = ref<Record<string, number>>({})

const finishedQty = ref<number>(0)
const cost = ref<SubcontractCostResult | null>(null)

const recQty = ref<number>(0)
const recTolPct = ref<number>(5)
const reconcile = ref<ConsignReconcileResult | null>(null)

const hasConsign = computed(() => consigns.value.length > 0)

async function reload() {
  loading.value = true
  try {
    const res = await subcontractApi.listOrders()
    rows.value = res?.data || []
  } finally {
    loading.value = false
  }
}

function openWork(row: PurchaseOrder) {
  current.value = row
  lineNo.value = row.lines?.[0]?.lineNo
  tab.value = 'consign'
  resetLineState()
  workVisible.value = true
  if (lineNo.value) void loadConsigns()
}

function resetLineState() {
  consignForm.value = []
  consigns.value = []
  batchQty.value = {}
  cost.value = null
  reconcile.value = null
  finishedQty.value = 0
  recQty.value = 0
}

async function onLineChange() {
  resetLineState()
  if (lineNo.value) await loadConsigns()
}

async function loadConsigns() {
  if (!current.value?.poNo || !lineNo.value) return
  const res = await subcontractApi.getConsign(current.value.poNo, lineNo.value)
  consigns.value = res?.data || []
}

function addConsignRow() {
  consignForm.value.push({ consignItemId: '', consignQty: 0, consignUnitCost: 0 })
}

async function saveConsign() {
  if (!current.value?.poNo || !lineNo.value) return
  const items = consignForm.value.filter(c => c.consignItemId?.trim() && c.consignQty > 0)
  if (items.length === 0) { ElMessage.warning(t('请填写支給材物料与应发数量')); return }
  saving.value = true
  try {
    await subcontractApi.addConsign(current.value.poNo, lineNo.value, items)
    ElMessage.success(t('已登记 {n} 项支給材', { n: items.length }))
    consignForm.value = []
    await loadConsigns()
  } finally {
    saving.value = false
  }
}

async function issueAll() {
  if (!current.value?.poNo || !lineNo.value) return
  saving.value = true
  try {
    await subcontractApi.issue(current.value.poNo, lineNo.value, null)
    ElMessage.success(t('已发料'))
    await loadConsigns()
  } finally {
    saving.value = false
  }
}

async function issueBatch(row: PoConsignMaterial) {
  if (!current.value?.poNo || !lineNo.value) return
  const qty = batchQty.value[row.consignItemId] || 0
  if (qty <= 0) { ElMessage.warning(t('本次发料量须大于0')); return }
  saving.value = true
  try {
    await subcontractApi.issue(current.value.poNo, lineNo.value, [{ consignItemId: row.consignItemId, qty }])
    ElMessage.success(t('已发料'))
    batchQty.value[row.consignItemId] = 0
    await loadConsigns()
  } finally {
    saving.value = false
  }
}

async function calcCost() {
  if (!current.value?.poNo || !lineNo.value) return
  if (finishedQty.value <= 0) { ElMessage.warning(t('请填收成品数')); return }
  saving.value = true
  try {
    const res = await subcontractApi.finishedCost(current.value.poNo, lineNo.value, finishedQty.value)
    cost.value = res?.data || null
  } finally {
    saving.value = false
  }
}

async function doReconcile() {
  if (!current.value?.poNo || !lineNo.value) return
  if (recQty.value <= 0) { ElMessage.warning(t('请填收成品数')); return }
  saving.value = true
  try {
    const res = await subcontractApi.reconcile(current.value.poNo, lineNo.value, recQty.value, recTolPct.value / 100)
    reconcile.value = res?.data || null
  } finally {
    saving.value = false
  }
}

onMounted(reload)
</script>

<style scoped>
.pur-sub { padding: 16px; }
.page-header { margin-bottom: 12px; }
.page-header h2 { margin: 0; color: #303133; font-size: 20px; font-weight: 650; }
.subtitle { color: #909399; font-size: 12px; }
.table-toolbar { margin-bottom: 8px; display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
.line-pick { margin-bottom: 4px; }
.lines-head { display: flex; justify-content: space-between; align-items: center; margin: 10px 0 6px; font-weight: 600; }
.row-actions { margin: 8px 0; display: flex; gap: 8px; }
.batch-cell { display: flex; gap: 6px; align-items: center; }
.hint { color: #909399; font-size: 12px; margin-top: 6px; }
</style>
