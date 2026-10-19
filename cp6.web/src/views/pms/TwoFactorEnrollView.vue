<template>
  <div class="twofa-shell">
    <el-card class="twofa-card" shadow="never" v-loading="initializing">
      <h2 class="twofa-title">{{ t('sec.2fa.enrollTitle') }}</h2>
      <p class="twofa-hint">{{ t('sec.2fa.scanQr') }}</p>

      <div class="twofa-qr">
        <img v-if="qrDataUrl" :src="qrDataUrl" alt="2FA QR" />
      </div>

      <div v-if="secret" class="twofa-secret">
        <span class="twofa-secret-label">{{ t('sec.2fa.title') }}</span>
        <code class="twofa-secret-code">{{ secret }}</code>
      </div>

      <el-form @submit.prevent>
        <el-form-item>
          <el-input
            v-model="code"
            size="large"
            maxlength="8"
            :placeholder="t('sec.2fa.enterCode')"
            @keyup.enter="handleEnroll"
          />
        </el-form-item>
        <el-form-item>
          <el-button
            type="primary"
            size="large"
            style="width: 100%"
            :loading="loading"
            @click="handleEnroll"
          >
            {{ t('sec.2fa.submit') }}
          </el-button>
        </el-form-item>
      </el-form>
      <p v-if="setupError" role="alert">{{ setupError }}</p>
      <PendingAuthenticationActions />
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import QRCode from 'qrcode'
import { twoFactorApi } from '@/api/sys/twoFactor'
import { navigateAuthentication } from '@/auth/navigation'
import PendingAuthenticationActions from '@/auth/PendingAuthenticationActions.vue'

const { t } = useI18n()
const router = useRouter()

const code = ref('')
const secret = ref('')
const qrDataUrl = ref('')
const initializing = ref(true)
const loading = ref(false)
const setupError = ref('')

onMounted(async () => {
  try {
    const res = await twoFactorApi.setup()
    secret.value = res.secret
    qrDataUrl.value = await QRCode.toDataURL(res.otpauthUri, { width: 200, margin: 1 })
  } catch (error: any) {
    setupError.value = error?.response?.data?.message ? t(error.response.data.message) : error?.message || '绑定材料生成结果未确认，请检查状态。'
  } finally {
    initializing.value = false
  }
})


async function handleEnroll() {
  if (!code.value.trim()) {
    ElMessage.warning(t('sec.2fa.enterCode'))
    return
  }
  loading.value = true
  try {
    const res: any = await twoFactorApi.enroll({ code: code.value.trim() })
    navigateAuthentication(res)
  } catch (error: any) {
    ElMessage.error(error?.response?.data?.message ? t(error.response.data.message) : error?.message || '绑定结果未确认，请检查状态。')
  } finally {
    code.value = ''
    loading.value = false
  }
}
</script>

<style scoped>
.twofa-shell {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 100vh;
  min-height: 100dvh;
  padding: 2rem;
  background: linear-gradient(135deg, #07111f 0%, #0b1d34 42%, #12345d 100%);
}
.twofa-card {
  width: min(100%, 420px);
  border-radius: 18px;
}
.twofa-title {
  margin: 0 0 0.6rem;
  font-size: 1.5rem;
  color: #303133;
}
.twofa-hint {
  margin: 0 0 1rem;
  color: #606266;
  font-size: 0.9rem;
}
.twofa-qr {
  display: flex;
  justify-content: center;
  margin-bottom: 1rem;
  min-height: 200px;
  align-items: center;
}
.twofa-qr img {
  border: 1px solid var(--el-border-color);
  border-radius: 8px;
}
.twofa-secret {
  display: flex;
  flex-direction: column;
  gap: 0.3rem;
  margin-bottom: 1.2rem;
  text-align: center;
}
.twofa-secret-label {
  color: #909399;
  font-size: 0.78rem;
}
.twofa-secret-code {
  font-family: monospace;
  font-size: 1rem;
  letter-spacing: 0.12em;
  color: #303133;
  word-break: break-all;
}
</style>
