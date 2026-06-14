<template>
  <div class="order-list">
    <!-- 検索条件 -->
    <el-card shadow="never" class="search-card">
      <el-form :model="query" label-width="100px" size="small" inline>
        <el-form-item :label="t('sales.term.base')">
          <el-input v-model="query.baseCd" style="width: 120px" />
        </el-form-item>
        <el-form-item :label="t('sales.term.customer') + ' ' + t('sales.search.from')">
          <el-input v-model="query.customerCd" style="width: 130px" />
        </el-form-item>
        <el-form-item :label="t('sales.term.customer') + ' ' + t('sales.search.to')">
          <el-input v-model="query.customerCdTo" style="width: 130px" />
        </el-form-item>
        <el-form-item :label="t('sales.term.orderType')">
          <el-input v-model="query.orderType" style="width: 100px" />
        </el-form-item>
        <el-form-item :label="t('sales.term.orderDate') + ' ' + t('sales.search.from')">
          <el-date-picker v-model="query.orderDateFrom" type="date" value-format="YYYY-MM-DD" style="width: 150px" />
        </el-form-item>
        <el-form-item :label="t('sales.term.orderDate') + ' ' + t('sales.search.to')">
          <el-date-picker v-model="query.orderDateTo" type="date" value-format="YYYY-MM-DD" style="width: 150px" />
        </el-form-item>
        <el-form-item :label="t('sales.term.deliveryDate') + ' ' + t('sales.search.from')">
          <el-date-picker v-model="query.deliveryDateFrom" type="date" value-format="YYYY-MM-DD" style="width: 150px" />
        </el-form-item>
        <el-form-item :label="t('sales.term.deliveryDate') + ' ' + t('sales.search.to')">
          <el-date-picker v-model="query.deliveryDateTo" type="date" value-format="YYYY-MM-DD" style="width: 150px" />
        </el-form-item>

        <el-collapse v-model="advancedOpen" style="width: 100%">
          <el-collapse-item :title="t('sales.section.advSearch')" name="adv">
            <el-form-item :label="t('sales.term.haibaiNo') + '1 ' + t('sales.search.from')">
              <el-input v-model="query.haibaiNo1From" style="width: 150px" />
            </el-form-item>
            <el-form-item :label="t('sales.term.haibaiNo') + '1 ' + t('sales.search.to')">
              <el-input v-model="query.haibaiNo1To" style="width: 150px" />
            </el-form-item>
            <el-form-item :label="t('sales.term.orderSheet')">
              <el-input v-model="query.orderSheetNo" style="width: 150px" />
            </el-form-item>
            <el-form-item :label="t('sales.term.productCd')">
              <el-input v-model="query.productCd" style="width: 150px" />
            </el-form-item>
            <el-form-item :label="t('顧客品名')">
              <el-input v-model="query.customerItemName" style="width: 200px" />
            </el-form-item>
            <el-form-item :label="t('シート段')">
              <el-input v-model="query.sheetFlute" style="width: 100px" />
            </el-form-item>
            <el-form-item :label="t('原紙CD')">
              <el-input v-model="query.paperCd" style="width: 130px" />
            </el-form-item>
            <el-form-item :label="t('印刷CD')">
              <el-input v-model="query.printCd" style="width: 130px" />
            </el-form-item>
            <el-form-item :label="t('エンボスCD')">
              <el-input v-model="query.embossCd" style="width: 130px" />
            </el-form-item>
            <el-form-item :label="t('メーカCD')">
              <el-input v-model="query.makerCd" style="width: 130px" />
            </el-form-item>
            <el-form-item :label="t('運送会社')">
              <el-input v-model="query.carrier" style="width: 130px" />
            </el-form-item>
            <el-form-item>
              <el-checkbox v-model="query.onlyConsignedSales">{{ t('sales.order.consignedSale') }}</el-checkbox>
              <el-checkbox v-model="query.onlyMcUntransferred">{{ t('sales.order.mcUntransferred') }}</el-checkbox>
            </el-form-item>
          </el-collapse-item>
        </el-collapse>

        <el-form-item>
          <el-button type="primary" @click="search" :loading="loading">{{ t('sales.btn.search') }}</el-button>
          <el-button @click="resetQuery">{{ t('sales.btn.clear') }}</el-button>
          <el-button :icon="Download" @click="exportCsv" :loading="exporting">{{ t('sales.btn.exportCsv') }}</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <!-- 結果 -->
    <el-card shadow="never">
      <div style="margin-bottom: 8px;">
        <el-tag size="small">{{ t('sales.list.totalCount', { n: total }) }}</el-tag>
      </div>

      <!-- 桌面端：完整表格 -->
      <el-table v-if="!isMobile" :data="rows" border stripe size="small" style="width: 100%" max-height="600" @sort-change="onSortChange">
        <el-table-column prop="rowNo" :label="t('sales.list.no')" width="60" align="center" />
        <el-table-column prop="customerCd" :label="t('sales.term.customer')" width="100" sortable="custom" />
        <el-table-column prop="customerName" :label="t('sales.term.customer') + t('sales.term.bpName').slice(-1)" width="160" />
        <el-table-column prop="salesPersonName" :label="t('sales.term.staff')" width="120" />
        <el-table-column prop="orderSheetNo" :label="t('注文書NO')" width="120" />
        <el-table-column prop="haibaiNo1" :label="t('手配NO1')" width="140" sortable="custom" />
        <el-table-column prop="defectiveHaibaiNo" :label="t('不適合手配NO')" width="140" sortable="custom" />
        <el-table-column prop="mcOrderNo" :label="t('注文NO(mc)')" width="140" sortable="custom" />
        <el-table-column prop="orderDate" :label="t('受注日')" width="100" />
        <el-table-column prop="customerDeliveryDate" :label="t('客先納期')" width="100" sortable="custom" />
        <el-table-column prop="productCd" :label="t('製品CD')" width="140" sortable="custom" />
        <el-table-column prop="cpItemOrComposition" :label="t('CP品名/構成')" min-width="180" />
        <el-table-column prop="sheetFlute" :label="t('段')" width="60" sortable="custom" />
        <el-table-column prop="compositionF" :label="t('表(構成)')" width="140" />
        <el-table-column prop="compositionC" :label="t('中(構成)')" width="140" />
        <el-table-column prop="compositionB" :label="t('裏(構成)')" width="140" />
        <el-table-column prop="quantity" :label="t('数量')" width="100" align="right" sortable="custom" />
        <el-table-column prop="qtyUnit" :label="t('単位')" width="60" />
        <el-table-column prop="individualUnitPrice" :label="t('個別単価')" width="120" align="right" sortable="custom" />
        <el-table-column prop="setUnitPrice" :label="t('セット単価')" width="120" align="right" sortable="custom" />
        <el-table-column prop="amount" :label="t('受注金額')" width="130" align="right" sortable="custom" />
        <el-table-column :label="t('通貨')" width="110" align="center">
          <template #default="{ row }">
            <span>{{ row.currencyCd || 'JPY' }}</span>
            <span v-if="row.currencyCd && row.currencyCd !== 'JPY'" class="fx-rate-hint"> @{{ row.fxRate }}</span>
          </template>
        </el-table-column>
        <el-table-column :label="t('預り売上')" width="80" align="center">
          <template #default="{ row }">
            <el-icon v-if="row.consignedSalesFlg === '1'" color="#67c23a"><Check /></el-icon>
          </template>
        </el-table-column>
        <el-table-column prop="slipNote" :label="t('伝票備考')" min-width="160" sortable="custom" />
        <el-table-column :label="t('操作')" width="230" align="center" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="goDetail(row)">詳細</el-button>
            <el-button
              link
              type="primary"
              size="small"
              :icon="Connection"
              @click.stop="goTrace(row.webOrderNo)"
            >
              {{ t('erp.orderTrace.btn.trace') }}
            </el-button>
            <el-button
              link
              type="danger"
              size="small"
              @click.stop="openCancelDialog(row)"
            >
              {{ t('sales.cancel.btn') }}
            </el-button>
          </template>
        </el-table-column>
      </el-table>

      <!-- 手机端：卡片列表 -->
      <div v-else class="order-card-list">
        <el-empty v-if="!rows.length && !loading" :image-size="80" :description="t('sales.err.E10008')" />
        <div
          v-for="row in rows"
          :key="row.webOrderNo"
          class="order-card"
          @click="goDetail(row)"
        >
          <div class="order-card-head">
            <div class="order-card-customer">{{ row.customerName || row.customerCd }}</div>
            <div class="order-card-amount">{{ formatCurrency(row.amount) }}</div>
          </div>
          <div class="order-card-row">
            <span class="lbl">注文書NO</span>
            <span class="val">{{ row.orderSheetNo || '—' }}</span>
          </div>
          <div class="order-card-row">
            <span class="lbl">手配NO1</span>
            <span class="val">{{ row.haibaiNo1 || '—' }}</span>
          </div>
          <div class="order-card-row">
            <span class="lbl">受注日 / 納期</span>
            <span class="val">{{ row.orderDate }} → {{ row.customerDeliveryDate }}</span>
          </div>
          <div class="order-card-row">
            <span class="lbl">製品CD</span>
            <span class="val">{{ row.productCd || '—' }}</span>
          </div>
          <div class="order-card-row">
            <span class="lbl">数量</span>
            <span class="val">{{ row.quantity }} {{ row.qtyUnit }}</span>
          </div>
          <div v-if="row.consignedSalesFlg === '1'" class="order-card-flag">
            <el-tag type="success" size="small">預り売上</el-tag>
          </div>
          <div class="order-card-actions" @click.stop>
            <el-button link type="primary" size="small" :icon="Connection" @click="goTrace(row.webOrderNo)">
              {{ t('erp.orderTrace.btn.trace') }}
            </el-button>
          </div>
        </div>
      </div>

      <el-pagination
        v-model:current-page="query.page"
        v-model:page-size="query.pageSize"
        :total="total"
        :page-sizes="[50, 100, 200]"
        :layout="isMobile ? 'prev, pager, next' : 'total, sizes, prev, pager, next, jumper'"
        :pager-count="isMobile ? 5 : 7"
        :small="isMobile"
        background
        style="margin-top: 12px; justify-content: flex-end"
        @current-change="search"
        @size-change="search"
      />
    </el-card>

    <!-- Phase 6 受注取消ダイアログ -->
    <OrderCancelDialog
      v-if="cancelDialogVisible"
      v-model="cancelDialogVisible"
      :web-order-no="cancelTargetWebOrderNo"
      @cancelled="onCancelled"
    />
  </div>
</template>

<script setup lang="ts">
import { ref, reactive } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { Download, Check, Connection } from '@element-plus/icons-vue'
import { orderApi } from '@/api/erp/order'
import type { OrderQueryDto, OrderListItemDto } from '@/types/erp/order'
import { useBreakpoint } from '@/composables/useBreakpoint'
import { useFormat } from '@/utils/format'
import OrderCancelDialog from './OrderCancelDialog.vue'

const { t } = useI18n()
const router = useRouter()
const { isMobile } = useBreakpoint()
// i18n 优化 P2 样例：金额走 locale 感知的多币种格式化（默认 JPY）。
const { formatCurrency } = useFormat()

const query = reactive<OrderQueryDto>({
  page: 1,
  pageSize: 100,
  sortField: '',
  sortOrder: '',
})
const advancedOpen = ref<string[]>([])
const rows = ref<OrderListItemDto[]>([])
const total = ref(0)
const loading = ref(false)
const exporting = ref(false)

// Phase 6 取消ダイアログ
const cancelDialogVisible = ref(false)
const cancelTargetWebOrderNo = ref('')

function openCancelDialog(row: OrderListItemDto) {
  cancelTargetWebOrderNo.value = row.webOrderNo
  cancelDialogVisible.value = true
}

function onCancelled() {
  ElMessage.success(t('sales.cancel.successMsg'))
  // 重新查询列表
  search()
}

async function search() {
  loading.value = true
  try {
    const res = await orderApi.searchList(query)
    if (res.code === 0 && res.data) {
      rows.value = res.data.rows
      total.value = res.data.total
      if (rows.value.length === 0) ElMessage.info(t('sales.err.E10008'))
    }
  } catch { /* */ } finally {
    loading.value = false
  }
}

function resetQuery() {
  Object.keys(query).forEach(k => {
    if (k !== 'page' && k !== 'pageSize') (query as any)[k] = undefined
  })
  query.page = 1
  rows.value = []
  total.value = 0
}

async function exportCsv() {
  exporting.value = true
  try {
    const blob = await orderApi.exportListCsv(query) as unknown as Blob
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `orders_${new Date().toISOString().slice(0, 10)}.csv`
    a.click()
    URL.revokeObjectURL(url)
  } catch { /* */ } finally {
    exporting.value = false
  }
}

function onSortChange({ prop, order }: { prop: string; order: string | null }) {
  query.sortField = order ? prop : ''
  query.sortOrder = order === 'ascending' ? 'asc' : order === 'descending' ? 'desc' : ''
  query.page = 1
  search()
}

function goDetail(row: OrderListItemDto) {
  router.push({ path: '/order', query: { webOrderNo: row.webOrderNo } })
}

function goTrace(webOrderNo: string) {
  router.push({ path: '/erp/order-trace', query: { webOrderNo } })
}
</script>

<style scoped>
.order-list { padding: 16px; }
.search-card { margin-bottom: 12px; }

/* 移动卡片 */
.order-card-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.order-card {
  background: #fff;
  border: 1px solid #ebeef5;
  border-radius: 10px;
  padding: 14px;
  cursor: pointer;
  transition: box-shadow 0.15s ease;
  position: relative;
}
.order-card:active {
  box-shadow: 0 2px 8px rgba(64,158,255,0.15);
}
.order-card-head {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  margin-bottom: 10px;
  padding-bottom: 8px;
  border-bottom: 1px dashed #ebeef5;
  gap: 8px;
}
.order-card-customer {
  font-weight: 600;
  font-size: 15px;
  color: #303133;
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.order-card-amount {
  font-weight: 600;
  font-size: 16px;
  color: #f56c6c;
  flex-shrink: 0;
}
.order-card-row {
  display: flex;
  gap: 8px;
  font-size: 13px;
  padding: 3px 0;
  line-height: 1.5;
}
.order-card-row .lbl {
  color: #909399;
  min-width: 88px;
  flex-shrink: 0;
}
.order-card-row .val {
  color: #303133;
  flex: 1;
  word-break: break-all;
  font-size: 13px;
}
.order-card-flag {
  margin-top: 8px;
}

.order-card-actions {
  display: flex;
  justify-content: flex-end;
  margin-top: 8px;
}

@media (max-width: 767px) {
  .order-list { padding: 12px; }
  .search-card :deep(.el-card__body) {
    padding: 12px;
  }
}
</style>
