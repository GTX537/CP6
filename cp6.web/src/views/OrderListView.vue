<template>
  <div class="order-list">
    <!-- 検索条件 -->
    <el-card shadow="never" class="search-card">
      <el-form :model="query" label-width="100px" size="small" inline>
        <el-form-item label="拠点">
          <el-input v-model="query.baseCd" style="width: 120px" />
        </el-form-item>
        <el-form-item label="得意先 FROM">
          <el-input v-model="query.customerCd" style="width: 130px" />
        </el-form-item>
        <el-form-item label="得意先 TO">
          <el-input v-model="query.customerCdTo" style="width: 130px" />
        </el-form-item>
        <el-form-item label="受注区分">
          <el-input v-model="query.orderType" style="width: 100px" />
        </el-form-item>
        <el-form-item label="受注日 FROM">
          <el-date-picker v-model="query.orderDateFrom" type="date" value-format="YYYY-MM-DD" style="width: 150px" />
        </el-form-item>
        <el-form-item label="受注日 TO">
          <el-date-picker v-model="query.orderDateTo" type="date" value-format="YYYY-MM-DD" style="width: 150px" />
        </el-form-item>
        <el-form-item label="客先納期 FROM">
          <el-date-picker v-model="query.deliveryDateFrom" type="date" value-format="YYYY-MM-DD" style="width: 150px" />
        </el-form-item>
        <el-form-item label="客先納期 TO">
          <el-date-picker v-model="query.deliveryDateTo" type="date" value-format="YYYY-MM-DD" style="width: 150px" />
        </el-form-item>

        <el-collapse v-model="advancedOpen" style="width: 100%">
          <el-collapse-item title="詳細検索" name="adv">
            <el-form-item label="手配NO1 FROM">
              <el-input v-model="query.haibaiNo1From" style="width: 150px" />
            </el-form-item>
            <el-form-item label="手配NO1 TO">
              <el-input v-model="query.haibaiNo1To" style="width: 150px" />
            </el-form-item>
            <el-form-item label="注文書NO">
              <el-input v-model="query.orderSheetNo" style="width: 150px" />
            </el-form-item>
            <el-form-item label="製品CD">
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
              <el-checkbox v-model="query.onlyConsignedSales">預り売上のみ</el-checkbox>
              <el-checkbox v-model="query.onlyMcUntransferred">mc未転送のみ</el-checkbox>
            </el-form-item>
          </el-collapse-item>
        </el-collapse>

        <el-form-item>
          <el-button type="primary" @click="search" :loading="loading">検索</el-button>
          <el-button @click="resetQuery">クリア</el-button>
          <el-button :icon="Download" @click="exportCsv" :loading="exporting">CSV出力</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <!-- 結果 -->
    <el-card shadow="never">
      <div style="margin-bottom: 8px;">
        <el-tag size="small">合計 {{ total }} 件</el-tag>
      </div>
      <el-table :data="rows" border stripe size="small" style="width: 100%" max-height="600">
        <el-table-column prop="rowNo" label="No" width="60" align="center" />
        <el-table-column prop="customerCd" label="得意先" width="100" />
        <el-table-column prop="customerName" label="得意先名" width="160" />
        <el-table-column prop="salesPersonName" label="担当者" width="120" />
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

      <el-pagination
        v-model:current-page="query.page"
        v-model:page-size="query.pageSize"
        :total="total"
        :page-sizes="[50, 100, 200]"
        layout="total, sizes, prev, pager, next, jumper"
        style="margin-top: 12px; justify-content: flex-end"
        @current-change="search"
        @size-change="search"
      />
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { Download, Check } from '@element-plus/icons-vue'
import { orderApi } from '@/api/order'
import type { OrderQueryDto, OrderListItemDto } from '@/types/order'

const router = useRouter()

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
      if (rows.value.length === 0) ElMessage.info('E10008: 検索結果がありません')
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
</style>
