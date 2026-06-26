<template>
  <div>
    <div class="page-head">
      <h2 class="page-title">{{ t('platform.gdpr.title') }}</h2>
    </div>

    <el-tabs v-model="activeTab" class="gdpr-tabs">
      <!-- 按租户导出 -->
      <el-tab-pane :label="t('platform.gdpr.exportTenant')" name="exportTenant">
        <el-form label-width="140px" style="max-width: 560px">
          <el-form-item :label="t('platform.audit.tenantCode')">
            <el-input v-model="exportTenantId" :placeholder="'tenantId'" clearable />
          </el-form-item>
          <el-form-item>
            <el-button type="primary" :disabled="!exportTenantId.trim()" :loading="busy" @click="doExportTenant">
              {{ t('platform.gdpr.exportTenant') }}
            </el-button>
          </el-form-item>
        </el-form>
      </el-tab-pane>

      <!-- 按主体导出 -->
      <el-tab-pane :label="t('platform.gdpr.exportSubject')" name="exportSubject">
        <el-form label-width="140px" style="max-width: 560px">
          <el-form-item :label="t('platform.tenant.adminUser')">
            <el-input v-model="exportSubjectId" :placeholder="'userId'" clearable />
          </el-form-item>
          <el-form-item>
            <el-button type="primary" :disabled="!exportSubjectId.trim()" :loading="busy" @click="doExportSubject">
              {{ t('platform.gdpr.exportSubject') }}
            </el-button>
          </el-form-item>
        </el-form>
      </el-tab-pane>

      <!-- 按主体擦除 -->
      <el-tab-pane :label="t('platform.gdpr.eraseSubject')" name="eraseSubject">
        <el-form label-width="140px" style="max-width: 560px">
          <el-form-item :label="t('platform.tenant.adminUser')">
            <el-input v-model="eraseSubjectId" :placeholder="'userId'" clearable />
          </el-form-item>
          <el-form-item>
            <el-button type="danger" :disabled="!eraseSubjectId.trim()" :loading="busy" @click="doEraseSubject">
              {{ t('platform.gdpr.eraseSubject') }}
            </el-button>
          </el-form-item>
        </el-form>
      </el-tab-pane>

      <!-- 按租户擦除 -->
      <el-tab-pane :label="t('platform.gdpr.eraseTenant')" name="eraseTenant">
        <el-form label-width="140px" style="max-width: 560px">
          <el-form-item :label="t('platform.audit.tenantCode')">
            <el-input v-model="eraseTenantId" :placeholder="'tenantId'" clearable />
          </el-form-item>
          <el-form-item :label="t('platform.gdpr.confirm')">
            <el-radio-group v-model="eraseMode">
              <el-radio label="anonymize">{{ t('platform.gdpr.modeAnonymize') }}</el-radio>
              <el-radio label="purge">{{ t('platform.gdpr.modePurge') }}</el-radio>
            </el-radio-group>
          </el-form-item>
          <el-form-item>
            <el-button type="danger" :disabled="!eraseTenantId.trim()" :loading="busy" @click="doEraseTenant">
              {{ t('platform.gdpr.eraseTenant') }}
            </el-button>
          </el-form-item>
        </el-form>
      </el-tab-pane>
    </el-tabs>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { gdprApi } from '@/api/platform/gdpr'

const { t } = useI18n()

const activeTab = ref('exportTenant')
const busy = ref(false)

const exportTenantId = ref('')
const exportSubjectId = ref('')
const eraseSubjectId = ref('')
const eraseTenantId = ref('')
const eraseMode = ref<'anonymize' | 'purge'>('anonymize')

/** 破坏性操作二次确认：要求输入 CONFIRM。返回 true=确认通过。 */
async function confirmDestructive(): Promise<boolean> {
  try {
    const { value } = await ElMessageBox.prompt(t('platform.gdpr.confirmHint'), t('platform.gdpr.confirm'), {
      type: 'warning',
      inputPattern: /^CONFIRM$/,
      inputErrorMessage: t('platform.gdpr.confirmHint')
    })
    return value === 'CONFIRM'
  } catch {
    return false // 取消
  }
}

async function doExportTenant() {
  busy.value = true
  try {
    await gdprApi.exportTenant(exportTenantId.value.trim())
  } catch {
    // E-SEC-032 由拦截器提示（注：blob 错误响应拦截器需能解析，见 http.ts）
  } finally {
    busy.value = false
  }
}

async function doExportSubject() {
  busy.value = true
  try {
    await gdprApi.exportSubject(exportSubjectId.value.trim())
  } catch {
    // E-SEC-032
  } finally {
    busy.value = false
  }
}

async function doEraseSubject() {
  if (!(await confirmDestructive())) return
  busy.value = true
  try {
    await gdprApi.eraseSubject(eraseSubjectId.value.trim())
    ElMessage.success(t('platform.saved'))
  } catch {
    // E-SEC-032 / 036 / 037 由拦截器提示
  } finally {
    busy.value = false
  }
}

async function doEraseTenant() {
  if (!(await confirmDestructive())) return
  busy.value = true
  try {
    await gdprApi.eraseTenant(eraseTenantId.value.trim(), eraseMode.value)
    ElMessage.success(t('platform.saved'))
  } catch {
    // E-SEC-032 / 036 / 038 / NotSupported(purge on non-relational) 由拦截器提示
  } finally {
    busy.value = false
  }
}
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
.gdpr-tabs {
  max-width: 760px;
}
</style>
