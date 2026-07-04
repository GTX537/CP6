<template>
  <el-card shadow="never">
    <!-- ============ 検索条件 ============ -->
    <el-form
      :model="store.basicInfo"
      label-width="110px"
      :disabled="store.isPageReadOnly"
      size="small"
      inline
    >
      <el-form-item :label="t('製品 CD')">
        <el-input v-model="store.productCd" :disabled="!store.isPkEditable" :placeholder="t('新規時自動採番')" style="width: 200px" />
      </el-form-item>
      <el-form-item :label="t('セット製品 CD')">
        <el-input
          v-model="store.basicInfo.setProductCd"
          :disabled="!store.canEdit"
          :placeholder="t('親製品 CD')"
          style="width: 240px"
        >
          <template #append>
            <el-button :icon="Search" :disabled="!store.canEdit" @click="openProductPicker" />
          </template>
        </el-input>
      </el-form-item>
      <el-form-item :label="t('セット品名')">
        <el-input v-model="store.basicInfo.setProductName" :disabled="!store.canEdit" style="width: 280px" />
      </el-form-item>
      <el-form-item :label="t('得意先 CD')">
        <el-input
          v-model="store.basicInfo.customerCd"
          :disabled="!store.canEdit"
          style="width: 200px"
        >
          <template #append>
            <el-button :icon="Search" :disabled="!store.canEdit" @click="openCustomerPicker" />
          </template>
        </el-input>
      </el-form-item>
      <el-form-item :label="t('親案件 NO')">
        <el-input v-model="parentProjectInput" :disabled="!store.canEdit" style="width: 160px" />
      </el-form-item>
      <el-form-item :label="t('御見積書 NO')">
        <el-input v-model="quotationNoInput" :disabled="!store.canEdit" style="width: 200px">
          <template #append>
            <el-button :icon="Search" @click="onLoadFromQuotation">{{ t('引入') }}</el-button>
          </template>
        </el-input>
      </el-form-item>
    </el-form>

    <el-divider content-position="left">{{ t('部材一覧') }}</el-divider>

    <!-- ============ 部材一覧 ============ -->
    <div style="margin-bottom: 8px">
      <el-button-group>
        <el-button
          type="primary"
          :icon="Plus"
          size="small"
          :disabled="!store.canEdit"
          @click="store.addMember()"
        >{{ t('行追加') }}</el-button>
        <el-button
          :icon="Delete"
          size="small"
          :disabled="!store.canEdit || !selectedRowNo"
          @click="onRemoveSelected"
        >{{ t('行削除') }}</el-button>
        <el-button
          :icon="Top"
          size="small"
          :disabled="!store.canEdit || !selectedRowNo || selectedRowNo === 1"
          @click="onMoveSelected('up')"
        >▲</el-button>
        <el-button
          :icon="Bottom"
          size="small"
          :disabled="!store.canEdit || !selectedRowNo || selectedRowNo === store.members.length"
          @click="onMoveSelected('down')"
        >▼</el-button>
        <el-button
          :icon="RefreshLeft"
          size="small"
          :disabled="!store.canEdit"
          @click="onClearForm"
        >{{ t('クリア') }}</el-button>
        <el-button
          :icon="Document"
          size="small"
          :disabled="!selectedRowNo || !selectedEstimateCalcNo"
          @click="onOpenEstimateDetail"
        >{{ t('見積詳細') }}</el-button>
      </el-button-group>
      <el-tag v-if="store.wipCheckResult" :type="wipTagType" size="small" style="margin-left: 12px">
        {{ t('仕掛チェック') }}: {{ wipLabel }}
      </el-tag>
    </div>

    <el-table
      :data="store.members"
      border
      stripe
      highlight-current-row
      style="width: 100%"
      size="small"
      @current-change="onCurrentRowChange">
      <el-table-column prop="rowNo" :label="t('行')" width="60" align="center" />
      <el-table-column prop="productCd" :label="t('製品 CD')" width="160" />
      <el-table-column :label="t('御見積書 NO')" width="160">
        <template #default="{ row }">
          <el-input v-model="row.quotationNo" :disabled="!store.canEdit" size="small" />
        </template>
      </el-table-column>
      <el-table-column :label="t('見積計算書 NO')" width="160">
        <template #default="{ row }">
          <el-input v-model="row.estimateCalcNo" :disabled="!store.canEdit" size="small" />
        </template>
      </el-table-column>
      <el-table-column :label="t('親案件')" width="120">
        <template #default="{ row }">
          <el-input v-model="row.projectNoParent" :disabled="!store.canEdit" size="small" />
        </template>
      </el-table-column>
      <el-table-column :label="t('子案件')" width="120">
        <template #default="{ row }">
          <el-input v-model="row.projectNoChild" :disabled="!store.canEdit" size="small" />
        </template>
      </el-table-column>
      <el-table-column :label="t('部材案件')" width="120">
        <template #default="{ row }">
          <el-input v-model="row.projectNoMaterial" :disabled="!store.canEdit" size="small" />
        </template>
      </el-table-column>
      <el-table-column :label="t('顧客品名 1')" min-width="160">
        <template #default="{ row }">
          <el-input v-model="row.customerProductName1" :disabled="!store.canEdit" size="small" />
        </template>
      </el-table-column>
      <el-table-column :label="t('状態')" width="90">
        <template #default="{ row }">
          <el-tag size="small" :type="statusTagType(row.status)">{{ statusLabel(row.status) }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="WF" width="60" align="center">
        <template #default="{ row }">
          <el-icon v-if="row.wfApproved" color="var(--cp-ok)"><Check /></el-icon>
          <span v-else>-</span>
        </template>
      </el-table-column>
      <el-table-column :label="t('連携')" width="60" align="center">
        <template #default="{ row }">
          <el-icon v-if="row.masterLinked" color="var(--cp-brand)"><Link /></el-icon>
          <span v-else>-</span>
        </template>
      </el-table-column>
      <el-table-column :label="t('操作')" width="80" align="center" fixed="right">
        <template #default="{ row }">
          <el-button
            link
            type="danger"
            size="small"
            :disabled="!store.canEdit"
            @click="store.removeMember(row.rowNo)"
          >{{ t('削除') }}</el-button>
        </template>
      </el-table-column>
    </el-table>

    <!-- ============ 共通 Master Popup ============ -->
    <MasterReferenceDialog
      v-model="customerPickerVisible"
      kind="customer"
      :initial-keyword="store.basicInfo.customerCd"
      @pick="onPickCustomer"
    />
    <MasterReferenceDialog
      v-model="productPickerVisible"
      kind="product"
      :initial-keyword="store.basicInfo.setProductCd"
      :customer-cd="store.basicInfo.customerCd"
      @pick="onPickProduct"
    />
  </el-card>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
import { ref, computed } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Search, Plus, Check, Link, Delete, Top, Bottom, RefreshLeft, Document } from '@element-plus/icons-vue'
import type { ProductMemberDto } from '@/types/erp/productMaster'
import { useProductMasterStore } from '@/stores/productMaster'
import { productApi } from '@/api/erp/product'
import MasterReferenceDialog from '@/components/master/MasterReferenceDialog.vue'
import type { CustomerLookupItem, ProductLookupItem } from '@/api/erp/master'

const store = useProductMasterStore()

// ───── Master Popup ─────
const customerPickerVisible = ref(false)
const productPickerVisible = ref(false)

function openCustomerPicker() {
  if (!store.canEdit) return
  customerPickerVisible.value = true
}
function openProductPicker() {
  if (!store.canEdit) return
  productPickerVisible.value = true
}

function isCustomer(row: CustomerLookupItem | ProductLookupItem): row is CustomerLookupItem {
  return (row as CustomerLookupItem).customerCd !== undefined && (row as ProductLookupItem).productCd === undefined
}

function onPickCustomer(row: CustomerLookupItem | ProductLookupItem) {
  if (!isCustomer(row)) return
  store.basicInfo.customerCd = row.customerCd
  if (row.customerName) store.basicInfo.customerName = row.customerName
  store.markDirty()
}

function onPickProduct(row: CustomerLookupItem | ProductLookupItem) {
  if (isCustomer(row)) return
  const p = row as ProductLookupItem
  store.basicInfo.setProductCd = p.productCd
  if (p.setProductName) store.basicInfo.setProductName = p.setProductName
  // 同時に得意先未設定なら採用（共通仕様：セット品の顧客に揃える）
  if (!store.basicInfo.customerCd && p.customerCd) {
    store.basicInfo.customerCd = p.customerCd
    if (p.customerName) store.basicInfo.customerName = p.customerName
  }
  store.markDirty()
}

const quotationNoInput = ref<string>('')
const parentProjectInput = ref<string>('')

// ───── 部材一覧 行選択 / トールバー ─────
const selectedRowNo = ref<number | null>(null)
const selectedEstimateCalcNo = computed(() => {
  if (!selectedRowNo.value) return ''
  const r = store.members.find(m => m.rowNo === selectedRowNo.value)
  return r?.estimateCalcNo ?? ''
})

function onCurrentRowChange(row: ProductMemberDto | null) {
  selectedRowNo.value = row?.rowNo ?? null
}

function onRemoveSelected() {
  if (!selectedRowNo.value) return
  store.removeMember(selectedRowNo.value)
  selectedRowNo.value = null
}

function onMoveSelected(direction: 'up' | 'down') {
  if (!selectedRowNo.value) return
  store.moveMember(selectedRowNo.value, direction)
  // 行入れ替え後は新しい rowNo を選択状態に保つ
  selectedRowNo.value = direction === 'up'
    ? selectedRowNo.value - 1
    : selectedRowNo.value + 1
}

async function onClearForm() {
  try {
    await ElMessageBox.confirm(
      t('入力中の内容をすべてクリアします。よろしいですか？'),
      t('確認'),
      { confirmButtonText: 'OK', cancelButtonText: t('キャンセル'), type: 'warning' },
    )
    store.clearForm()
    quotationNoInput.value = ''
    parentProjectInput.value = ''
    selectedRowNo.value = null
    ElMessage.success(t('クリアしました'))
  } catch {
    /* user cancelled */
  }
}

function onOpenEstimateDetail() {
  if (!selectedEstimateCalcNo.value) {
    ElMessage.warning(t('見積計算書 NO が設定された行を選択してください'))
    return
  }
  // 見積詳細画面 (MSBBPA040) は外部画面のため別ウィンドウで開く想定
  const url = `/estimate-detail?no=${encodeURIComponent(selectedEstimateCalcNo.value)}`
  window.open(url, '_blank')
}

function statusLabel(s: number): string {
  return s === 9 ? t('承認済') : s === 1 ? t('承認待') : t('未作成')
}
function statusTagType(s: number): 'info' | 'warning' | 'success' {
  return s === 9 ? 'success' : s === 1 ? 'warning' : 'info'
}

const wipTagType = computed<'success' | 'warning' | 'danger'>(() => {
  const lv = store.wipCheckResult?.level ?? 0
  if (lv === 0) return 'success'
  if (lv === 1) return 'warning'
  return 'danger'
})
const wipLabel = computed(() => {
  const lv = store.wipCheckResult?.level ?? 0
  if (lv === 0) return t('問題なし')
  if (lv === 1) return t('警告（続行可）')
  if (lv === 2) return t('エラー（確定済）')
  return t('エラー（指図済）')
})

async function onLoadFromQuotation() {
  if (!quotationNoInput.value) {
    ElMessage.warning(t('御見積書 NO を入力してください'))
    return
  }
  try {
    const res = await productApi.getMembersByQuotation(quotationNoInput.value)
    if (res.code === 0) {
      store.members = res.data ?? []
      store.markDirty()
      ElMessage.success(t('{count} 件の部材を引入しました', { count: store.members.length }))
    }
  } catch {
    /* http interceptor toast */
  }
}
</script>

<style scoped>
:deep(.el-form-item) {
  margin-bottom: 8px;
}
</style>
