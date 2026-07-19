<template>
  <div class="pur-rfq">
    <div class="page-header">
      <h2>{{ t('询价比价') }}</h2>
      <span class="subtitle">{{ t('价格发现：从PR发起→邀供应商→收报价→按行比价→选定→回写价表→转PO') }}</span>
    </div>

    <el-card shadow="never">
      <div class="table-toolbar">
        <el-select v-model="status" size="small" style="width: 130px" clearable :placeholder="t('全部状态')" @change="reload">
          <el-option v-for="(lbl, k) in RFQ_STATUS_LABEL" :key="k" :value="Number(k)" :label="t(lbl)" />
        </el-select>
        <el-divider direction="vertical" />
        <el-input v-model="fromPrNo" size="small" style="width: 160px" :placeholder="t('采购申请号')" clearable />
        <el-button v-permission="'pur-rfq:add'" type="primary" size="small" @click="doCreateFromPr">{{ t('从PR发起询价') }}</el-button>
        <el-button size="small" @click="reload">{{ t('刷新') }}</el-button>
        <el-tag size="small" type="info">{{ t('共 {n} 条', { n: rows.length }) }}</el-tag>
      </div>

      <el-table :data="rows" border stripe size="small" max-height="620" v-loading="loading">
        <el-table-column prop="rfqNo" :label="t('询价单号')" width="160" />
        <el-table-column :label="t('询价日期')" width="110">
          <template #default="{ row }">{{ (row.rfqDate || '').slice(0, 10) }}</template>
        </el-table-column>
        <el-table-column prop="buyer" :label="t('询价员')" width="100" />
        <el-table-column prop="sourcePrNo" :label="t('来源PR')" width="150" />
        <el-table-column :label="t('行/供应商')" width="100" align="center">
          <template #default="{ row }">{{ (row.lines?.length || 0) }} / {{ (row.suppliers?.length || 0) }}</template>
        </el-table-column>
        <el-table-column :label="t('状态')" width="100" align="center">
          <template #default="{ row }">
            <el-tag :type="RFQ_STATUS_TAG[row.status] || 'info'" size="small">{{ t(RFQ_STATUS_LABEL[row.status] || '') }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column :label="t('操作')" width="120" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="openDetail(row)">{{ t('比价台') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- 比价台（详情 + 全生命周期操作） -->
    <el-dialog v-model="detailVisible" :title="t('比价台') + ' ' + (detail?.rfqNo || '')" width="960" top="5vh">
      <template v-if="detail">
        <el-descriptions :column="4" size="small" border>
          <el-descriptions-item :label="t('询价单号')">{{ detail.rfqNo }}</el-descriptions-item>
          <el-descriptions-item :label="t('询价员')">{{ detail.buyer }}</el-descriptions-item>
          <el-descriptions-item :label="t('来源PR')">{{ detail.sourcePrNo }}</el-descriptions-item>
          <el-descriptions-item :label="t('状态')">
            <el-tag :type="RFQ_STATUS_TAG[detail.status!] || 'info'" size="small">{{ t(RFQ_STATUS_LABEL[detail.status!] || '') }}</el-tag>
          </el-descriptions-item>
        </el-descriptions>

        <!-- 操作条 -->
        <div class="action-bar">
        <el-button v-permission="'pur-rfq:invite'" size="small" @click="openInvite">{{ t('邀请供应商') }}</el-button>
        <el-button v-permission="'pur-rfq:quote'" size="small" :disabled="!detail.suppliers?.length" @click="openQuote">{{ t('录入报价') }}</el-button>
        <el-button v-permission="'pur-rfq:rank'" size="small" type="primary" :disabled="!detail.quotes?.length" @click="doRank">{{ t('比价排名') }}</el-button>
        <el-button v-permission="'pur-rfq:select'" size="small" type="success" :disabled="!hasPicks" @click="doSelect">{{ t('确认选定') }}</el-button>
        <el-button v-permission="'pur-rfq:writeback'" size="small" :disabled="!hasSelected" @click="doWriteBack">{{ t('回写价表') }}</el-button>
        <el-button v-permission="'pur-rfq:convert'" size="small" type="warning" :disabled="!hasSelected" @click="doConvert">{{ t('转采购订单') }}</el-button>
        </div>

        <!-- 被邀供应商 -->
        <div class="sec-title">{{ t('被邀供应商') }}</div>
        <div class="sup-tags">
          <el-tag v-for="s in detail.suppliers" :key="s.supplierId" size="small" :type="s.inviteStatus === 2 ? 'success' : 'info'" effect="plain">
            {{ s.supplierName || s.supplierId }} · {{ t(RFQ_INVITE_LABEL[s.inviteStatus || 0] || '') }}
          </el-tag>
          <span v-if="!detail.suppliers?.length" class="empty">{{ t('暂无') }}</span>
        </div>

        <!-- 比价矩阵：行 × 供应商，单元格=价/交期/名次 + 选定单选 -->
        <div class="sec-title">{{ t('比价矩阵') }}</div>
        <el-table :data="detail.lines" border size="small" max-height="360">
          <el-table-column prop="lineNo" :label="t('行')" width="46" fixed />
          <el-table-column prop="itemId" :label="t('物料')" width="120" fixed show-overflow-tooltip />
          <el-table-column prop="qty" :label="t('数量')" width="80" align="right" fixed />
          <el-table-column v-for="s in detail.suppliers" :key="s.supplierId" :label="s.supplierName || s.supplierId" min-width="140" align="center">
            <template #default="{ row }">
              <div v-if="quoteOf(row.lineNo, s.supplierId)" class="cell-quote" :class="{ best: quoteOf(row.lineNo, s.supplierId)!.rank === 1, expired: isExpired(quoteOf(row.lineNo, s.supplierId)!) }">
                <el-radio :model-value="picks[row.lineNo]" :value="s.supplierId" :disabled="isExpired(quoteOf(row.lineNo, s.supplierId)!)" @change="picks[row.lineNo] = s.supplierId">
                  <span class="price">{{ quoteOf(row.lineNo, s.supplierId)!.quotedPrice }}</span>
                </el-radio>
                <div class="meta">
                  <el-tag v-if="quoteOf(row.lineNo, s.supplierId)!.rank" :type="quoteOf(row.lineNo, s.supplierId)!.rank === 1 ? 'success' : 'info'" size="small" effect="plain">#{{ quoteOf(row.lineNo, s.supplierId)!.rank }}</el-tag>
                  <span v-if="quoteOf(row.lineNo, s.supplierId)!.leadDays != null" class="lead">{{ t('交期{d}天', { d: quoteOf(row.lineNo, s.supplierId)!.leadDays }) }}</span>
                  <span v-if="isExpired(quoteOf(row.lineNo, s.supplierId)!)" class="exp">{{ t('已过期') }}</span>
                </div>
              </div>
              <span v-else class="empty">—</span>
            </template>
          </el-table-column>
        </el-table>
        <div class="hint">{{ t('名次仅为建议（价格优先→交期），最终由人按行选定；过期报价不可选') }}</div>
      </template>
    </el-dialog>

    <!-- 邀请供应商 -->
    <el-dialog v-model="inviteVisible" :title="t('邀请供应商')" width="480">
      <el-form label-width="90px" size="small">
        <el-form-item :label="t('供应商')">
          <el-input v-model="inviteCodes" type="textarea" :rows="3" :placeholder="t('多个发注先编码用逗号或换行分隔')" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="inviteVisible = false" :disabled="saving">{{ t('取消') }}</el-button>
        <el-button type="primary" :loading="saving" @click="submitInvite">{{ t('确定') }}</el-button>
      </template>
    </el-dialog>

    <!-- 录入报价 -->
    <el-dialog v-model="quoteVisible" :title="t('录入报价')" width="720">
      <el-form label-width="90px" size="small">
        <el-form-item :label="t('供应商')" required>
          <el-select v-model="quoteSupplier" style="width: 260px" :placeholder="t('选择被邀供应商')">
            <el-option v-for="s in detail?.suppliers || []" :key="s.supplierId" :value="s.supplierId" :label="s.supplierName || s.supplierId" />
          </el-select>
        </el-form-item>
      </el-form>
      <el-table :data="quoteLines" border size="small">
        <el-table-column prop="lineNo" :label="t('行')" width="46" />
        <el-table-column prop="itemId" :label="t('物料')" width="110" show-overflow-tooltip />
        <el-table-column :label="t('报价单价')" width="150">
          <template #default="{ row }"><el-input-number v-model="row.quotedPrice" :min="0" :precision="4" size="small" controls-position="right" style="width:100%" /></template>
        </el-table-column>
        <el-table-column :label="t('交期(天)')" width="120">
          <template #default="{ row }"><el-input-number v-model="row.leadDays" :min="0" size="small" controls-position="right" style="width:100%" /></template>
        </el-table-column>
        <el-table-column :label="t('报价有效期')" min-width="160">
          <template #default="{ row }"><el-date-picker v-model="row.validUntil" type="date" size="small" value-format="YYYY-MM-DD" style="width:100%" /></template>
        </el-table-column>
      </el-table>
      <div class="hint">{{ t('只填要报价的行；有效期过期的报价不能被选定转PO') }}</div>
      <template #footer>
        <el-button @click="quoteVisible = false" :disabled="saving">{{ t('取消') }}</el-button>
        <el-button type="primary" :loading="saving" @click="submitQuote">{{ t('提交报价') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { rfqApi } from '@/api/pur/pur'
import {
  RFQ_STATUS_LABEL, RFQ_STATUS_TAG, RFQ_INVITE_LABEL,
  type Rfq, type RfqQuote, type RfqQuoteLineForm,
} from '@/types/pur/pur'

const { t } = useI18n()

const rows = ref<Rfq[]>([])
const loading = ref(false)
const saving = ref(false)
const status = ref<number | undefined>(undefined)
const fromPrNo = ref('')

const detailVisible = ref(false)
const detail = ref<Rfq | null>(null)
const picks = reactive<Record<number, string>>({}) // 行 → 选定供应商

const inviteVisible = ref(false)
const inviteCodes = ref('')

const quoteVisible = ref(false)
const quoteSupplier = ref('')
const quoteLines = ref<(RfqQuoteLineForm & { itemId?: string })[]>([])

const hasPicks = computed(() => Object.keys(picks).length > 0)
const hasSelected = computed(() => (detail.value?.quotes || []).some(q => q.isSelected))

function quoteOf(lineNo: number, supplierId: string): RfqQuote | undefined {
  return (detail.value?.quotes || []).find(q => q.lineNo === lineNo && q.supplierId === supplierId)
}
function isExpired(q: RfqQuote): boolean {
  return !!q.validUntil && new Date(q.validUntil) < new Date(new Date().toDateString())
}

async function reload() {
  loading.value = true
  try {
    const res = await rfqApi.list(status.value)
    rows.value = res?.data || []
  } finally {
    loading.value = false
  }
}

async function doCreateFromPr() {
  if (!fromPrNo.value?.trim()) { ElMessage.warning(t('请先填采购申请号')); return }
  const res = await rfqApi.createFromPr(fromPrNo.value.trim())
  ElMessage.success(t('已发起询价 {no}', { no: res?.data?.rfqNo || '' }))
  fromPrNo.value = ''
  await reload()
  if (res?.data?.rfqNo) await openDetailByNo(res.data.rfqNo)
}

async function openDetail(row: Rfq) {
  if (row.rfqNo) await openDetailByNo(row.rfqNo)
}
async function openDetailByNo(rfqNo: string) {
  const res = await rfqApi.get(rfqNo)
  detail.value = res?.data || null
  // 用已选中报价回填 picks（便于继续操作）
  Object.keys(picks).forEach(k => delete picks[Number(k)])
  for (const q of detail.value?.quotes || []) if (q.isSelected) picks[q.lineNo] = q.supplierId
  detailVisible.value = true
}
async function refreshDetail() {
  if (detail.value?.rfqNo) await openDetailByNo(detail.value.rfqNo)
  await reload()
}

// 邀请供应商
function openInvite() { inviteCodes.value = ''; inviteVisible.value = true }
async function submitInvite() {
  const codes = inviteCodes.value.split(/[,，\s]+/).map(s => s.trim()).filter(Boolean)
  if (codes.length === 0) { ElMessage.warning(t('请填写至少一个供应商')); return }
  saving.value = true
  try {
    await rfqApi.addSuppliers(detail.value!.rfqNo!, codes)
    ElMessage.success(t('已邀请'))
    inviteVisible.value = false
    await refreshDetail()
  } finally {
    saving.value = false
  }
}

// 录入报价
function openQuote() {
  quoteSupplier.value = ''
  quoteLines.value = (detail.value?.lines || []).map(l => ({
    lineNo: l.lineNo!, itemId: l.itemId, quotedPrice: 0, leadDays: undefined as any, currencyCd: null, validUntil: null,
  }))
  quoteVisible.value = true
}
async function submitQuote() {
  if (!quoteSupplier.value) { ElMessage.warning(t('请选择供应商')); return }
  const lines = quoteLines.value.filter(l => (l.quotedPrice ?? 0) > 0)
    .map(l => ({ lineNo: l.lineNo, quotedPrice: l.quotedPrice, leadDays: l.leadDays ?? null, currencyCd: l.currencyCd ?? null, validUntil: l.validUntil ?? null }))
  if (lines.length === 0) { ElMessage.warning(t('请至少为一行填报价')); return }
  saving.value = true
  try {
    await rfqApi.recordQuote(detail.value!.rfqNo!, quoteSupplier.value, lines)
    ElMessage.success(t('报价已录入'))
    quoteVisible.value = false
    await refreshDetail()
  } finally {
    saving.value = false
  }
}

async function doRank() {
  await rfqApi.rank(detail.value!.rfqNo!)
  ElMessage.success(t('已比价'))
  await refreshDetail()
}

async function doSelect() {
  const selections = Object.entries(picks).map(([lineNo, supplierId]) => ({ lineNo: Number(lineNo), supplierId }))
  if (selections.length === 0) { ElMessage.warning(t('请先在矩阵中按行选定')); return }
  await rfqApi.select(detail.value!.rfqNo!, selections)
  ElMessage.success(t('已选定'))
  await refreshDetail()
}

async function doWriteBack() {
  await rfqApi.writeBack(detail.value!.rfqNo!)
  ElMessage.success(t('已回写采购价表'))
  await refreshDetail()
}

async function doConvert() {
  await ElMessageBox.confirm(t('将选中报价按供应商分组转为采购订单？'), t('提示'), { type: 'warning' })
  const res = await rfqApi.convert(detail.value!.rfqNo!)
  ElMessage.success(t('已转 {n} 张采购订单', { n: res?.data?.length || 0 }))
  await refreshDetail()
}

onMounted(reload)
</script>

<style scoped>
.pur-rfq { padding: 16px; }
.page-header { margin-bottom: 12px; }
.page-header h2 { margin: 0; color: #303133; font-size: 20px; font-weight: 650; }
.subtitle { color: #909399; font-size: 12px; }
.table-toolbar { margin-bottom: 8px; display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
.action-bar { margin: 12px 0; display: flex; gap: 8px; flex-wrap: wrap; }
.sec-title { font-weight: 600; margin: 10px 0 6px; color: #303133; }
.sup-tags { display: flex; gap: 6px; flex-wrap: wrap; }
.empty { color: #c0c4cc; }
.cell-quote { display: flex; flex-direction: column; align-items: center; gap: 2px; }
.cell-quote.best { background: #f0f9eb; border-radius: 4px; }
.cell-quote.expired { opacity: 0.5; }
.cell-quote .price { font-weight: 600; }
.cell-quote .meta { display: flex; gap: 4px; align-items: center; font-size: 11px; color: #909399; }
.cell-quote .exp { color: #f56c6c; }
.hint { color: #909399; font-size: 12px; margin-top: 6px; }
</style>
