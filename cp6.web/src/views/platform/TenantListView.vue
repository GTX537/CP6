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
        <el-button type="success" :icon="Plus" @click="openCreate">{{ t('platform.tenant.create') }}</el-button>
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

    <!-- 新建租户对话框 -->
    <el-dialog v-model="createVisible" :title="t('platform.tenant.create')" width="520px">
      <el-form ref="createFormRef" :model="createForm" :rules="createRules" label-width="120px">
        <el-form-item :label="t('platform.tenant.code')" prop="code">
          <el-input v-model="createForm.code" />
        </el-form-item>
        <el-form-item :label="t('platform.tenant.name')" prop="name">
          <el-input v-model="createForm.name" />
        </el-form-item>
        <el-form-item :label="t('platform.tenant.adminUser')" prop="adminUserName">
          <el-input v-model="createForm.adminUserName" />
        </el-form-item>
        <el-form-item :label="t('platform.tenant.expire')">
          <el-date-picker v-model="createForm.expire" type="date" value-format="YYYY-MM-DD" style="width: 100%" />
        </el-form-item>
        <el-form-item :label="t('platform.tenant.remark')">
          <el-input v-model="createForm.remark" type="textarea" :rows="2" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createVisible = false">{{ t('table.cancel') }}</el-button>
        <el-button type="primary" :loading="creating" @click="doCreate">{{ t('table.confirm') }}</el-button>
      </template>
    </el-dialog>

    <!-- 一次性临时密码对话框 -->
    <el-dialog v-model="tempPwdVisible" :title="t('platform.tenant.tempPassword')" width="480px" :close-on-click-modal="false">
      <el-alert :title="t('platform.tenant.tempPasswordHint')" type="warning" :closable="false" show-icon />
      <div class="temp-pwd-row">
        <span class="temp-pwd-label">{{ t('platform.tenant.adminUser') }}</span>
        <code>{{ createdResult?.adminUserName }}</code>
      </div>
      <div class="temp-pwd-row">
        <span class="temp-pwd-label">{{ t('platform.tenant.tempPassword') }}</span>
        <code class="temp-pwd-value">{{ createdResult?.tempPassword }}</code>
        <el-button size="small" :icon="CopyDocument" @click="copyTempPwd">{{ t('platform.copy') }}</el-button>
      </div>
      <template #footer>
        <el-button type="primary" @click="tempPwdVisible = false">{{ t('table.confirm') }}</el-button>
      </template>
    </el-dialog>

    <TenantManagementPanel ref="management" @changed="loadData" />
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { Search, Plus, CopyDocument } from '@element-plus/icons-vue'
import { ElMessage, type FormInstance, type FormRules } from 'element-plus'
import { tenantApi } from '@/api/platform/tenant'
import TenantManagementPanel from './TenantManagementPanel.vue'
import type { TenantRow, CreateTenantResult } from '@/types/platform/platform'

const { t } = useI18n()
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

// ── 新建 ────────────────────────────────────────────
const createVisible = ref(false)
const creating = ref(false)
const createFormRef = ref<FormInstance>()
const createForm = reactive({ code: '', name: '', adminUserName: '', expire: '' as string | null, remark: '' })
const createRules = reactive<FormRules>({
  code: [{ required: true, message: t('platform.tenant.code'), trigger: 'blur' }],
  name: [{ required: true, message: t('platform.tenant.name'), trigger: 'blur' }],
  adminUserName: [{ required: true, message: t('platform.tenant.adminUser'), trigger: 'blur' }]
})

const tempPwdVisible = ref(false)
const createdResult = ref<CreateTenantResult | null>(null)

function openCreate() {
  createForm.code = ''
  createForm.name = ''
  createForm.adminUserName = ''
  createForm.expire = ''
  createForm.remark = ''
  createVisible.value = true
}

async function doCreate() {
  if (!createFormRef.value) return
  await createFormRef.value.validate()
  creating.value = true
  try {
    const res = await tenantApi.create({
      code: createForm.code.trim(),
      name: createForm.name.trim(),
      adminUserName: createForm.adminUserName.trim(),
      expire: createForm.expire || null,
      remark: createForm.remark || null
    })
    createVisible.value = false
    createdResult.value = res
    tempPwdVisible.value = true
    loadData()
  } catch {
    // E-SEC-033（编码冲突）由 http.ts 拦截器统一提示
  } finally {
    creating.value = false
  }
}

async function copyTempPwd() {
  if (!createdResult.value) return
  try {
    await navigator.clipboard.writeText(createdResult.value.tempPassword)
    ElMessage.success(t('platform.copied'))
  } catch {
    ElMessage.warning(createdResult.value.tempPassword)
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
.pagination {
  display: flex;
  justify-content: flex-end;
  margin-top: 16px;
}
.temp-pwd-row {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-top: 14px;
}
.temp-pwd-label {
  width: 90px;
  color: #606266;
}
.temp-pwd-value {
  font-size: 16px;
  font-weight: 600;
  color: #b45309;
  letter-spacing: 0.04em;
}
</style>
