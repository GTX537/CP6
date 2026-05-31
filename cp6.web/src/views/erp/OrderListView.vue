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
            <el-form-item label="顧客品名">
              <el-input v-model="query.customerItemName" style="width: 200px" />
            </el-form-item>
            <el-form-item label="シート段">
              <el-input v-model="query.sheetFlute" style="width: 100px" />
            </el-form-item>
            <el-form-item label="原紙CD">
              <el-input v-model="query.paperCd" style="width: 130px" />
            </el-form-item>
            <el-form-item label="印刷CD">
              <el-input v-model="query.printCd" style="width: 130px" />
            </el-form-item>
            <el-form-item label="エンボスCD">
              <el-input v-model="query.embossCd" style="width: 130px" />
            </el-form-item>
            <el-form-item label="メーカCD">
              <el-input v-model="query.makerCd" style="width: 130px" />
            </el-form-item>
            <el-form-item label="運送会社">
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
      <el-table v-if="!isMobile" :data="rows" border stripe size="small" style="width: 100%" max-height="600">
        <el-table-column prop="rowNo" :label="t('sales.list.no')" width="60" align="center" />
        <el-table-column prop="customerCd" :label="t('sales.term.customer')" width="100" />
        <el-table-column prop="customerName" :label="t('sales.term.customer') + t('sales.term.bpName').slice(-1)" width="160" />
        <el-table-column prop="salesPersonName" :label="t('sales.term.staff')" width="120" />
        <el-table-column prop="orderSheetNo" label="注文書NO" width="120" />
        <el-table-column prop="haibaiNo1" label="手配NO1" width="140" />
        <el-table-column prop="defectiveHaibaiNo" label="不適合手配NO" width="140" />
        <el-table-column prop="mcOrderNo" label="注文NO(mc)" width="140" />
        <el-table-column prop="orderDate" label="受注日" width="100" />
        <el-table-column prop="customerDeliveryDate" label="客先納期" width="100" />
        <el-table-column prop="productCd" label="製品CD" width="140" />
        <el-table-column prop="cpItemOrComposition" label="CP品名/構成" min-width="180" />
        <el-table-column prop="sheetFlute" label="段" width="60" />
        <el-table-column prop="compositionF" label="表(構成)" width="140" />
        <el-table-column prop="compositionC" label="中(構成)" width="140" />
        <el-table-column prop="compositionB" label="裏(構成)" width="140" />
        <el-table-column prop="quantity" label="数量" width="100" align="right" />
        <el-table-column prop="qtyUnit" label="単位" width="60" />
        <el-table-column prop="individualUnitPrice" label="個別単価" width="120" align="right" />
        <el-table-column prop="setUnitPrice" label="セット単価" width="120" align="right" />
        <el-table-column prop="amount" label="受注金額" width="130" align="right" />
        <el-table-column label="預り売上" width="80" align="center">
          <template #default="{ row }">
            <el-icon v-if="row.consignedSalesFlg === '1'" color="#67c23a"><Check /></el-icon>
          </template>
        </el-table-column>
        <el-table-column prop="slipNote" label="伝票備考" min-width="160" />
        <el-table-column label="操作" width="100" align="center" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="goDetail(row)">詳細</el-button>
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
            <div class="order-card-amount">¥{{ formatAmount(row.amount) }}</div>
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
  </div>
</template>

<script setup lang="ts">
import { ref, reactive } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { Download, Check } from '@element-plus/icons-vue'
import { orderApi } from '@/api/order'
import type { OrderQueryDto, OrderListItemDto } from '@/types/order'
import { useBreakpoint } from '@/composables/useBreakpoint'

const { t } = useI18n()
const router = useRouter()
const { isMobile } = useBreakpoint()

function formatAmount(v: any): string {
  if (v == null || v === '') return '0'
  const n = Number(v)
  if (!isFinite(n)) return String(v)
  return n.toLocaleString()
}

const query = reactive<OrderQueryDto>({
  page: 1,
  pageSize: 100,
})
const advancedOpen = ref<string[]>([])
const rows = ref<OrderListItemDto[]>([])
const total = ref(0)
const loading = ref(false)
const exporting = ref(false)

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

function goDetail(row: OrderListItemDto) {
  router.push({ path: '/order', query: { webOrderNo: row.webOrderNo } })
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

@media (max-width: 767px) {
  .order-list { padding: 12px; }
  .search-card :deep(.el-card__body) {
    padding: 12px;
  }
}
</style>
