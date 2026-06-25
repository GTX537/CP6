<template>
  <div class="sso-config-page">
    <el-card shadow="never" class="sso-config-card" v-loading="loading">
      <template #header>
        <div class="sso-config-header">
          <h3 class="sso-config-title">{{ t('sec.sso.settingsTitle') }}</h3>
          <p class="sso-config-subtitle">{{ t('sec.sso.settingsSubtitle') }}</p>
        </div>
      </template>

      <el-form ref="formRef" :model="form" :rules="rules" label-width="220px" label-position="right">
        <el-form-item :label="t('sec.sso.enabled')">
          <el-switch v-model="form.enabled" />
        </el-form-item>
        <el-form-item :label="t('sec.sso.authority')" prop="authority">
          <el-input v-model="form.authority" placeholder="https://idp.example.com" />
        </el-form-item>
        <el-form-item :label="t('sec.sso.clientId')" prop="clientId">
          <el-input v-model="form.clientId" />
        </el-form-item>
        <el-form-item :label="t('sec.sso.clientSecret')">
          <el-input
            v-model="form.clientSecret"
            type="password"
            show-password
            :placeholder="hasClientSecret ? t('sec.sso.secretSetHint') : ''"
          />
        </el-form-item>
        <el-form-item :label="t('sec.sso.scopes')" prop="scopes">
          <el-input v-model="form.scopes" placeholder="openid profile email" />
        </el-form-item>
        <el-form-item :label="t('sec.sso.emailClaim')" prop="emailClaim">
          <el-input v-model="form.emailClaim" placeholder="email" />
        </el-form-item>
        <el-form-item :label="t('sec.sso.enforced')">
          <el-switch v-model="form.enforced" />
        </el-form-item>
        <el-form-item :label="t('sec.sso.autoProvision')">
          <el-switch v-model="form.autoProvision" />
        </el-form-item>
        <el-form-item :label="t('sec.sso.defaultRole')" :required="form.autoProvision">
          <el-select v-model="form.defaultRoleId" clearable filterable style="width: 100%">
            <el-option
              v-for="r in roleOptions"
              :key="r.value"
              :label="r.label"
              :value="r.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :loading="saving" @click="handleSave">
            {{ t('sec.sso.save') }}
          </el-button>
        </el-form-item>
      </el-form>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, type FormInstance, type FormRules } from 'element-plus'
import { ssoApi } from '@/api/sys/sso'
import { roleApi } from '@/api/sys/role'
import type { SsoConfigInput } from '@/types/sys/sso'

const { t } = useI18n()
const formRef = ref<FormInstance>()
const loading = ref(false)
const saving = ref(false)
const hasClientSecret = ref(false)
const roleOptions = ref<{ label: string; value: number }[]>([])

const form = reactive<SsoConfigInput>({
  authority: '',
  clientId: '',
  clientSecret: '',
  scopes: 'openid profile email',
  emailClaim: 'email',
  enabled: false,
  enforced: false,
  autoProvision: false,
  defaultRoleId: null
})

const rules = computed<FormRules>(() => ({
  authority: [{ required: true, message: t('sec.sso.authority'), trigger: 'blur' }],
  clientId: [{ required: true, message: t('sec.sso.clientId'), trigger: 'blur' }],
  scopes: [{ required: true, message: t('sec.sso.scopes'), trigger: 'blur' }],
  emailClaim: [{ required: true, message: t('sec.sso.emailClaim'), trigger: 'blur' }]
}))

async function loadConfig() {
  loading.value = true
  try {
    const cfg: any = await ssoApi.getConfig()
    form.authority = cfg.authority || ''
    form.clientId = cfg.clientId || ''
    form.clientSecret = '' // 投影绝不返密文；留空=不修改
    form.scopes = cfg.scopes || 'openid profile email'
    form.emailClaim = cfg.emailClaim || 'email'
    form.enabled = !!cfg.enabled
    form.enforced = !!cfg.enforced
    form.autoProvision = !!cfg.autoProvision
    form.defaultRoleId = cfg.defaultRoleId ?? null
    hasClientSecret.value = !!cfg.hasClientSecret
  } finally {
    loading.value = false
  }
}

async function loadRoles() {
  const res: any = await roleApi.getAll()
  roleOptions.value = (res || []).map((r: any) => ({ label: r.roleName, value: r.roleId }))
}

async function handleSave() {
  if (!formRef.value) return
  await formRef.value.validate()
  // 自动供给必配默认角色（spec §4.2 R5：防误授 admin）
  if (form.autoProvision && !form.defaultRoleId) {
    ElMessage.warning(t('sec.sso.defaultRole'))
    return
  }
  saving.value = true
  try {
    const payload: SsoConfigInput = { ...form }
    // clientSecret 留空 → 不发送，保留原密文
    if (!payload.clientSecret) delete payload.clientSecret
    await ssoApi.setConfig(payload)
    ElMessage.success(t('sec.sso.saveSuccess'))
    await loadConfig()
  } finally {
    saving.value = false
  }
}

onMounted(() => {
  loadConfig()
  loadRoles()
})
</script>

<style scoped>
.sso-config-page {
  padding: 16px;
}
.sso-config-card {
  max-width: 820px;
}
.sso-config-title {
  margin: 0;
  font-size: 1.05rem;
}
.sso-config-subtitle {
  margin: 6px 0 0;
  color: var(--el-text-color-secondary);
  font-size: 0.85rem;
}
</style>
