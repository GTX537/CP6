<template>
  <div>
    <div class="page-head">
      <h2 class="page-title">{{ t('platform.impersonation.title') }}</h2>
    </div>

    <el-card shadow="never" class="imp-card">
      <el-form :model="form" label-width="160px" style="max-width: 640px">
        <el-form-item :label="t('platform.impersonation.selectTenant')">
          <el-select
            v-model="form.tenantId"
            filterable
            clearable
            style="width: 100%"
            :loading="tenantsLoading"
          >
            <el-option
              v-for="tn in tenants"
              :key="tn.id"
              :label="tn.tenantCode + ' — ' + tn.tenantName"
              :value="tn.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('platform.impersonation.selectUser')">
          <el-input v-model="form.userId" :placeholder="'userId (optional)'" clearable />
        </el-form-item>
        <el-form-item :label="t('platform.impersonation.reason')">
          <el-input v-model="form.reason" type="textarea" :rows="2" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :disabled="!form.tenantId" :loading="starting" @click="doStart">
            {{ t('platform.impersonation.start') }}
          </el-button>
        </el-form-item>
      </el-form>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { tenantApi } from '@/api/platform/tenant'
import { impersonationApi } from '@/api/platform/impersonation'
import { usePlatformStore } from '@/stores/platform'
import { addDynamicRoutes } from '@/router'
import type { TenantRow } from '@/types/platform/platform'

const { t } = useI18n()
const router = useRouter()
const store = usePlatformStore()

const tenants = ref<TenantRow[]>([])
const tenantsLoading = ref(false)
const starting = ref(false)
const form = reactive<{ tenantId: string; userId: string; reason: string }>({
  tenantId: '',
  userId: '',
  reason: ''
})

async function loadTenants() {
  tenantsLoading.value = true
  try {
    const res = await tenantApi.list({ enable: true, page: 1, pageSize: 200 })
    tenants.value = res.rows
  } finally {
    tenantsLoading.value = false
  }
}

async function doStart() {
  if (!form.tenantId) return
  starting.value = true
  try {
    const res = await impersonationApi.start({
      tenantId: form.tenantId,
      userId: form.userId.trim() || null,
      reason: form.reason.trim() || null
    })
    // R8：以目标用户身份建立 imp 会话 → sessionStorage 态 + 替换 localStorage menus + 重建路由 + 进首页。
    store.setImpersonation({
      tenantName: res.tenantName,
      userName: res.userName,
      expiresAt: Date.now() + res.expiresInMinutes * 60000
    })
    const menus = res.menus || []
    localStorage.setItem('menus', JSON.stringify(menus))
    addDynamicRoutes(menus)
    ElMessage.success(t('platform.impersonation.bannerActive', {
      tenantName: res.tenantName,
      userName: res.userName
    }))
    router.push('/dashboard')
    // 路由表已重建（addDynamicRoutes 重设 layout children + 平台区）；刷新以挂回目标用户菜单 + 横幅。
    window.location.reload()
  } catch {
    // E-SEC-032 / E-SEC-035 由拦截器提示
  } finally {
    starting.value = false
  }
}

onMounted(() => loadTenants())
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
.imp-card {
  max-width: 760px;
}
</style>
