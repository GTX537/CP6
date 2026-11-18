<template>
  <div>
    <!-- 不用 VolTable，日志是只读的，自己写更合适 -->
    <div class="table-header">
      <div class="search-area">
        <el-input
          v-model="keyword"
          placeholder="搜索控制器、操作或路由模板"
          clearable
          style="width: 250px"
          @keyup.enter="search"
        >
          <template #append>
            <el-button @click="search" :icon="Search" />
          </template>
        </el-input>
      </div>
      <AuditAccessPanel ref="access" audit-class="TECH_TELEMETRY" menu="operlog" :policy-version="policyVersion" :filter="activeFilter" />
    </div>

    <el-alert v-if="loadError" :title="loadError" type="warning" :closable="false" />
    <el-table :data="tableData" v-loading="loading" stripe border style="width: 100%">
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
      <el-table-column label="记录状态" width="135">
        <template #default="{ row }">{{ row.outcome === 'UNKNOWN' ? '业务结果待核对' : row.evidenceGap ? '记录不完整' : 'HTTP 观测' }}</template>
      </el-table-column>
      <el-table-column :label="t('operlog.time')" min-width="215">
        <template #default="{ row }">{{ auditTime(row, row.createDate) }}</template>
      </el-table-column>
      <el-table-column :label="t('table.operation')" width="80">
        <template #default="{ row }">
          <el-button v-permission="'operlog:read-content'" link type="primary" @click="access?.openDetail(row.id)">{{ t('operlog.detail') }}</el-button>
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

    <AuditRetentionPanel v-permission="'operlog:retention-manage'" @completed="loadData" />
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, onBeforeUnmount } from 'vue'
import { useI18n } from 'vue-i18n'
import { Search } from '@element-plus/icons-vue'
import { operLogApi } from '@/api/sys/operlog'
import { auditTime, type AuditFilters, type AuditSummary } from '@/api/sys/auditAccess'
import { AUTH_EVENT, identityStamp } from '@/auth/identity'
import AuditAccessPanel from './AuditAccessPanel.vue'
import AuditRetentionPanel from './AuditRetentionPanel.vue'

const { t } = useI18n()

const tableData = ref<(AuditSummary & { createDate: string; outcome?: string })[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(20)
const keyword = ref('')
const loading = ref(false)

const access = ref<InstanceType<typeof AuditAccessPanel>>()
const policyVersion = ref<string>()
const activeFilter = ref<AuditFilters>({})
const loadError = ref('')
let generation = 0

function methodColor(method: string) {
  const map: Record<string, string> = { POST: 'success', PUT: 'warning', DELETE: 'danger' }
  return map[method] || 'info'
}

async function loadData() {
  const id = ++generation, stamp = identityStamp()
  const current = () => id === generation && stamp === identityStamp()
  const filter = { keyword: keyword.value || undefined }
  loading.value = true
  loadError.value = ''
  policyVersion.value = undefined
  try {
    const res = await operLogApi.getList({ ...filter, page: page.value, pageSize: pageSize.value })
    if (!current()) return
    tableData.value = res.rows
    total.value = res.total
    activeFilter.value = filter
    policyVersion.value = res.policyVersion
  } catch {
    if (current()) { tableData.value = []; total.value = 0; loadError.value = '当前权限或服务状态不允许查询操作日志。' }
  } finally {
    if (current()) loading.value = false
  }
}

function search() {
  page.value = 1
  loadData()
}

function clearList() {
  ++generation
  tableData.value = []
  total.value = 0
  policyVersion.value = undefined
  loadError.value = ''
  loading.value = false
}

onMounted(() => { window.addEventListener(AUTH_EVENT, clearList); loadData() })
onBeforeUnmount(() => { window.removeEventListener(AUTH_EVENT, clearList); clearList() })
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
