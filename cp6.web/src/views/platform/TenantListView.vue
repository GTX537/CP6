<template>
  <div>
    <div class="page-head">
      <h2 class="page-title">{{ t('platform.tenant.title') }}</h2>
    </div>

    <!-- 筛选 + 新建 -->
    <div class="table-header">
      <div class="search-area">
        <el-input
          v-model="keyword"
          :placeholder="t('platform.tenant.code') + ' / ' + t('platform.tenant.name')"
          clearable
          style="width: 240px"
          @keyup.enter="search"
        />
        <el-select v-model="enableFilter" :placeholder="t('platform.tenant.title')" clearable style="width: 130px">
          <el-option :label="t('sec.2fa.status.on')" :value="true" />
          <el-option :label="t('sec.2fa.status.off')" :value="false" />
        </el-select>
        <el-button type="primary" :icon="Search" @click="search">{{ t('table.search') }}</el-button>
        <el-button type="success" :icon="Plus" @click="creation?.open()">{{ t('platform.tenant.create') }}</el-button>
      </div>
    </div>

    <el-table :data="tableData" v-loading="loading" stripe border style="width: 100%">
      <el-table-column prop="tenantCode" :label="t('platform.tenant.code')" width="160" />
      <el-table-column prop="tenantName" :label="t('platform.tenant.name')" />
      <el-table-column prop="enable" :label="t('platform.tenant.title')" width="110">
        <template #default="{ row }">
          <el-tag :type="row.enable ? 'success' : 'info'" size="small">
            {{ row.enable ? t('sec.2fa.status.on') : t('sec.2fa.status.off') }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="expireDate" :label="t('platform.tenant.expire')" width="130">
        <template #default="{ row }">{{ row.expireDate ? row.expireDate.slice(0, 10) : '-' }}</template>
      </el-table-column>
      <el-table-column prop="userCount" :label="t('platform.tenant.userCount')" width="90" align="center" />
      <el-table-column :label="t('table.operation')" width="220">
        <template #default="{ row }">
          <el-button link type="primary" @click="management?.open(row.id)">{{ t('管理配置与启停') }}</el-button>
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

    <TenantCreationPanel ref="creation" @changed="loadData" />

    <TenantManagementPanel ref="management" @changed="loadData" />
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { Search, Plus } from '@element-plus/icons-vue'
import { tenantApi } from '@/api/platform/tenant'
import TenantManagementPanel from './TenantManagementPanel.vue'
import TenantCreationPanel from './TenantCreationPanel.vue'
import type { TenantRow } from '@/types/platform/platform'

const { t } = useI18n()
const creation = ref<InstanceType<typeof TenantCreationPanel>>()
const management = ref<InstanceType<typeof TenantManagementPanel>>()

const tableData = ref<TenantRow[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(20)
const loading = ref(false)
const keyword = ref('')
const enableFilter = ref<boolean | ''>('')

async function loadData() {
  loading.value = true
  try {
    const res = await tenantApi.list({
      keyword: keyword.value || undefined,
      enable: enableFilter.value === '' ? undefined : enableFilter.value,
      page: page.value,
      pageSize: pageSize.value
    })
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

onMounted(() => loadData())
</script>

<style scoped>
.page-head {
  margin-bottom: 16px;
}
.page-title {
  margin: 0;
  font-size: 18px;
  color: #303133;
}
.table-header {
  display: flex;
  justify-content: space-between;
  margin-bottom: 16px;
}
.search-area {
  display: flex;
  gap: 10px;
  flex-wrap: wrap;
}
.pagination {
  display: flex;
  justify-content: flex-end;
  margin-top: 16px;
}
</style>
