<template>
  <!--
    共通 Master Popup（参照ダイアログ）
    MSBBPACOM 共通設計書 §項目定義「テ:テーブル参照」 パターンの汎用実装
    対応マスタ: customer (得意先) / product (製品マスタ)
    将来拡張: ship (原紙) / press (印刷) / process (工程) etc.
  -->
  <el-dialog
    :model-value="modelValue"
    @update:model-value="(v: boolean) => emit('update:modelValue', v)"
    :title="title"
    width="860"
    :close-on-click-modal="false"
    draggable
    @open="onOpen"
  >
    <el-form inline @submit.prevent="onSearch" size="small">
      <el-form-item label="キーワード">
        <el-input
          v-model="keyword"
          placeholder="コード／名称 部分一致"
          clearable
          style="width: 280px"
          @keyup.enter="onSearch"
        />
      </el-form-item>
      <el-form-item v-if="kind === 'product'" label="顧客CD">
        <el-input v-model="filterCustomerCd" placeholder="任意" clearable style="width: 140px" />
      </el-form-item>
      <el-form-item v-if="kind === 'product'">
        <el-checkbox v-model="onlyApproved">承認済のみ</el-checkbox>
      </el-form-item>
      <el-form-item>
        <el-button type="primary" :icon="Search" @click="onSearch">検索</el-button>
        <el-button :icon="RefreshLeft" @click="onReset">クリア</el-button>
      </el-form-item>
    </el-form>

    <el-table
      :data="rows"
      v-loading="loading"
      stripe
      border
      height="380"
      highlight-current-row
      @row-dblclick="onPick"
    >
      <template v-if="kind === 'customer'">
        <el-table-column prop="customerCd" label="顧客CD" width="120" />
        <el-table-column prop="customerName" label="顧客名" min-width="220" show-overflow-tooltip />
        <el-table-column prop="usageCount" label="使用件数" width="100" align="right" />
      </template>
      <template v-else-if="kind === 'product'">
        <el-table-column prop="productCd" label="製品CD" width="170" />
        <el-table-column prop="setProductName" label="セット品名" min-width="180" show-overflow-tooltip />
        <el-table-column prop="customerCd" label="顧客CD" width="100" />
        <el-table-column prop="customerItemName1" label="顧客品名1" min-width="160" show-overflow-tooltip />
        <el-table-column prop="customerItemName2" label="顧客品名2" min-width="140" show-overflow-tooltip />
        <el-table-column label="状態" width="90">
          <template #default="{ row }">
            <el-tag :type="statusTagType(row.status)" size="small">{{ statusLabel(row.status) }}</el-tag>
          </template>
        </el-table-column>
      </template>
      <el-table-column label="操作" width="80" fixed="right">
        <template #default="{ row }">
          <el-button link type="primary" @click="onPick(row)">選択</el-button>
        </template>
      </el-table-column>
    </el-table>

    <div class="pagination">
      <el-pagination
        v-model:current-page="page"
        v-model:page-size="pageSize"
        :total="total"
        :page-sizes="[10, 20, 50]"
        layout="total, sizes, prev, pager, next"
        small
        @current-change="loadData"
        @size-change="loadData"
      />
    </div>

    <template #footer>
      <el-button @click="emit('update:modelValue', false)">キャンセル</el-button>
    </template>
  </el-dialog>
</template>

<script setup lang="ts">
import { ref, watch, computed } from 'vue'
import { Search, RefreshLeft } from '@element-plus/icons-vue'
import { masterApi } from '@/api/master'
import type { CustomerLookupItem, ProductLookupItem } from '@/api/master'

type LookupKind = 'customer' | 'product'

const props = defineProps<{
  modelValue: boolean
  /** 検索対象マスタ種別 */
  kind: LookupKind
  /** 既定キーワード（呼び出し側のフィールド値） */
  initialKeyword?: string
  /** product: 顧客で絞り込む */
  customerCd?: string
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', v: boolean): void
  (e: 'pick', row: CustomerLookupItem | ProductLookupItem): void
}>()

const title = computed(() =>
  props.kind === 'customer' ? '得意先 参照' : '製品マスタ 参照'
)

const keyword = ref('')
const filterCustomerCd = ref('')
const onlyApproved = ref(false)
const page = ref(1)
const pageSize = ref(20)
const total = ref(0)
const loading = ref(false)
const rows = ref<(CustomerLookupItem | ProductLookupItem)[]>([])

function statusLabel(s: number): string {
  return s === 9 ? '承認済' : s === 1 ? '承認待' : '未作成'
}
function statusTagType(s: number): 'info' | 'warning' | 'success' {
  return s === 9 ? 'success' : s === 1 ? 'warning' : 'info'
}

async function loadData() {
  try {
    loading.value = true
    if (props.kind === 'customer') {
      const res = await masterApi.lookupCustomer({
        keyword: keyword.value || undefined,
        page: page.value,
        pageSize: pageSize.value,
      })
      if (res.code === 0) {
        rows.value = res.data.rows
        total.value = res.data.total
      }
    } else {
      const res = await masterApi.lookupProduct({
        keyword: keyword.value || undefined,
        customerCd: filterCustomerCd.value || undefined,
        onlyApproved: onlyApproved.value || undefined,
        page: page.value,
        pageSize: pageSize.value,
      })
      if (res.code === 0) {
        rows.value = res.data.rows
        total.value = res.data.total
      }
    }
  } finally {
    loading.value = false
  }
}

function onSearch() {
  page.value = 1
  loadData()
}

function onReset() {
  keyword.value = ''
  filterCustomerCd.value = ''
  onlyApproved.value = false
  page.value = 1
  loadData()
}

function onPick(row: CustomerLookupItem | ProductLookupItem) {
  emit('pick', row)
  emit('update:modelValue', false)
}

function onOpen() {
  keyword.value = props.initialKeyword ?? ''
  filterCustomerCd.value = props.kind === 'product' ? (props.customerCd ?? '') : ''
  onlyApproved.value = false
  page.value = 1
  loadData()
}

watch(
  () => props.modelValue,
  v => {
    if (!v) {
      rows.value = []
      total.value = 0
    }
  },
)
</script>

<style scoped>
.pagination {
  margin-top: 12px;
  text-align: right;
}
</style>
