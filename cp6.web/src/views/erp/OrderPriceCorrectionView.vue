<template>
  <div class="price-correction">
    <!-- 検索条件 -->
    <el-card shadow="never" class="search-card">
      <el-form :model="query" label-width="100px" size="small" inline>
        <el-form-item :label="t('sales.term.base')" required>
          <el-input v-model="query.baseCd" style="width: 120px" :placeholder="t('sales.search.required')" />
        </el-form-item>
        <el-form-item :label="t('sales.term.customer') + ' ' + t('sales.search.from')">
          <el-input v-model="query.customerCd" style="width: 130px" />
        </el-form-item>
        <el-form-item :label="t('sales.term.customer') + ' ' + t('sales.search.to')">
          <el-input v-model="query.customerCdTo" style="width: 130px" />
        </el-form-item>
        <el-form-item :label="t('sales.term.orderDate') + ' ' + t('sales.search.from')">
          <el-date-picker v-model="query.orderDateFrom" type="date" value-format="YYYY-MM-DD" style="width: 150px" />
        </el-form-item>
        <el-form-item :label="t('sales.term.orderDate') + ' ' + t('sales.search.to')">
          <el-date-picker v-model="query.orderDateTo" type="date" value-format="YYYY-MM-DD" style="width: 150px" />
        </el-form-item>
        <el-form-item :label="t('sales.term.haibaiNo') + '1'">
          <el-input v-model="query.haibaiNo1" style="width: 150px" />
        </el-form-item>
        <el-form-item :label="t('sales.term.productCd')">
          <el-input v-model="query.productCd" style="width: 130px" />
        </el-form-item>
        <el-form-item :label="t('顧客品名')">
          <el-input v-model="query.customerItemName" style="width: 200px" />
        </el-form-item>
        <el-form-item :label="t('sales.term.qty') + ' FROM-TO'">
          <el-input-number v-model="query.qtyFrom" :precision="2" :controls="false" style="width: 110px" />
          〜
          <el-input-number v-model="query.qtyTo" :precision="2" :controls="false" style="width: 110px" />
        </el-form-item>
        <el-form-item :label="t('sales.term.amount') + ' FROM-TO'">
          <el-input-number v-model="query.amountFrom" :precision="0" :controls="false" style="width: 130px" />
          〜
          <el-input-number v-model="query.amountTo" :precision="0" :controls="false" style="width: 130px" />
        </el-form-item>
        <el-form-item>
          <el-checkbox v-model="query.onlyProvisional">{{ t('sales.pc.provisional') }}</el-checkbox>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="search" :loading="loading">{{ t('sales.btn.search') }}</el-button>
          <el-button @click="resetQuery">{{ t('sales.btn.clear') }}</el-button>
          <el-button type="success" @click="onSubmit" :loading="submitting" :disabled="selectedRows.length === 0">
            {{ t('sales.pc.updateSelected') }}（{{ selectedRows.length }}）
          </el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <!-- 結果 -->
    <el-card shadow="never">
      <div style="margin-bottom: 8px;">
        <el-tag size="small">合計 {{ total }} 件</el-tag>
        <el-tag v-if="selectedRows.length > 0" type="success" size="small" style="margin-left: 8px;">
          選択中 {{ selectedRows.length }} 件
        </el-tag>
      </div>
      <el-table :data="rows" border stripe size="small" style="width: 100%" max-height="600" @selection-change="onSelectionChange">
        <el-table-column type="selection" width="50" />
        <el-table-column prop="rowNo" label="No" width="60" align="center" />
        <el-table-column :label="t('状態')" width="80" align="center">
          <template #default="{ row }">
            <el-tag :type="approvalTagType(row.approvalStatus)" size="small">{{ approvalLabel(row.approvalStatus) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="customerCd" :label="t('得意先')" width="100" />
        <el-table-column prop="customerName" :label="t('得意先名')" width="160" />
        <el-table-column prop="salesPersonName" :label="t('売上担当')" width="120" />
        <el-table-column prop="orderSheetNo" :label="t('注文書NO')" width="120" />
        <el-table-column prop="haibaiNo1" :label="t('手配NO1')" width="140" />
        <el-table-column prop="productCd" :label="t('製品CD')" width="140" />
        <el-table-column prop="cpItemName1" :label="t('品名1')" min-width="160" />
        <el-table-column prop="orderDate" :label="t('受注日')" width="100" />
        <el-table-column prop="customerDeliveryDate" :label="t('客先納期')" width="100" />
        <el-table-column prop="quantity" :label="t('数量')" width="100" align="right" />
        <el-table-column prop="qtyUnit" :label="t('単位')" width="60" />
        <el-table-column prop="individualUnitPriceBefore" :label="t('個別単価(変更前)')" width="130" align="right" />
        <el-table-column prop="setUnitPriceBefore" :label="t('セット単価(変更前)')" width="130" align="right" />
        <el-table-column prop="unitPriceUnit" :label="t('単位')" width="60" />
        <el-table-column :label="t('個別単価(変更後)')" width="140">
          <template #default="{ row }">
            <el-input-number v-model="row.individualUnitPriceAfter" :disabled="!isRowSelected(row)" :precision="4" :controls="false" size="small" style="width: 130px" />
          </template>
        </el-table-column>
        <el-table-column :label="t('セット単価(変更後)')" width="140">
          <template #default="{ row }">
            <el-input-number v-model="row.setUnitPriceAfter" :disabled="!isRowSelected(row)" :precision="4" :controls="false" size="small" style="width: 130px" />
          </template>
        </el-table-column>
        <el-table-column :label="t('特値')" width="60" align="center">
          <template #default="{ row }">
            <el-checkbox v-model="row.specialPriceFlg" true-value="1" false-value="0" :disabled="!isRowSelected(row)" />
          </template>
        </el-table-column>
        <el-table-column :label="t('仮単価')" width="60" align="center">
          <template #default="{ row }">
            <el-icon v-if="row.provisionalPriceFlg" color="#e6a23c"><Warning /></el-icon>
          </template>
        </el-table-column>
        <el-table-column :label="t('単価変更理由')" min-width="180">
          <template #default="{ row }">
            <el-input v-model="row.priceChangeReason" :disabled="!isRowSelected(row)" size="small" />
          </template>
        </el-table-column>
        <el-table-column prop="amount" :label="t('金額')" width="130" align="right" />
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
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'

const { t } = useI18n()
import { Warning } from '@element-plus/icons-vue'
import { orderApi } from '@/api/erp/order'
import type {
  OrderPriceCorrectionQueryDto,
  OrderPriceCorrectionItemDto,
} from '@/types/erp/order'

const query = reactive<OrderPriceCorrectionQueryDto>({
  page: 1,
  pageSize: 100,
})
const rows = ref<OrderPriceCorrectionItemDto[]>([])
const total = ref(0)
const loading = ref(false)
const submitting = ref(false)
const selectedRows = ref<OrderPriceCorrectionItemDto[]>([])

function isRowSelected(row: OrderPriceCorrectionItemDto): boolean {
  return selectedRows.value.includes(row)
}

function onSelectionChange(sel: OrderPriceCorrectionItemDto[]) {
  selectedRows.value = sel
}

async function search() {
  if (!query.baseCd) {
    ElMessage.warning(t('sales.err.E10022') + ': ' + t('sales.term.base'))
    return
  }
  loading.value = true
  try {
    const res = await orderApi.searchPriceCorrection(query)
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
  selectedRows.value = []
}

async function onSubmit() {
  if (selectedRows.value.length === 0) {
    ElMessage.warning(t('sales.err.E10009'))
    return
  }
  try {
    await ElMessageBox.confirm(
      `${selectedRows.value.length} 件の単価訂正を実行します。よろしいですか？`,
      '確認', { type: 'warning' },
    )
  } catch { return }

  submitting.value = true
  try {
    const res = await orderApi.batchUpdatePrice({
      items: selectedRows.value.map(r => ({
        webOrderNo: r.webOrderNo,
        webOrderDetailNo: r.webOrderDetailNo,
        individualUnitPriceAfter: r.individualUnitPriceAfter,
        setUnitPriceAfter: r.setUnitPriceAfter,
        specialPriceFlg: r.specialPriceFlg,
        priceChangeReason: r.priceChangeReason,
        rowVersion: r.rowVersion,
      })),
    })
    if (res.code === 0 && res.data) {
      ElMessage.success(`更新: ${res.data.updatedCount} 件 / WF 起票: ${res.data.wfRequestedCount} 件`)
      if (res.data.conflictedKeys.length > 0) {
        ElMessage.warning(`競合: ${res.data.conflictedKeys.join(', ')}`)
      }
      await search()
      selectedRows.value = []
    }
  } catch { /* */ } finally {
    submitting.value = false
  }
}

function approvalLabel(s?: number): string {
  if (s === 1) return '承認依頼中'
  if (s === 9) return '承認済'
  return '未'
}
function approvalTagType(s?: number): 'info' | 'warning' | 'success' {
  if (s === 1) return 'warning'
  if (s === 9) return 'success'
  return 'info'
}
</script>

<style scoped>
.price-correction { padding: 16px; }
.search-card { margin-bottom: 12px; }
</style>
