<template>
  <div class="login-shell" :style="shellStyle" @pointermove="handlePointerMove" @pointerleave="handlePointerLeave">
    <div class="login-ambient">
      <span class="blob blob-a"></span>
      <span class="blob blob-b"></span>
      <span class="blob blob-c"></span>
      <span class="wave-ring wave-ring-a"></span>
      <span class="wave-ring wave-ring-b"></span>
      <span
        v-for="particle in particles"
        :key="particle.id"
        class="float-particle"
        :style="particle.style"
      ></span>
      <span class="grid-mask"></span>
    </div>

    <section class="login-panel" :class="{ 'is-success': loginSuccess }">
      <el-card class="login-card" shadow="never" :style="cardStyle" :class="{ 'is-success': loginSuccess }">
        <div class="card-glow"></div>
        <div class="success-burst"></div>
        <div class="card-header">
          <div class="brand-chip">CP6</div>
          <p class="card-eyebrow">{{ $t('login.welcomeBack') }}</p>
          <h2 class="login-title">{{ $t('login.title') }}</h2>
          <p class="login-subtitle">{{ $t('login.subtitle') }}</p>
        </div>

        <el-form ref="formRef" :model="form" :rules="rules" label-width="0" class="login-form">
          <el-form-item prop="userName">
            <el-input
              v-model="form.userName"
              :placeholder="$t('login.username')"
              :prefix-icon="User"
              size="large"
            />
          </el-form-item>
          <el-form-item prop="password">
            <el-input
              v-model="form.password"
              :type="passwordVisible ? 'text' : 'password'"
              :placeholder="$t('login.password')"
              :prefix-icon="Lock"
              size="large"
              @keyup.enter="handleLogin"
            >
              <template #suffix>
                <button
                  type="button"
                  class="password-toggle"
                  :aria-label="passwordVisible ? 'Hide password' : 'Show password'"
                  @click="passwordVisible = !passwordVisible"
                >
                  <el-icon>
                    <component :is="passwordVisible ? View : Hide" />
                  </el-icon>
                </button>
              </template>
            </el-input>
          </el-form-item>
          <el-form-item v-if="showTenant" prop="tenantCode">
            <el-input
              v-model="form.tenantCode"
              :placeholder="$t('login.tenantCode')"
              :prefix-icon="OfficeBuilding"
              size="large"
              @keyup.enter="handleLogin"
            />
          </el-form-item>
          <div v-else class="tenant-toggle-row">
            <button type="button" class="tenant-toggle" @click="showTenant = true">
              {{ $t('login.specifyTenant') }}
            </button>
          </div>
          <el-form-item class="login-action">
            <el-button
              type="primary"
              size="large"
              class="login-button"
              :class="{ 'is-success': loginSuccess }"
              :loading="loading"
              @click="handleLogin"
            >
              {{ loginSuccess ? $t('login.entering') : $t('login.button') }}
            </el-button>
          </el-form-item>
          <el-form-item class="sso-action">
            <button type="button" class="sso-button" :disabled="ssoLoading" @click="handleSsoLogin">
              {{ ssoLoading ? $t('sec.sso.redirecting') : $t('sec.sso.loginButton') }}
            </button>
          </el-form-item>
        </el-form>

        <div class="lang-switch">
          <span class="lang-label">{{ $t('login.language') }}</span>
          <el-popover
            v-model:visible="langMenuOpen"
            placement="top"
            trigger="click"
            :width="220"
            popper-class="login-lang-popper"
            :teleported="false"
            :show-arrow="false"
            :popper-style="{
              padding: '0',
              border: 'none',
              background: 'transparent',
              boxShadow: 'none'
            }"
          >
            <template #reference>
              <button type="button" class="lang-trigger">
                <span class="lang-current">{{ currentLangLabel }}</span>
                <el-icon class="lang-arrow" :class="{ 'is-open': langMenuOpen }"><ArrowDown /></el-icon>
              </button>
            </template>

            <div class="lang-menu-panel">
              <div class="lang-menu-title">{{ $t('login.selectLanguage') }}</div>
              <div class="lang-menu">
                <button
                  v-for="item in langOptions"
                  :key="item.value"
                  type="button"
                  class="lang-option"
                  :class="{ 'is-active': item.value === currentLang }"
                  @click="onChangeLang(item.value)"
                >
                  <span>{{ item.label }}</span>
                  <el-icon v-if="item.value === currentLang"><Check /></el-icon>
                </button>
              </div>
            </div>
          </el-popover>
        </div>
      </el-card>
    </section>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import type { FormInstance } from 'element-plus'
import { ArrowDown, Check, Hide, Lock, OfficeBuilding, User, View } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { authApi } from '@/api/sys/auth'
import { ssoApi } from '@/api/sys/sso'
import { langOptions, changeLang } from '@/i18n'
import { addDynamicRoutes } from '@/router'

const { t, locale } = useI18n()
const router = useRouter()
const formRef = ref<FormInstance>()
const loading = ref(false)
const ssoLoading = ref(false)
const currentLang = ref(locale.value)
const pointerX = ref(50)
const pointerY = ref(40)
const rotateX = ref(0)
const rotateY = ref(0)
const loginSuccess = ref(false)
const passwordVisible = ref(false)
const langMenuOpen = ref(false)
// 章10 §7 登录租户选择器：默认折叠，同名多租户时后端回 needTenant → 自动展开
const showTenant = ref(false)

const particles = [
  { id: 1, style: '--size: 10px; --left: 8%; --top: 18%; --duration: 14s; --delay: -2s;' },
  { id: 2, style: '--size: 6px; --left: 22%; --top: 72%; --duration: 17s; --delay: -11s;' },
  { id: 3, style: '--size: 14px; --left: 34%; --top: 32%; --duration: 16s; --delay: -5s;' },
  { id: 4, style: '--size: 8px; --left: 58%; --top: 14%; --duration: 13s; --delay: -7s;' },
  { id: 5, style: '--size: 12px; --left: 76%; --top: 62%; --duration: 19s; --delay: -3s;' },
  { id: 6, style: '--size: 7px; --left: 84%; --top: 26%; --duration: 15s; --delay: -9s;' },
  { id: 7, style: '--size: 9px; --left: 66%; --top: 82%; --duration: 18s; --delay: -6s;' },
  { id: 8, style: '--size: 5px; --left: 44%; --top: 54%; --duration: 12s; --delay: -1s;' }
] as const

const form = ref({
  userName: '',
  password: '',
  tenantCode: ''
})

const rules = computed(() => ({
  userName: [{ required: true, message: t('login.usernameRequired'), trigger: 'blur' }],
  password: [{ required: true, message: t('login.passwordRequired'), trigger: 'blur' }]
}))

const shellStyle = computed(() => ({
  '--pointer-x': `${pointerX.value}%`,
  '--pointer-y': `${pointerY.value}%`
}))

const cardStyle = computed(() => ({
  transform: `perspective(1200px) rotateX(${rotateX.value}deg) rotateY(${rotateY.value}deg)`
}))

const currentLangLabel = computed(
  () => langOptions.find(item => item.value === currentLang.value)?.label ?? currentLang.value
)

async function onChangeLang(lang: string) {
  currentLang.value = lang
  langMenuOpen.value = false
  await changeLang(lang)
}

function handlePointerMove(event: PointerEvent) {
  const currentTarget = event.currentTarget as HTMLElement | null
  if (!currentTarget) return

  const rect = currentTarget.getBoundingClientRect()
  const x = ((event.clientX - rect.left) / rect.width) * 100
  const y = ((event.clientY - rect.top) / rect.height) * 100

  pointerX.value = Math.max(0, Math.min(100, x))
  pointerY.value = Math.max(0, Math.min(100, y))

  rotateY.value = ((pointerX.value - 50) / 50) * 4
  rotateX.value = ((50 - pointerY.value) / 50) * 4
}

function handlePointerLeave() {
  pointerX.value = 50
  pointerY.value = 40
  rotateX.value = 0
  rotateY.value = 0
}

async function handleLogin() {
  if (!formRef.value) return
  await formRef.value.validate()

  loading.value = true
  loginSuccess.value = false
  try {
    const res: any = await authApi.login(form.value)
    // #2 2FA（T9）：租户要求 2FA 时后端不签发 auth cookie，仅写 pending cookie（cp6_2fa/cp6_csrf），
    // 回 { twoFactorRequired:true, mustEnroll }。此时绝不置 cp6_authed，转挑战屏/入会屏完成第二因素。
    if (res?.twoFactorRequired === true) {
      loginSuccess.value = true
      router.push(res.mustEnroll === true ? '/sys/2fa-enroll' : '/sys/2fa-challenge')
      return
    }
    // T9：token 已由后端 Set-Cookie（httpOnly），前端仅存非敏感登录态标志
    localStorage.setItem('cp6_authed', '1')
    localStorage.setItem('cp6_mustChangePwd', res.mustChangePassword ? '1' : '')
    localStorage.setItem('userName', res.userName)
    localStorage.setItem('nickName', res.nickName || res.userName)
    const menus = res.menus || []
    localStorage.setItem('menus', JSON.stringify(menus))
    addDynamicRoutes(menus)
    ElMessage.success(t('login.success'))
    loginSuccess.value = true
    // 强制改密：直接进改密页，不走登录过渡动画
    if (res.mustChangePassword) {
      router.push('/sys/change-password')
      return
    }
    sessionStorage.setItem('cp6-login-transition', 'pending')
    await new Promise(resolve => window.setTimeout(resolve, 700))
    router.push('/')
  } catch (err: any) {
    // 错误由 http.ts 拦截器统一提示文案；此处仅在"同名多租户"时展开租户编码输入
    if (err?.response?.data?.needTenant) showTenant.value = true
    loginSuccess.value = false
  } finally {
    loading.value = false
  }
}

// #3 SSO（T9）：SSO 登录入口。必须先有租户编码（SSO 按租户配 IdP），跳转到 IdP 授权端点。
async function handleSsoLogin() {
  const tenantCode = form.value.tenantCode.trim()
  if (!tenantCode) {
    showTenant.value = true
    ElMessage.warning(t('sec.sso.tenantCodePrompt'))
    return
  }
  ssoLoading.value = true
  try {
    const { authorizeUrl } = await ssoApi.authorize(tenantCode)
    window.location.href = authorizeUrl   // 整页跳转到 IdP（回调后落地屏 /sso/landing）
  } catch {
    // 错误（E-SEC-020/028）由 http.ts 拦截器统一提示
    ssoLoading.value = false
  }
}
</script>

<style scoped>
.login-shell {
  --glass-border: rgba(255, 255, 255, 0.34);
  --glass-surface: rgba(255, 255, 255, 0.14);
  --glass-surface-strong: rgba(255, 255, 255, 0.2);
  --text-primary: #eff7ff;
  --text-secondary: rgba(239, 247, 255, 0.72);
  --accent: #7dd3fc;
  --accent-strong: #0f9cf3;
  position: relative;
  min-height: 100vh;
  min-height: 100dvh;
  overflow: hidden;
  background:
    radial-gradient(circle at top left, rgba(110, 231, 255, 0.24), transparent 32%),
    radial-gradient(circle at 85% 18%, rgba(255, 255, 255, 0.18), transparent 24%),
    linear-gradient(135deg, #07111f 0%, #0b1d34 42%, #12345d 100%);
  transition: background-position 0.35s ease;
}

.login-shell::before {
  content: '';
  position: absolute;
  inset: 0;
  background:
    linear-gradient(125deg, rgba(255, 255, 255, 0.08), transparent 36%),
    linear-gradient(300deg, rgba(16, 185, 255, 0.1), transparent 30%);
  mix-blend-mode: screen;
  pointer-events: none;
}

.login-shell::after {
  content: '';
  position: absolute;
  inset: -12%;
  background:
    radial-gradient(circle at var(--pointer-x, 50%) var(--pointer-y, 40%), rgba(255, 255, 255, 0.2), transparent 18%),
    radial-gradient(circle at calc(var(--pointer-x, 50%) + 12%) calc(var(--pointer-y, 40%) - 10%), rgba(125, 211, 252, 0.18), transparent 22%);
  filter: blur(24px);
  opacity: 0.9;
  pointer-events: none;
  transition: opacity 0.3s ease;
}

.login-ambient {
  position: absolute;
  inset: 0;
  overflow: hidden;
  pointer-events: none;
}

.blob {
  position: absolute;
  border-radius: 50%;
  filter: blur(18px);
  opacity: 0.82;
  animation: drift 18s ease-in-out infinite;
}

.blob-a {
  top: 8%;
  left: 8%;
  width: 22rem;
  height: 22rem;
  background: radial-gradient(circle at 35% 35%, rgba(255, 255, 255, 0.72), rgba(94, 234, 212, 0.2) 55%, transparent 72%);
}

.blob-b {
  right: 10%;
  bottom: 6%;
  width: 26rem;
  height: 26rem;
  background: radial-gradient(circle at 50% 50%, rgba(147, 197, 253, 0.5), rgba(14, 165, 233, 0.18) 58%, transparent 76%);
  animation-duration: 24s;
  animation-delay: -8s;
}

.blob-c {
  top: 46%;
  left: 46%;
  width: 14rem;
  height: 14rem;
  background: radial-gradient(circle at 45% 45%, rgba(255, 255, 255, 0.42), rgba(125, 211, 252, 0.14) 52%, transparent 74%);
  animation-duration: 16s;
  animation-delay: -4s;
}

.wave-ring {
  position: absolute;
  border-radius: 50%;
  border: 1px solid rgba(190, 242, 255, 0.14);
  box-shadow:
    inset 0 0 40px rgba(255, 255, 255, 0.05),
    0 0 50px rgba(56, 189, 248, 0.08);
  animation: ripple 15s linear infinite;
}

.wave-ring-a {
  top: 12%;
  right: 14%;
  width: 28rem;
  height: 28rem;
}

.wave-ring-b {
  left: 12%;
  bottom: 8%;
  width: 20rem;
  height: 20rem;
  animation-duration: 19s;
  animation-delay: -6s;
}

.float-particle {
  position: absolute;
  left: var(--left);
  top: var(--top);
  width: var(--size);
  height: var(--size);
  border-radius: 50%;
  background: radial-gradient(circle at 35% 35%, rgba(255, 255, 255, 0.9), rgba(125, 211, 252, 0.2));
  box-shadow: 0 0 18px rgba(125, 211, 252, 0.25);
  opacity: 0.72;
  animation: float-up var(--duration) ease-in-out infinite;
  animation-delay: var(--delay);
}

.grid-mask {
  position: absolute;
  inset: -10%;
  background-image:
    linear-gradient(rgba(255, 255, 255, 0.05) 1px, transparent 1px),
    linear-gradient(90deg, rgba(255, 255, 255, 0.05) 1px, transparent 1px);
  background-size: 54px 54px;
  mask-image: radial-gradient(circle at center, black 40%, transparent 85%);
  opacity: 0.28;
  transform: perspective(900px) rotateX(66deg) translateY(24%);
}

.login-panel {
  position: relative;
  z-index: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 100vh;
  min-height: 100dvh;
  padding: 3rem clamp(1.25rem, 3vw, 3rem);
  max-width: 960px;
  margin: 0 auto;
  box-sizing: border-box;
  transition: transform 0.5s ease, opacity 0.5s ease, filter 0.5s ease;
}

.login-panel.is-success {
  transform: scale(1.015);
  filter: saturate(1.08);
}

.brand-chip {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 64px;
  padding: 0.45rem 0.9rem;
  border: 1px solid rgba(255, 255, 255, 0.18);
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.08);
  backdrop-filter: blur(16px);
  font-size: 0.85rem;
  font-weight: 700;
  letter-spacing: 0.28em;
  text-transform: uppercase;
  box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.18);
}

.login-card {
  position: relative;
  width: min(100%, 440px);
  border: 1px solid var(--glass-border);
  border-radius: 30px;
  background:
    linear-gradient(180deg, rgba(255, 255, 255, 0.24), rgba(255, 255, 255, 0.1)),
    rgba(255, 255, 255, 0.08);
  box-shadow:
    inset 0 1px 0 rgba(255, 255, 255, 0.34),
    0 28px 80px rgba(3, 10, 24, 0.45);
  backdrop-filter: blur(24px) saturate(150%);
  overflow: hidden;
  animation: rise-in 1s ease-out 0.15s both;
  transform-style: preserve-3d;
  transition:
    transform 0.28s ease,
    box-shadow 0.28s ease,
    border-color 0.28s ease,
    opacity 0.45s ease,
    filter 0.45s ease;
}

.card-glow {
  position: absolute;
  inset: auto auto 75% 62%;
  width: 9rem;
  height: 9rem;
  border-radius: 50%;
  background: radial-gradient(circle, rgba(255, 255, 255, 0.5), transparent 68%);
  filter: blur(10px);
  opacity: 0.9;
  pointer-events: none;
}

.login-card::before {
  content: '';
  position: absolute;
  inset: 1px;
  border-radius: inherit;
  background:
    radial-gradient(circle at var(--pointer-x, 50%) var(--pointer-y, 40%), rgba(255, 255, 255, 0.26), transparent 22%),
    linear-gradient(140deg, rgba(255, 255, 255, 0.18), transparent 34%);
  opacity: 0.95;
  pointer-events: none;
}

.login-card::after {
  content: '';
  position: absolute;
  inset: 0;
  border-radius: inherit;
  background: linear-gradient(115deg, transparent 20%, rgba(255, 255, 255, 0.24) 40%, transparent 58%);
  transform: translateX(-120%);
  animation: sheen 8s ease-in-out infinite;
  pointer-events: none;
}

.login-card.is-success {
  box-shadow:
    inset 0 1px 0 rgba(255, 255, 255, 0.42),
    0 36px 96px rgba(3, 10, 24, 0.52);
  filter: brightness(1.05);
}

.login-card :deep(.el-card__body) {
  position: relative;
  padding: 2rem;
}

.card-header {
  margin-bottom: 1.6rem;
  color: var(--text-primary);
  transition: transform 0.4s ease, opacity 0.4s ease;
}

.card-eyebrow {
  margin: 1rem 0 0.55rem;
  color: rgba(224, 231, 255, 0.7);
  font-size: 0.76rem;
  letter-spacing: 0.2em;
  text-transform: uppercase;
}

.login-title {
  margin: 0;
  font-size: clamp(1.8rem, 4vw, 2.4rem);
  line-height: 1.08;
  letter-spacing: -0.04em;
}

.login-subtitle {
  margin: 0.65rem 0 0;
  color: var(--text-secondary);
  line-height: 1.7;
}

.login-form {
  position: relative;
  z-index: 1;
  transition: transform 0.4s ease, opacity 0.4s ease;
}

.login-form :deep(.el-form-item) {
  margin-bottom: 1rem;
}

.login-form :deep(.el-input__wrapper),
.lang-switch :deep(.el-select__wrapper) {
  background: var(--glass-surface) !important;
  border: 1px solid rgba(255, 255, 255, 0.15);
  box-shadow:
    inset 0 1px 0 rgba(255, 255, 255, 0.22),
    0 10px 26px rgba(15, 23, 42, 0.18) !important;
  backdrop-filter: blur(16px);
  transition:
    transform 0.24s ease,
    box-shadow 0.24s ease,
    border-color 0.24s ease;
}

.login-form :deep(.el-input__wrapper:hover),
.lang-switch :deep(.el-select__wrapper:hover) {
  transform: translateY(-1px);
}

.login-form :deep(.el-input__wrapper.is-focus),
.lang-switch :deep(.is-focused .el-select__wrapper) {
  box-shadow:
    0 0 0 1px rgba(125, 211, 252, 0.65),
    0 16px 32px rgba(14, 165, 233, 0.2) !important;
}

.login-form :deep(.el-input__inner),
.login-form :deep(.el-input__prefix-inner),
.login-form :deep(.el-input__suffix-inner),
.lang-switch :deep(.el-select__selected-item),
.lang-switch :deep(.el-select__placeholder) {
  color: var(--text-primary) !important;
}

.login-form :deep(.el-input__inner::placeholder) {
  color: rgba(226, 232, 240, 0.52);
}

.password-toggle {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 2rem;
  height: 2rem;
  margin-right: -0.2rem;
  border: 0;
  border-radius: 999px;
  background: transparent;
  color: rgba(239, 247, 255, 0.7);
  cursor: pointer;
  transition: background-color 0.2s ease, color 0.2s ease, transform 0.2s ease;
}

.password-toggle:hover {
  background: rgba(255, 255, 255, 0.08);
  color: #eff7ff;
  transform: scale(1.05);
}

.tenant-toggle-row {
  display: flex;
  justify-content: flex-end;
  margin: -0.4rem 0 0.6rem;
}

.tenant-toggle {
  border: 0;
  background: transparent;
  color: rgba(239, 247, 255, 0.66);
  font-size: 0.8rem;
  cursor: pointer;
  padding: 0.1rem 0.2rem;
  transition: color 0.2s ease;
}

.tenant-toggle:hover {
  color: var(--accent, #7dd3fc);
  text-decoration: underline;
}

.login-button {
  width: 100%;
  height: 3.2rem;
  border: none;
  border-radius: 18px;
  background: linear-gradient(135deg, rgba(125, 211, 252, 0.95), rgba(14, 165, 233, 0.9)) !important;
  box-shadow:
    inset 0 1px 0 rgba(255, 255, 255, 0.45),
    0 18px 34px rgba(14, 165, 233, 0.34);
  transition:
    transform 0.25s ease,
    box-shadow 0.25s ease,
    filter 0.25s ease,
    letter-spacing 0.25s ease;
  animation: pulse 3.6s ease-in-out infinite;
}

.login-button.is-success {
  background: linear-gradient(135deg, rgba(52, 211, 153, 0.96), rgba(6, 182, 212, 0.92)) !important;
  box-shadow:
    inset 0 1px 0 rgba(255, 255, 255, 0.45),
    0 24px 42px rgba(16, 185, 129, 0.34);
}

.login-button:hover {
  transform: translateY(-2px);
  box-shadow:
    inset 0 1px 0 rgba(255, 255, 255, 0.45),
    0 22px 42px rgba(14, 165, 233, 0.42);
  filter: saturate(112%);
  letter-spacing: 0.04em;
}

.login-button:active {
  transform: translateY(0);
}

.login-action {
  margin-top: 1.35rem;
  margin-bottom: 0.4rem !important;
}

.sso-action {
  margin-bottom: 0.2rem !important;
}

.sso-button {
  width: 100%;
  height: 2.9rem;
  border: 1px solid rgba(255, 255, 255, 0.22);
  border-radius: 16px;
  background: rgba(255, 255, 255, 0.08);
  color: var(--text-primary);
  font-size: 0.95rem;
  letter-spacing: 0.02em;
  cursor: pointer;
  backdrop-filter: blur(16px);
  box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.18);
  transition: transform 0.22s ease, border-color 0.22s ease, background-color 0.22s ease;
}

.sso-button:hover:not(:disabled) {
  transform: translateY(-1px);
  border-color: rgba(125, 211, 252, 0.5);
  background: rgba(125, 211, 252, 0.14);
}

.sso-button:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.lang-switch {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.8rem;
  margin-top: 1rem;
  color: var(--text-secondary);
  transition: transform 0.4s ease, opacity 0.4s ease;
}

.lang-label {
  font-size: 0.82rem;
  letter-spacing: 0.12em;
  text-transform: uppercase;
}

.lang-trigger {
  display: inline-flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
  min-width: 166px;
  padding: 0.78rem 0.95rem;
  border: 1px solid rgba(255, 255, 255, 0.16);
  border-radius: 16px;
  background: rgba(255, 255, 255, 0.09);
  box-shadow:
    inset 0 1px 0 rgba(255, 255, 255, 0.18),
    0 10px 26px rgba(15, 23, 42, 0.16);
  color: var(--text-primary);
  backdrop-filter: blur(16px);
  cursor: pointer;
  transition: transform 0.24s ease, border-color 0.24s ease, box-shadow 0.24s ease;
}

.lang-trigger:hover {
  transform: translateY(-1px);
  border-color: rgba(125, 211, 252, 0.36);
  box-shadow:
    inset 0 1px 0 rgba(255, 255, 255, 0.18),
    0 16px 30px rgba(14, 165, 233, 0.14);
}

.lang-current {
  font-size: 0.95rem;
}

.lang-arrow {
  transition: transform 0.24s ease, opacity 0.24s ease;
  opacity: 0.72;
}

.lang-arrow.is-open {
  transform: rotate(180deg);
}

.lang-menu {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
}

.lang-menu-panel {
  padding: 0.7rem;
  border: 1px solid rgba(255, 255, 255, 0.16);
  border-radius: 22px;
  background:
    linear-gradient(180deg, rgba(255, 255, 255, 0.18), rgba(255, 255, 255, 0.08)),
    rgba(7, 18, 33, 0.76);
  box-shadow:
    inset 0 1px 0 rgba(255, 255, 255, 0.16),
    0 24px 56px rgba(3, 10, 24, 0.42);
  backdrop-filter: blur(24px) saturate(150%);
}

.lang-menu-title {
  margin-bottom: 0.55rem;
  padding: 0.2rem 0.45rem 0.45rem;
  color: rgba(226, 232, 240, 0.72);
  font-size: 0.72rem;
  letter-spacing: 0.18em;
  text-transform: uppercase;
}

:deep(.login-lang-popper.el-popover) {
  padding: 0 !important;
  border: none !important;
  background: transparent !important;
  box-shadow: none !important;
}

:deep(.login-lang-popper.el-popper .el-popper__arrow::before) {
  background: rgba(10, 24, 43, 0.9);
  border-color: rgba(255, 255, 255, 0.14);
}

.lang-option {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  padding: 0.78rem 0.9rem;
  border: 0;
  border-radius: 14px;
  background: rgba(255, 255, 255, 0.04);
  color: rgba(248, 250, 252, 0.92);
  text-align: left;
  cursor: pointer;
  transition:
    background-color 0.22s ease,
    color 0.22s ease,
    transform 0.22s ease,
    box-shadow 0.22s ease;
}

.lang-option:hover {
  background: rgba(125, 211, 252, 0.16);
  color: #f8fbff;
  transform: translateX(2px);
  box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.12);
}

.lang-option.is-active {
  background: linear-gradient(135deg, rgba(125, 211, 252, 0.3), rgba(59, 130, 246, 0.22));
  color: #ffffff;
  box-shadow:
    inset 0 1px 0 rgba(255, 255, 255, 0.24),
    0 8px 24px rgba(59, 130, 246, 0.18);
}

.success-burst {
  position: absolute;
  inset: 50% auto auto 50%;
  width: 16rem;
  height: 16rem;
  border-radius: 50%;
  background: radial-gradient(circle, rgba(52, 211, 153, 0.32), rgba(125, 211, 252, 0.18) 35%, transparent 68%);
  transform: translate(-50%, -50%) scale(0.35);
  opacity: 0;
  filter: blur(10px);
  pointer-events: none;
}

.login-card.is-success .success-burst {
  animation: success-burst 0.7s ease-out forwards;
}

.login-card.is-success .card-header,
.login-card.is-success .login-form,
.login-card.is-success .lang-switch {
  transform: translateY(-2px);
}

@keyframes drift {
  0%,
  100% {
    transform: translate3d(0, 0, 0) scale(1);
  }
  33% {
    transform: translate3d(18px, -24px, 0) scale(1.04);
  }
  66% {
    transform: translate3d(-22px, 20px, 0) scale(0.96);
  }
}

@keyframes rise-in {
  from {
    opacity: 0;
    transform: translateY(26px) scale(0.98);
  }
  to {
    opacity: 1;
    transform: translateY(0) scale(1);
  }
}

@keyframes ripple {
  0%,
  100% {
    transform: scale(0.94);
    opacity: 0.22;
  }
  50% {
    transform: scale(1.04);
    opacity: 0.42;
  }
}

@keyframes float-up {
  0%,
  100% {
    transform: translate3d(0, 0, 0) scale(1);
    opacity: 0.45;
  }
  40% {
    transform: translate3d(12px, -24px, 0) scale(1.12);
    opacity: 0.9;
  }
  70% {
    transform: translate3d(-10px, -54px, 0) scale(0.9);
    opacity: 0.55;
  }
}

@keyframes success-burst {
  0% {
    transform: translate(-50%, -50%) scale(0.35);
    opacity: 0;
  }
  35% {
    opacity: 1;
  }
  100% {
    transform: translate(-50%, -50%) scale(1.45);
    opacity: 0;
  }
}

@keyframes sheen {
  0%,
  70%,
  100% {
    transform: translateX(-120%);
  }
  82% {
    transform: translateX(140%);
  }
}

@keyframes pulse {
  0%,
  100% {
    box-shadow:
      inset 0 1px 0 rgba(255, 255, 255, 0.45),
      0 18px 34px rgba(14, 165, 233, 0.34);
  }
  50% {
    box-shadow:
      inset 0 1px 0 rgba(255, 255, 255, 0.52),
      0 24px 40px rgba(14, 165, 233, 0.4);
  }
}

@media (prefers-reduced-motion: reduce) {
  .blob,
  .wave-ring,
  .float-particle,
  .login-card,
  .login-card::after,
  .login-card .success-burst,
  .login-button {
    animation: none !important;
  }

  .login-card,
  .login-button,
  .login-form :deep(.el-input__wrapper),
  .lang-switch :deep(.el-select__wrapper) {
    transition: none !important;
  }
}

@media (max-width: 960px) {
  .login-panel {
    padding: 1.25rem;
  }
}

@media (max-width: 767px) {
  .login-shell {
    background:
      radial-gradient(circle at top, rgba(110, 231, 255, 0.2), transparent 32%),
      linear-gradient(180deg, #09111f 0%, #102746 100%);
  }

  .login-panel {
    min-height: 100vh;
    min-height: 100dvh;
    padding: max(1rem, var(--safe-top)) 1rem max(1rem, var(--safe-bottom));
  }

  .login-card {
    border-radius: 26px;
    transform: none !important;
  }

  .login-card :deep(.el-card__body) {
    padding: 1.35rem;
  }

  .lang-switch {
    flex-direction: column;
    align-items: stretch;
  }

  .lang-trigger {
    width: 100%;
  }
}
</style>
