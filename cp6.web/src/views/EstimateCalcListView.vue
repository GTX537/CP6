<template>
  <div class="estimate-list">
    <!-- 查询区 -->
    <el-card shadow="never" class="search-card">
      <el-form inline :model="query" @submit.prevent="onSearch">
        <el-form-item label="見積No">
          <el-input v-model="query.qtnCalcNo" placeholder="例: 00000001" clearable style="width: 180px" />
        </el-form-item>
        <el-form-item label="顧客">
          <el-input v-model="query.customerCd" placeholder="顧客コード" clearable style="width: 160px" />
        </el-form-item>
        <el-form-item label="拠点">
          <el-select v-model="query.baseCd" placeholder="全部" clearable style="width: 160px">
            <el-option v-for="b in bases" :key="b.baseCd" :value="b.baseCd" :label="`${b.baseCd} ${b.baseName}`" />
          </el-select>
        </el-form-item>
        <el-form-item label="見積日">
          <el-date-picker
            v-model="dateRange"
            type="daterange"
            value-format="YYYY-MM-DD"
            range-separator="~"
            start-placeholder="開始日"
            end-placeholder="終了日"
            style="width: 260px"
          />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :icon="Search" @click="onSearch">検索</el-button>
          <el-button :icon="RefreshLeft" @click="onReset">クリア</el-button>
          <el-button type="success" :icon="Plus" @click="onNew">新規作成</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <!-- 列表 -->
    <el-card shadow="never" class="table-card">
      <el-table
        :data="rows"
        v-loading="loading"
        stripe
        border
        style="width: 100%"
        @row-dblclick="(row: EstimateCalcListItem) => onView(row)"
      >
        <el-table-column prop="qtnCalcNo" label="見積No" width="140" />
        <el-table-column prop="qtnDate" label="見積日" width="110">
          <template #default="{ row }">{{ fmtDate(row.qtnDate) }}</template>
        </el-table-column>
        <el-table-column prop="qtnBaseCd" label="拠点" width="80" />
        <el-table-column prop="staffCd" label="担当" width="90" />
        <el-table-column prop="customerCd" label="顧客" width="120" />
        <el-table-column prop="customerProductName1" label="顧客品名" min-width="200" show-overflow-tooltip />
        <el-table-column prop="orderQty" label="受注数" width="100" align="right">
          <template #default="{ row }">{{ fmtNum(row.orderQty) }}</template>
        </el-table-column>
        <el-table-column prop="estimateUnitPrice" label="見積単価" width="120" align="right">
          <template #default="{ row }">{{ fmtMoney(row.estimateUnitPrice) }}</template>
        </el-table-column>
        <el-table-column prop="qtnDiv" label="見積区分" width="100" />
        <el-table-column prop="modifyDate" label="最終更新" width="160">
          <template #default="{ row }">{{ fmtDateTime(row.modifyDate || row.createDate) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="260" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" @click="onView(row)">照会</el-button>
            <el-button link type="warning" @click="onEdit(row)">編集</el-button>
            <el-button link type="success" @click="onCopy(row)">流用</el-button>
            <el-button link type="danger" @click="onDelete(row)">削除</el-button>
          </template>
        </el-table-column>
      </el-table>

      <div class="pagination">
        <el-pagination
          v-model:current-page="query.page"
          v-model:page-size="query.pageSize"
          :total="total"
          :page-sizes="[10, 20, 50]"
          layout="total, sizes, prev, pager, next"
          @current-change="loadData"
          @size-change="loadData"
        />
      </div>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted, computed } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Search, RefreshLeft, Plus } from '@element-plus/icons-vue'
import { estimateCalcApi } from '@/api/estimateCalc'
import { masterApi } from '@/api/master'
import { useEstimateStore } from '@/stores/estimate'
import { OperationType } from '@/types/estimateCalc'
import type { EstimateCalcListItem, MasterBase, EstimateCalcQuery } from '@/types/estimateCalc'

const router = useRouter()
const store = useEstimateStore()

const loading = ref(false)
const rows = ref<EstimateCalcListItem[]>([])
const total = ref(0)
const bases = ref<MasterBase[]>([])

const query = reactive<EstimateCalcQuery>({
  page: 1,
  pageSize: 10,
  qtnCalcNo: '',
  customerCd: '',
  baseCd: '',
  dateFrom: '',
  dateTo: '',
})
const dateRange = ref<[string, string] | null>(null)

const fmtDate = (v?: string) => (v ? v.slice(0, 10) : '')
const fmtDateTime = (v?: string) => (v ? v.replace('T', ' ').slice(0, 19) : '')
const fmtNum = (v?: number) => (v == null ? '' : v.toLocaleString())
const fmtMoney = (v?: number) =>
  v == null ? '' : v.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })

async function loadData() {
  try {
    loading.value = true
    if (dateRange.value) {
      query.dateFrom = dateRange.value[0]
      query.dateTo = dateRange.value[1]
    } else {
      query.dateFrom = ''
      query.dateTo = ''
    }
    const res = await estimateCalcApi.getList(query)
    if (res.code === 0) {
      rows.value = res.data.rows ?? []
      total.value = res.data.total ?? 0
    }
  } finally {
    loading.value = false
  }
}

function onSearch() {
  query.page = 1
  loadData()
}
function onReset() {
  query.qtnCalcNo = ''
  query.customerCd = ''
  query.baseCd = ''
  dateRange.value = null
  query.page = 1
  loadData()
}

/** 跳转到编辑页，设置操作种别并加载数据 */
async function gotoDetail(row: EstimateCalcListItem, op: OperationType) {
  try {
    const res = await estimateCalcApi.getByNo(row.qtnCalcNo)
    if (res.code !== 0) {
      ElMessage.warning(res.message)
      return
    }
    store.reset()
    store.loadBasicInfo(res.data)
    store.setOperationType(op)
    router.push('/estimate-calc')
  } catch (e) {
    console.error(e)
  }
}

function onView(row: EstimateCalcListItem) {
  gotoDetail(row, OperationType.View)
}
function onEdit(row: EstimateCalcListItem) {
  gotoDetail(row, OperationType.Edit)
}
function onCopy(row: EstimateCalcListItem) {
  gotoDetail(row, OperationType.Copy)
}
async function onDelete(row: EstimateCalcListItem) {
  try {
    await ElMessageBox.confirm(`削除 ${row.qtnCalcNo} ? （論理削除、復旧不可）`, '確認', {
      type: 'warning',
    })
  } catch {
    return
  }
  try {
    const res = await estimateCalcApi.remove(row.qtnCalcNo)
    if (res.code === 0) {
      ElMessage.success('削除完了')
      loadData()
    }
  } catch {
    // interceptor shows error
  }
}

function onNew() {
  store.reset()
  store.setOperationType(OperationType.New)
  router.push('/estimate-calc')
}

onMounted(async () => {
  const [baseRes] = await Promise.all([masterApi.getBases(), loadData()])
  bases.value = baseRes.data ?? []
})
</script>

<style scoped>
.estimate-list {
  padding: 12px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.search-card :deep(.el-card__body),
.table-card :deep(.el-card__body) {
  padding: 12px 16px;
}
.pagination {
  margin-top: 12px;
  text-align: right;
}
</style>
