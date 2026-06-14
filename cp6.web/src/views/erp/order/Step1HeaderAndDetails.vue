<template>
  <el-card shadow="never">
    <!-- ============ ヘッダー（基本情報） ============ -->
    <el-form
      :model="store.order"
      label-width="110px"
      :disabled="store.isPageReadOnly"
      size="small"
      inline
    >
      <el-form-item :label="t('受注区分')">
        <el-select v-model="store.order.orderType" :disabled="!store.canEdit" :placeholder="t('区分')" style="width: 160px">
          <el-option :label="t('加工製品(10)')" value="10" />
          <el-option :label="t('シート受注(20)')" value="20" />
          <el-option :label="t('商品(30)')" value="30" />
          <el-option :label="t('原紙(40)')" value="40" />
          <el-option :label="t('購買品(50)')" value="50" />
          <el-option :label="t('経費品(60)')" value="60" />
          <el-option :label="t('版型(70)')" value="70" />
          <el-option :label="t('輪切り(80)')" value="80" />
          <el-option :label="t('有償支給(90)')" value="90" />
        </el-select>
      </el-form-item>
      <el-form-item :label="t('得意先 CD')">
        <el-input v-model="store.order.customerCd" :disabled="!store.canEdit" style="width: 200px">
          <template #append>
            <el-button :icon="Search" :disabled="!store.canEdit" @click="customerPickerVisible = true" />
          </template>
        </el-input>
      </el-form-item>
      <el-form-item :label="t('受注部門')">
        <el-input v-model="store.order.orderDepartment" :disabled="!store.canEdit" style="width: 140px" />
      </el-form-item>
      <el-form-item :label="t('受注日')">
        <el-date-picker v-model="store.order.orderDate" type="date" :disabled="!store.canEdit" value-format="YYYY-MM-DD" style="width: 150px" />
      </el-form-item>
      <el-form-item :label="t('客先納期')">
        <el-date-picker v-model="store.order.customerDeliveryDate" type="date" :disabled="!store.canEdit" value-format="YYYY-MM-DD" style="width: 150px" />
      </el-form-item>
      <el-form-item :label="t('数量')">
        <el-input-number v-model="store.order.quantity" :disabled="!store.canEdit" :precision="2" style="width: 120px" />
      </el-form-item>
      <el-form-item :label="t('注文書 NO')">
        <el-input v-model="store.order.orderSheetNo" :disabled="!store.canEdit" style="width: 160px" />
      </el-form-item>
      <el-form-item :label="t('先方担当')">
        <el-input v-model="store.order.customerContact" :disabled="!store.canEdit" style="width: 140px" />
      </el-form-item>
      <el-form-item :label="t('宛名')">
        <el-input v-model="store.order.addressee" :disabled="!store.canEdit" style="width: 180px" />
      </el-form-item>
      <el-form-item :label="t('運送会社')">
        <el-input v-model="store.order.carrier" :disabled="!store.canEdit" style="width: 140px" />
      </el-form-item>
      <el-form-item :label="t('出荷日時')">
        <el-input v-model="store.order.shipDateTime" placeholder="YYYY/MM/DD HH:MM" :disabled="!store.canEdit" style="width: 180px" />
      </el-form-item>
      <el-form-item :label="t('出荷条件')">
        <el-input v-model="store.order.shipCondition" :disabled="!store.canEdit" style="width: 140px" />
      </el-form-item>
      <el-form-item :label="t('売価区分')">
        <el-select v-model="store.order.salesPriceDiv" :disabled="!store.canEdit" style="width: 140px">
          <el-option :label="t('個別単価')" value="1" />
          <el-option :label="t('セット単価')" value="2" />
        </el-select>
      </el-form-item>
    </el-form>

    <el-divider content-position="left">{{ t('受注明細（部材一覧）') }}</el-divider>

    <!-- ============ ツールバー ============ -->
    <div style="margin-bottom: 8px; display: flex; gap: 8px; align-items: center; flex-wrap: wrap;">
      <el-input v-model="memberProductCd" :placeholder="t('製品 CD で部材引入')" clearable style="width: 220px" size="small" :disabled="!store.canEdit">
        <template #append>
          <el-button :icon="Search" :disabled="!store.canEdit" @click="onLookupMembers">引入</el-button>
        </template>
      </el-input>
      <el-button-group>
        <el-button type="primary" :icon="Plus" size="small" :disabled="!store.canEdit" @click="store.addDetail()">行追加</el-button>
        <el-button :icon="Delete" size="small" :disabled="!store.canEdit || !selectedRowNo" @click="onRemove">行削除</el-button>
        <el-button :icon="CopyDocument" size="small" :disabled="!canCopy" @click="onCopy">行コピー</el-button>
        <el-button :icon="Top" size="small" :disabled="!canMoveUp" @click="onMove('up')">▲</el-button>
        <el-button :icon="Bottom" size="small" :disabled="!canMoveDown" @click="onMove('down')">▼</el-button>
        <el-button :icon="RefreshLeft" size="small" :disabled="!store.canEdit" @click="onClear">クリア</el-button>
      </el-button-group>
    </div>

    <el-table
      :data="store.order.details"
      border stripe
      highlight-current-row
      style="width: 100%"
      size="small"
      @current-change="onCurrentRowChange"
    >
      <el-table-column prop="webOrderDetailNo" label="No" width="60" align="center" />
      <el-table-column prop="haibaiNo1" :label="t('手配NO1')" width="160" />
      <el-table-column prop="productCd" :label="t('製品 CD')" width="140">
        <template #default="{ row }">
          <el-input v-model="row.productCd" :disabled="!store.canEdit" size="small">
            <template #append>
              <el-button :icon="Search" @click="onPickProduct(row)" :disabled="!store.canEdit" />
            </template>
          </el-input>
        </template>
      </el-table-column>
      <el-table-column :label="t('製品名')" min-width="180">
        <template #default="{ row }">
          <el-input v-model="row.cpItemName1" :disabled="!store.canEdit" size="small" />
        </template>
      </el-table-column>
      <el-table-column :label="t('単位')" width="80">
        <template #default="{ row }">
          <el-input v-model="row.qtyUnit" :disabled="!store.canEdit" size="small" />
        </template>
      </el-table-column>
      <el-table-column :label="t('数量')" width="100">
        <template #default="{ row }">
          <el-input-number v-model="row.quantity" :disabled="!store.canEdit" :precision="2" :controls="false" size="small" style="width: 95px" @change="onQtyOrPriceChange(row)" />
        </template>
      </el-table-column>
      <el-table-column :label="t('特値')" width="60" align="center">
        <template #default="{ row }">
          <el-checkbox v-model="row.specialPriceFlg" true-value="1" false-value="0" :disabled="!store.canEdit" />
        </template>
      </el-table-column>
      <el-table-column :label="t('個別単価')" width="120">
        <template #default="{ row }">
          <el-input-number v-model="row.individualUnitPrice" :disabled="!store.canEdit || row.specialPriceFlg !== '1'" :precision="4" :controls="false" size="small" style="width: 115px" @change="onQtyOrPriceChange(row)" />
        </template>
      </el-table-column>
      <el-table-column :label="t('セット単価')" width="120">
        <template #default="{ row }">
          <el-input-number v-model="row.setUnitPrice" :disabled="!store.canEdit || row.specialPriceFlg !== '1'" :precision="4" :controls="false" size="small" style="width: 115px" @change="onQtyOrPriceChange(row)" />
        </template>
      </el-table-column>
      <el-table-column :label="t('金額')" width="120">
        <template #default="{ row }">
          <span>{{ row.amount != null ? formatNumber(row.amount, 'decimal') : '-' }}</span>
        </template>
      </el-table-column>
      <el-table-column :label="t('手配区分')" width="100">
        <template #default="{ row }">
          <el-input v-model="row.haibaiKbn" :disabled="!store.canEdit" size="small" />
        </template>
      </el-table-column>
      <el-table-column :label="t('納品先')" width="120">
        <template #default="{ row }">
          <el-input v-model="row.deliveryCd" :disabled="!store.canEdit" size="small" />
        </template>
      </el-table-column>
      <el-table-column :label="t('預り')" width="60" align="center">
        <template #default="{ row }">
          <el-checkbox v-model="row.consignedSalesFlg" true-value="1" false-value="0" :disabled="!store.canEdit" />
        </template>
      </el-table-column>
      <el-table-column :label="t('ステータス')" width="100">
        <template #default="{ row }">
          <el-tag size="small" :type="statusTagType(row.status)">{{ statusLabel(row.status) }}</el-tag>
        </template>
      </el-table-column>
    </el-table>

    <!-- 製品 CD 検索 popup -->
    <MasterReferenceDialog
      v-model="productPickerVisible"
      kind="product"
      :initial-keyword="pickingRow?.productCd ?? ''"
      :customer-cd="store.order.customerCd"
      @pick="onPickedProduct"
    />
    <MasterReferenceDialog
      v-model="customerPickerVisible"
      kind="customer"
      :initial-keyword="store.order.customerCd"
      @pick="onPickCustomer"
    />
  </el-card>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
import { ref, computed } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Search, Plus, Delete, Top, Bottom, CopyDocument, RefreshLeft } from '@element-plus/icons-vue'
import { useOrderStore } from '@/stores/order'
import { orderApi } from '@/api/erp/order'
import { formatNumber } from '@/utils/format'
import MasterReferenceDialog from '@/components/master/MasterReferenceDialog.vue'
import type { CustomerLookupItem, ProductLookupItem } from '@/api/erp/master'
import type { OrderDetailDto } from '@/types/erp/order'

const store = useOrderStore()

const memberProductCd = ref('')
const productPickerVisible = ref(false)
const customerPickerVisible = ref(false)
const pickingRow = ref<OrderDetailDto | null>(null)
const selectedRowNo = ref<number | null>(null)

const canCopy = computed(() => {
  // 仕様書 §4：受注区分=原紙(40) のみ行コピー有効
  return store.canEdit && !!selectedRowNo.value && store.order.orderType === '40'
})
const canMoveUp = computed(() => store.canEdit && !!selectedRowNo.value && selectedRowNo.value > 1)
const canMoveDown = computed(() => store.canEdit && !!selectedRowNo.value && selectedRowNo.value < store.order.details.length)

function onCurrentRowChange(row: OrderDetailDto | null) {
  selectedRowNo.value = row?.webOrderDetailNo ?? null
  if (row) store.currentDetailIndex = store.order.details.findIndex(d => d.webOrderDetailNo === row.webOrderDetailNo)
}

function onRemove() {
  if (!selectedRowNo.value) return
  store.removeDetail(selectedRowNo.value)
  selectedRowNo.value = null
}

function onCopy() {
  if (!selectedRowNo.value) return
  store.copyDetail(selectedRowNo.value)
}

function onMove(dir: 'up' | 'down') {
  if (!selectedRowNo.value) return
  store.moveDetail(selectedRowNo.value, dir)
  selectedRowNo.value += dir === 'up' ? -1 : 1
}

async function onClear() {
  try {
    await ElMessageBox.confirm('入力中の内容をすべてクリアします。よろしいですか？', '確認', {
      confirmButtonText: 'OK', cancelButtonText: 'キャンセル', type: 'warning',
    })
    store.clearForm()
    selectedRowNo.value = null
    ElMessage.success('クリアしました')
  } catch { /* */ }
}

function onQtyOrPriceChange(row: OrderDetailDto) {
  const qty = row.quantity ?? 0
  const price = store.order.salesPriceDiv === '1'
    ? (row.individualUnitPrice ?? 0)
    : (row.setUnitPrice ?? 0)
  row.amount = qty * price
  store.markDirty()
}

function onPickProduct(row: OrderDetailDto) {
  if (!store.canEdit) return
  pickingRow.value = row
  productPickerVisible.value = true
}

async function onPickedProduct(item: CustomerLookupItem | ProductLookupItem) {
  if (!('productCd' in item)) return
  const cd = item.productCd
  const row = pickingRow.value
  if (!row) return

  // 製品マスタから 63 項目を引入
  try {
    const res = await orderApi.lookupProductMaster(cd)
    if (res.code === 0 && res.data) {
      Object.assign(row, res.data)
      row.productCd = cd
      onQtyOrPriceChange(row)
      store.markDirty()
    }
  } catch { /* */ }
  pickingRow.value = null
}

function onPickCustomer(item: CustomerLookupItem | ProductLookupItem) {
  if (!('customerCd' in item) || !item.customerCd) return
  store.order.customerCd = item.customerCd
  store.markDirty()
}

async function onLookupMembers() {
  if (!memberProductCd.value) return
  try {
    const res = await orderApi.lookupBySetProductCd(memberProductCd.value)
    if (res.code === 0 && res.data) {
      const rows = res.data
      if (rows.length === 0) {
        ElMessage.warning('該当する部材がありません')
        return
      }
      // 既存明細をクリアして引入結果を入れる
      store.order.details = rows.map((r, i) => ({
        ...r,
        webOrderDetailNo: i + 1,
        status: 0,
        wfApprovalFlg: false,
        mcTransferFlg: false,
        provisionalPriceFlg: false,
        processes: [],
        processNotes: [],
        materials: [],
      }))
      store.markDirty()
      ElMessage.success(`${rows.length} 件の部材を引入しました`)
    }
  } catch { /* */ }
}

function statusLabel(s: number): string {
  return s === 9 ? 'mc転送済' : s === 1 ? '承認待' : '未'
}
function statusTagType(s: number): 'info' | 'warning' | 'success' {
  return s === 9 ? 'success' : s === 1 ? 'warning' : 'info'
}
</script>

<style scoped>
:deep(.el-form-item) { margin-bottom: 8px; }
</style>
