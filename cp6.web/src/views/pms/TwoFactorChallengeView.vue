<template>
  <div class="twofa-shell">
    <el-card class="twofa-card" shadow="never">
      <h2 class="twofa-title">{{ t('sec.2fa.challengeTitle') }}</h2>

      <p class="twofa-hint">
        {{ method === 'email' ? t('sec.2fa.emailSent') : t('sec.2fa.enterCode') }}
      </p>

      <el-form @submit.prevent>
        <el-form-item>
          <el-input
            v-model="code"
            size="large"
            maxlength="8"
            :placeholder="t('sec.2fa.enterCode')"
            autofocus
            @keyup.enter="handleVerify"
          />
        </el-form-item>
        <el-form-item>
          <el-button
            type="primary"
            size="large"
            style="width: 100%"
            :loading="loading"
            @click="handleVerify"
          >
            {{ t('sec.2fa.verify') }}
          </el-button>
        </el-form-item>
      </el-form>

      <div class="twofa-switch">
        <button
          v-if="method === 'totp'"
          type="button"
          class="twofa-link"
          :disabled="sending"
          @click="switchToEmail"
        >
          {{ sending ? t('sec.2fa.sendEmailCode') : t('sec.2fa.useEmail') }}
        </button>
        <button
          v-else
          type="button"
          class="twofa-link"
          :disabled="sending"
          @click="resendEmail"
        >
          {{ t('sec.2fa.sendEmailCode') }}
        </button>
      </div>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { twoFactorApi } from '@/api/sys/twoFactor'
import type { TwoFactorMethod } from '@/types/sys/twoFactor'
import { addDynamicRoutes } from '@/router'

const { t } = useI18n()
const router = useRouter()

const code = ref('')
const method = ref<TwoFactorMethod>('totp')
const loading = ref(false)
const sending = ref(false)

// 与 LoginView 登录成功后处理对称：置非敏感登录态标志 + 存用户/菜单 + 挂动态路由 + 进首页。
function completeLogin(res: any) {
  localStorage.setItem('cp6_authed', '1')
  localStorage.setItem('cp6_mustChangePwd', res.mustChangePassword ? '1' : '')
  localStorage.setItem('userName', res.userName)
  localStorage.setItem('nickName', res.nickName || res.userName)
  const menus = res.menus || []
  localStorage.setItem('menus', JSON.stringify(menus))
  addDynamicRoutes(menus)
  router.push(res.mustChangePassword ? '/sys/change-password' : '/')
}

async function handleVerify() {
  if (!code.value.trim()) {
    ElMessage.warning(t('sec.2fa.enterCode'))
    return
  }
  loading.value = true
  try {
    const res: any = await twoFactorApi.verify({ code: code.value.trim(), method: method.value })
    completeLogin(res)
  } catch {
    // 错误（E-SEC-011/013/002 等）由 http.ts 拦截器统一提示
  } finally {
    loading.value = false
  }
}

// 切换到邮件验证：先发 OTP，成功后切到 email 模式。
async function switchToEmail() {
  sending.value = true
  try {
    await twoFactorApi.emailOtp()
    method.value = 'email'
    code.value = ''
    ElMessage.success(t('sec.2fa.emailSent'))
  } catch {
    // E-SEC-014/015/016/018 由拦截器统一提示
  } finally {
    sending.value = false
  }
}

async function resendEmail() {
  sending.value = true
  try {
    await twoFactorApi.emailOtp()
    ElMessage.success(t('sec.2fa.emailSent'))
  } catch {
    // 同上
  } finally {
    sending.value = false
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
  margin: 0 0 1.2rem;
  color: #606266;
  font-size: 0.9rem;
}
.twofa-switch {
  text-align: center;
  margin-top: 0.4rem;
}
.twofa-link {
  border: 0;
  background: transparent;
  color: var(--el-color-primary);
  cursor: pointer;
  font-size: 0.88rem;
  padding: 0.2rem 0.4rem;
}
.twofa-link:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
.twofa-link:hover:not(:disabled) {
  text-decoration: underline;
}
</style>
