<template>
  <div class="twofa-settings-page">
    <!-- ── 自助 2FA 启停 ─────────────────────────────── -->
    <el-card shadow="never" class="twofa-settings-card" v-loading="loading">
      <template #header>
        <div class="twofa-settings-header">
          <h3 class="twofa-settings-title">{{ t('sec.2fa.title') }}</h3>
        </div>
      </template>

      <div class="twofa-status-row">
        <span class="twofa-status-label">{{ t('sec.2fa.title') }}</span>
        <el-tag :type="status?.enabled ? 'success' : 'info'" effect="light">
          {{ status?.enabled ? t('sec.2fa.status.on') : t('sec.2fa.status.off') }}
        </el-tag>
      </div>

      <!-- 未启用：启用入口（setup-self → 扫码 → enroll-self） -->
      <template v-if="status && !status.enabled">
        <el-button type="primary" :loading="enrolling" @click="startEnroll">
          {{ t('sec.2fa.enable') }}
        </el-button>

        <div v-if="enrollStarted" class="twofa-enroll-box">
          <p class="twofa-hint">{{ t('sec.2fa.scanQr') }}</p>
          <div class="twofa-qr">
            <img v-if="qrDataUrl" :src="qrDataUrl" alt="2FA QR" />
          </div>
          <div v-if="secret" class="twofa-secret">
            <code class="twofa-secret-code">{{ secret }}</code>
          </div>
          <el-input
            v-model="enrollCode"
            maxlength="8"
            :placeholder="t('sec.2fa.enterCode')"
            style="max-width: 240px; margin-top: 8px"
            @keyup.enter="confirmEnroll"
          />
          <div style="margin-top: 12px">
            <el-button type="primary" :loading="confirming" @click="confirmEnroll">
              {{ t('sec.2fa.submit') }}
            </el-button>
          </div>
        </div>
      </template>

      <!-- 已启用：关闭入口（仅 canDisable 时显示） -->
      <template v-else-if="status && status.enabled">
        <el-button
          v-if="status.canDisable"
          type="danger"
          plain
          @click="openDisable"
        >
          {{ t('sec.2fa.disable') }}
        </el-button>
        <el-alert
          v-else
          type="info"
          :closable="false"
          show-icon
          :title="t('E-SEC-019')"
          style="margin-top: 8px"
        />
      </template>
    </el-card>

    <!-- ── 租户 2FA 策略 ─────────────────────────────── -->
    <el-card shadow="never" class="twofa-settings-card" v-loading="policyLoading">
      <template #header>
        <div class="twofa-settings-header">
          <h3 class="twofa-settings-title">{{ t('sec.2fa.policyTitle') }}</h3>
        </div>
      </template>

      <el-form label-width="160px" label-position="right">
        <el-form-item :label="t('sec.2fa.policyTitle')">
          <el-select v-model="policyMode" style="width: 220px" @change="savePolicy">
            <el-option :label="t('sec.2fa.policy.off')" :value="TwoFactorMode.Off" />
            <el-option :label="t('sec.2fa.policy.optional')" :value="TwoFactorMode.Optional" />
            <el-option :label="t('sec.2fa.policy.required')" :value="TwoFactorMode.Required" />
          </el-select>
        </el-form-item>
      </el-form>
    </el-card>

    <!-- 关闭对话框：当前密码 + 验证码（TOTP/邮件） -->
    <el-dialog v-model="disableDialog" :title="t('sec.2fa.disable')" width="420px">
      <el-form label-position="top">
        <el-form-item :label="t('sec.2fa.currentPassword')">
          <el-input v-model="disablePassword" type="password" show-password />
        </el-form-item>
        <el-form-item :label="t('sec.2fa.enterCode')">
          <el-input v-model="disableCode" maxlength="8" />
          <div class="twofa-method-switch">
            <el-radio-group v-model="disableMethod" size="small">
              <el-radio-button label="totp">TOTP</el-radio-button>
              <el-radio-button label="email">{{ t('sec.2fa.useEmail') }}</el-radio-button>
            </el-radio-group>
            <el-button
              v-if="disableMethod === 'email'"
              link
              type="primary"
              :loading="sendingOtp"
              @click="sendDisableOtp"
            >
              {{ t('sec.2fa.sendEmailCode') }}
            </el-button>
          </div>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="disableDialog = false">{{ t('sec.2fa.policy.off') }}</el-button>
        <el-button type="danger" :loading="disabling" @click="confirmDisable">
          {{ t('sec.2fa.disable') }}
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import QRCode from 'qrcode'
import { twoFactorApi } from '@/api/sys/twoFactor'
import { TwoFactorMode, type TwoFactorStatus, type TwoFactorMethod } from '@/types/sys/twoFactor'

const { t } = useI18n()

const loading = ref(false)
const status = ref<TwoFactorStatus | null>(null)

// 启用流
const enrolling = ref(false)
const enrollStarted = ref(false)
const confirming = ref(false)
const secret = ref('')
const qrDataUrl = ref('')
const enrollCode = ref('')

// 关闭流
const disableDialog = ref(false)
const disablePassword = ref('')
const disableCode = ref('')
const disableMethod = ref<TwoFactorMethod>('totp')
const disabling = ref(false)
const sendingOtp = ref(false)

// 租户策略
const policyLoading = ref(false)
const policyMode = ref<TwoFactorMode>(TwoFactorMode.Off)

async function loadStatus() {
  loading.value = true
  try {
    status.value = await twoFactorApi.status()
  } finally {
    loading.value = false
  }
}

async function loadPolicy() {
  policyLoading.value = true
  try {
    const p = await twoFactorApi.getPolicy()
    policyMode.value = p.mode
  } finally {
    policyLoading.value = false
  }
}

async function startEnroll() {
  enrolling.value = true
  try {
    const res = await twoFactorApi.setupSelf()
    secret.value = res.secret
    qrDataUrl.value = await QRCode.toDataURL(res.otpauthUri, { width: 200, margin: 1 })
    enrollStarted.value = true
  } catch {
    // E-SEC-017 等由拦截器统一提示
  } finally {
    enrolling.value = false
  }
}

async function confirmEnroll() {
  if (!enrollCode.value.trim()) {
    ElMessage.warning(t('sec.2fa.enterCode'))
    return
  }
  confirming.value = true
  try {
    await twoFactorApi.enrollSelf({ code: enrollCode.value.trim() })
    ElMessage.success(t('sec.2fa.status.on'))
    enrollStarted.value = false
    enrollCode.value = ''
    await loadStatus()
  } catch {
    // E-SEC-011 由拦截器统一提示
  } finally {
    confirming.value = false
  }
}

function openDisable() {
  disablePassword.value = ''
  disableCode.value = ''
  disableMethod.value = 'totp'
  disableDialog.value = true
}

async function sendDisableOtp() {
  sendingOtp.value = true
  try {
    await twoFactorApi.emailOtpSelf()
    ElMessage.success(t('sec.2fa.emailSent'))
  } catch {
    // E-SEC-015/016/018 由拦截器统一提示
  } finally {
    sendingOtp.value = false
  }
}

async function confirmDisable() {
  if (!disablePassword.value || !disableCode.value.trim()) {
    ElMessage.warning(t('sec.2fa.enterCode'))
    return
  }
  disabling.value = true
  try {
    await twoFactorApi.disableSelf({
      currentPassword: disablePassword.value,
      code: disableCode.value.trim(),
      method: disableMethod.value,
    })
    ElMessage.success(t('sec.2fa.status.off'))
    disableDialog.value = false
    await loadStatus()
  } catch {
    // E-SEC-006/019/011 由拦截器统一提示
  } finally {
    disabling.value = false
  }
}

async function savePolicy(mode: TwoFactorMode) {
  policyLoading.value = true
  try {
    await twoFactorApi.setPolicy({ mode })
    ElMessage.success(t('sec.sso.saveSuccess'))
  } catch {
    // E-SEC-012 由拦截器统一提示；失败则回读真实值
    await loadPolicy()
  } finally {
    policyLoading.value = false
  }
}

onMounted(() => {
  loadStatus()
  loadPolicy()
})
</script>

<style scoped>
.twofa-settings-page {
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}
.twofa-settings-card {
  max-width: 640px;
}
.twofa-settings-title {
  margin: 0;
  font-size: 1.05rem;
}
.twofa-status-row {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 16px;
}
.twofa-status-label {
  color: var(--el-text-color-secondary);
}
.twofa-enroll-box {
  margin-top: 16px;
  padding: 16px;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 8px;
}
.twofa-hint {
  margin: 0 0 12px;
  color: #606266;
  font-size: 0.9rem;
}
.twofa-qr img {
  border: 1px solid var(--el-border-color);
  border-radius: 8px;
}
.twofa-secret {
  margin: 8px 0;
}
.twofa-secret-code {
  font-family: monospace;
  letter-spacing: 0.1em;
  word-break: break-all;
}
.twofa-method-switch {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-top: 8px;
}
</style>
