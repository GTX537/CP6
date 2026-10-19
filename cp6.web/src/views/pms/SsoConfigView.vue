<template>
  <div class="sso-config-page">
    <el-card shadow="never" class="sso-config-card" v-loading="loading">
      <template #header>
        <div class="sso-config-header">
          <h3 class="sso-config-title">{{ t('sec.sso.settingsTitle') }}</h3>
          <p class="sso-config-subtitle">{{ t('sec.sso.settingsSubtitle') }}</p>
        </div>
      </template>

      <el-alert v-if="notice" :title="notice" type="warning" :closable="false" role="status" />
      <div v-if="pending" class="recovery-actions">
        <p>上次保存结果尚未确认。请查询原操作；只有确认已提交或已关闭后才能再次保存。</p>
        <el-button :loading="saving" @click="recover(false)">查询原操作</el-button>
        <el-button :loading="saving" @click="recover(true)">关闭原操作后重试</el-button>
      </div>
      <el-form ref="formRef" :model="form" :rules="rules" :disabled="saving || loading || !!pending || committed || !scope" label-width="220px" label-position="right">
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
          <el-divider>首次 SSO 身份准入</el-divider>
        </el-form-item>
        <el-form-item label="当前批准">
          <span v-if="admissionPolicy">颁发者：{{ admissionPolicy.approvedIssuer }}；邮箱初绑：{{ admissionPolicy.allowInitialEmailLink ? '允许' : '关闭' }}；自动建号：{{ admissionPolicy.allowJit ? '允许' : '关闭' }}</span>
          <span v-else>尚未批准首次邮箱绑定或自动建号</span>
        </el-form-item>
        <el-form-item label="本次操作">
          <el-select v-model="form.admission.action">
            <el-option label="保留当前批准" value="preserve" />
            <el-option label="明确批准新的准入政策" value="approve" />
            <el-option label="撤销首次身份准入批准" value="revoke" />
          </el-select>
        </el-form-item>
        <template v-if="form.admission.action === 'approve'">
          <el-form-item label="批准的精确 issuer">
            <el-input v-model="form.admission.approvedIssuer" placeholder="填写 IdP 令牌中的精确 issuer" />
          </el-form-item>
          <el-form-item label="邮箱权威确认">
            <el-checkbox v-model="form.admission.issuerIsEmailAuthority">我确认该颁发者可代表本租户证明此邮箱 claim，且首次准入必须 email_verified=true</el-checkbox>
          </el-form-item>
          <el-form-item label="允许首次绑定现有邮箱">
            <el-switch v-model="form.admission.allowInitialEmailLink" />
          </el-form-item>
          <el-form-item label="允许首次自动建号">
            <el-switch v-model="form.admission.allowJit" />
          </el-form-item>
          <el-form-item label="批准的自动建号角色">
            <el-select v-model="form.admission.approvedJitRoleIds" multiple filterable>
              <el-option v-for="r in roleOptions" :key="r.value" :label="r.label" :value="r.value" />
            </el-select>
          </el-form-item>
        </template>
        <el-form-item label="修改原因" prop="reason">
          <el-input v-model="form.reason" maxlength="256" type="textarea" />
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
import { ref, reactive, computed, onMounted, onUnmounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, type FormInstance, type FormRules } from 'element-plus'
import { ssoApi } from '@/api/sys/sso'
import { roleApi } from '@/api/sys/role'
import type { SsoConfigInput, SsoAdmissionPolicy } from '@/types/sys/sso'
import { pendingSsoConfiguration, saveSsoConfiguration, resolveSsoConfiguration, type SsoConfigScope } from '@/auth/ssoConfigurationRecovery'

const { t } = useI18n()
const formRef = ref<FormInstance>()
const loading = ref(false)
const saving = ref(false)
const hasClientSecret = ref(false)
const scope = ref<SsoConfigScope>()
const admissionPolicy = ref<SsoAdmissionPolicy | null>(null)
const pending = ref<string | null>(null)
const notice = ref('')
const committed = ref(false)
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
  defaultRoleId: null,
  expectedConfigVersion: '', expectedPolicyVersion: '', operationId: '', reason: '',
  admission: { action: 'preserve', approvedIssuer: '', issuerIsEmailAuthority: false, emailClaimSemantic: 'verified-email',
    allowInitialEmailLink: false, allowJit: false, approvedJitRoleIds: [] }
})

const rules = computed<FormRules>(() => ({
  authority: [{ required: true, message: t('sec.sso.authority'), trigger: 'blur' }],
  clientId: [{ required: true, message: t('sec.sso.clientId'), trigger: 'blur' }],
  scopes: [{ required: true, message: t('sec.sso.scopes'), trigger: 'blur' }],
  emailClaim: [{ required: true, message: t('sec.sso.emailClaim'), trigger: 'blur' }],
  reason: [{ required: true, whitespace: true, message: '请填写修改原因', trigger: 'blur' }]
}))

async function loadConfig() {
  loading.value = true
  try {
    const cfg = await ssoApi.getConfig()
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
    scope.value = { tenantId: cfg.tenantId, actorId: cfg.actorId }
    form.expectedConfigVersion = cfg.configVersion; form.expectedPolicyVersion = cfg.policyVersion
    form.expectedPolicyRef = cfg.admissionPolicy?.policyRef ?? null
    admissionPolicy.value = cfg.admissionPolicy; syncPending()
    committed.value = false
  } catch {
    scope.value = undefined; notice.value = '配置读取失败，请确认登录状态后重新打开页面。'
  } finally {
    loading.value = false
  }
}

async function loadRoles() {
  const res: any = await roleApi.getAll()
  roleOptions.value = (res || []).filter((r: any) => r.enable === true).map((r: any) => ({ label: r.roleName, value: r.roleId }))
}

async function handleSave() {
  if (!formRef.value || !scope.value || saving.value || committed.value || pending.value) return
  await formRef.value.validate()
  // 自动供给必配默认角色（spec §4.2 R5：防误授 admin）
  if (form.autoProvision && !form.defaultRoleId) {
    ElMessage.warning(t('sec.sso.defaultRole'))
    return
  }
  if (form.admission.action === 'approve' && (!form.admission.issuerIsEmailAuthority || !form.admission.approvedIssuer?.trim())) {
    ElMessage.warning('请明确填写并确认邮箱权威颁发者'); return
  }
  saving.value = true
  try {
    const payload: SsoConfigInput = { ...form }
    // clientSecret 留空 → 不发送，保留原密文
    if (!payload.clientSecret) delete payload.clientSecret
    await saveSsoConfiguration(scope.value, payload)
    committed.value = true; notice.value = '配置已提交。认证政策已更新，请重新登录后继续操作。'
    ElMessage.success(t('sec.sso.saveSuccess'))
  } catch {
    notice.value = '尚未确认保存结果。若登录已失效，请重新登录后回到此页查询原操作；请勿另行重复保存。'
  } finally {
    form.clientSecret = ''; syncPending()
    saving.value = false
  }
}

function syncPending() {
  if (!scope.value) return
  try { pending.value = pendingSsoConfiguration(scope.value) }
  catch { notice.value = '本地恢复记录不可读，暂不能安全保存。'; scope.value = undefined }
}
async function recover(close: boolean) {
  if (!scope.value || saving.value) return
  saving.value = true
  try {
    const result = await resolveSsoConfiguration(scope.value, close)
    if (result.state === 'UNKNOWN') notice.value = '原操作结果仍未知，可以关闭原操作以阻止迟到保存。'
    else {
      notice.value = result.state === 'COMMITTED' ? '已确认原操作提交成功。请读取最新配置后再修改。' : '原操作已关闭，可以重新填写并保存。'
      form.admission.action = 'preserve'; form.admission.issuerIsEmailAuthority = false; form.reason = ''; form.clientSecret = ''
      await loadConfig()
    }
  } catch { notice.value = '查询或关闭尚未确认，请恢复登录与连接后继续查询同一原操作。' }
  finally { syncPending(); saving.value = false }
}

onMounted(() => {
  loadConfig()
  loadRoles().catch(() => { notice.value = '角色读取失败，暂不能批准自动建号角色。' })
  window.addEventListener('storage', syncPending)
  window.addEventListener('focus', syncPending)
})
onUnmounted(() => { window.removeEventListener('storage', syncPending); window.removeEventListener('focus', syncPending) })
</script>

<style scoped>
.sso-config-page {
  padding: 16px;
}
.sso-config-card {
  max-width: 820px;
}
.sso-config-card :deep(.el-checkbox) {
  height: auto;
  align-items: flex-start;
}
.sso-config-card :deep(.el-checkbox__label) {
  white-space: normal;
  line-height: 1.5;
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
