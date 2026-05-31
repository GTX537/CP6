<template>
  <div>
    <!-- 不用 VolTable，日志是只读的，自己写更合适 -->
    <div class="table-header">
      <div class="search-area">
        <el-input
          v-model="keyword"
          :placeholder="t('table.search')"
          clearable
          style="width: 250px"
          @keyup.enter="search"
        >
          <template #append>
            <el-button @click="search" :icon="Search" />
          </template>
        </el-input>
      </div>
    </div>

    <el-table :data="tableData" v-loading="loading" stripe border style="width: 100%">
      <el-table-column prop="userName" :label="t('operlog.user')" width="100" />
      <el-table-column prop="httpMethod" :label="t('operlog.method')" width="80">
        <template #default="{ row }">
          <el-tag :type="methodColor(row.httpMethod)" size="small">{{ row.httpMethod }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="requestUrl" :label="t('operlog.url')" show-overflow-tooltip />
      <el-table-column prop="controller" :label="t('operlog.controller')" width="110" />
      <el-table-column prop="statusCode" :label="t('operlog.status')" width="80">
        <template #default="{ row }">
          <el-tag :type="row.statusCode === 200 ? 'success' : 'danger'" size="small">{{ row.statusCode }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="elapsedMs" :label="t('operlog.elapsed')" width="90">
        <template #default="{ row }">{{ row.elapsedMs }}ms</template>
      </el-table-column>
      <el-table-column prop="clientIp" label="IP" width="120" />
      <el-table-column prop="createDate" :label="t('operlog.time')" width="170" />
      <el-table-column :label="t('table.operation')" width="80">
        <template #default="{ row }">
          <el-button link type="primary" @click="showDetail(row)">{{ t('operlog.detail') }}</el-button>
        </template>
      </el-table-column>
    </el-table>

    <div class="pagination">
      <el-pagination
        v-model:current-page="page"
        v-model:page-size="pageSize"
        :total="total"
        :page-sizes="[20, 50, 100]"
        layout="total, sizes, prev, pager, next"
        @current-change="loadData"
        @size-change="loadData"
      />
    </div>

    <!-- 详情弹窗 -->
    <el-dialog v-model="detailVisible" :title="t('operlog.detail')" width="600px">
      <el-descriptions :column="1" border>
        <el-descriptions-item :label="t('operlog.user')">{{ detailRow?.userName }}</el-descriptions-item>
        <el-descriptions-item :label="t('operlog.method')">{{ detailRow?.httpMethod }}</el-descriptions-item>
        <el-descriptions-item :label="t('operlog.url')">{{ detailRow?.requestUrl }}</el-descriptions-item>
        <el-descriptions-item :label="t('operlog.controller')">{{ detailRow?.controller }} / {{ detailRow?.action }}</el-descriptions-item>
        <el-descriptions-item :label="t('operlog.status')">{{ detailRow?.statusCode }}</el-descriptions-item>
        <el-descriptions-item :label="t('operlog.elapsed')">{{ detailRow?.elapsedMs }}ms</el-descriptions-item>
        <el-descriptions-item label="IP">{{ detailRow?.clientIp }}</el-descriptions-item>
        <el-descriptions-item :label="t('operlog.time')">{{ detailRow?.createDate }}</el-descriptions-item>
        <el-descriptions-item :label="t('operlog.requestBody')">
          <pre style="max-height: 200px; overflow: auto; white-space: pre-wrap; margin: 0">{{ detailRow?.requestBody || '-' }}</pre>
        </el-descriptions-item>
      </el-descriptions>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { Search } from '@element-plus/icons-vue'
import { operLogApi } from '@/api/operlog'

const { t } = useI18n()

const tableData = ref<any[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(20)
const keyword = ref('')
const loading = ref(false)

const detailVisible = ref(false)
const detailRow = ref<any>(null)

function methodColor(method: string) {
  const map: Record<string, string> = { POST: 'success', PUT: 'warning', DELETE: 'danger' }
  return map[method] || 'info'
}

async function loadData() {
  loading.value = true
  try {
    const res = await operLogApi.getList({ page: page.value, pageSize: pageSize.value, keyword: keyword.value }) as any
    tableData.value = res.rows
    total.value = res.total
  } finally {
    loading.value = false
  }
}

function search() {
  page.value = 1
  loadData()
}

function showDetail(row: any) {
  detailRow.value = row
  detailVisible.value = true
}

onMounted(() => loadData())
</script>

<style scoped>
.table-header {
  display: flex;
  justify-content: space-between;
  margin-bottom: 16px;
}
.pagination {
  display: flex;
  justify-content: flex-end;
  margin-top: 16px;
}
</style>
