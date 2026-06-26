<template>
  <div>
    <div class="page-head">
      <h2 class="page-title">{{ t('platform.admin.title') }}</h2>
    </div>

    <div class="table-header">
      <div class="search-area">
        <el-input
          v-model="grantUserId"
          :placeholder="t('platform.tenant.adminUser') + ' (userId)'"
          clearable
          style="width: 320px"
          @keyup.enter="doGrant"
        />
        <el-button type="success" :icon="Plus" :loading="granting" @click="doGrant">
          {{ t('platform.admin.grant') }}
        </el-button>
      </div>
    </div>

    <el-table :data="tableData" v-loading="loading" stripe border style="width: 100%">
      <el-table-column prop="userName" :label="t('platform.tenant.adminUser')" width="180" />
      <el-table-column prop="nickName" :label="t('platform.tenant.name')" />
      <el-table-column prop="tenantId" label="Tenant" show-overflow-tooltip />
      <el-table-column prop="enable" :label="t('platform.tenant.title')" width="110">
        <template #default="{ row }">
          <el-tag :type="row.enable ? 'success' : 'info'" size="small">
            {{ row.enable ? t('sec.2fa.status.on') : t('sec.2fa.status.off') }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column :label="t('table.operation')" width="120">
        <template #default="{ row }">
          <el-button link type="danger" @click="doRevoke(row)">{{ t('platform.admin.revoke') }}</el-button>
        </template>
      </el-table-column>
    </el-table>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { Plus } from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { platformAdminApi } from '@/api/platform/admin'
import type { PlatformAdminRow } from '@/types/platform/platform'

const { t } = useI18n()

const tableData = ref<PlatformAdminRow[]>([])
const loading = ref(false)
const grantUserId = ref('')
const granting = ref(false)

async function loadData() {
  loading.value = true
  try {
    tableData.value = await platformAdminApi.list()
  } finally {
    loading.value = false
  }
}

async function doGrant() {
  const id = grantUserId.value.trim()
  if (!id) return
  granting.value = true
  try {
    await platformAdminApi.grant(id)
    grantUserId.value = ''
    ElMessage.success(t('platform.saved'))
    loadData()
  } catch {
    // E-SEC-032（用户不存在）由拦截器提示
  } finally {
    granting.value = false
  }
}

async function doRevoke(row: PlatformAdminRow) {
  try {
    await ElMessageBox.confirm(t('platform.admin.confirmRevoke'), t('platform.admin.revoke'), {
      type: 'warning'
    })
  } catch {
    return // 用户取消
  }
  try {
    await platformAdminApi.revoke(row.id)
    ElMessage.success(t('platform.saved'))
    loadData()
  } catch {
    // E-SEC-037（不能撤最后一个）由 http.ts 拦截器以 error message 提示
  }
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
</style>
