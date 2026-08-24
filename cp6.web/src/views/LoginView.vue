<template>
  <div
    class="login-page"
    :class="{ 'is-ready': pageReady }"
    :style="shellStyle"
    @pointermove="handlePointerMove"
    @pointerleave="handlePointerLeave"
  >
    <div class="ambient-orb ambient-orb-one" aria-hidden="true"></div>
    <div class="ambient-orb ambient-orb-two" aria-hidden="true"></div>

    <header class="topbar">
      <div class="brand" aria-label="CP6 Packaging Operations Platform">
        <svg class="brand-mark" viewBox="0 0 64 64" aria-hidden="true">
          <defs>
            <linearGradient id="login-logo-bg" x1="0" y1="0" x2="1" y2="1">
              <stop stop-color="#2bd4cd" />
              <stop offset="1" stop-color="#0e93a0" />
            </linearGradient>
          </defs>
          <rect x="3" y="3" width="58" height="58" rx="16" fill="url(#login-logo-bg)" />
          <g stroke="#0e93a0" stroke-width="1.2" stroke-linejoin="round">
            <polygon points="32,13 50,23 32,33 14,23" fill="#fff" />
            <polygon points="14,23 32,33 32,53 14,43" fill="#d2f6f3" />
            <polygon points="50,23 32,33 32,53 50,43" fill="#9de3de" />
          </g>
        </svg>
        <div class="brand-copy">
          <strong>CP6</strong>
          <span>PACKAGING OPERATIONS PLATFORM</span>
        </div>
      </div>

      <div class="top-actions">
        <div class="status-pill" role="status">
          <i class="status-dot" aria-hidden="true"></i>
          <span>{{ copy.serviceStatus }}</span>
        </div>

        <el-popover
          v-model:visible="langMenuOpen"
          placement="bottom-end"
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
            <button
              type="button"
              class="lang-button"
              :class="{ 'is-open': langMenuOpen }"
              :aria-expanded="langMenuOpen"
              aria-haspopup="listbox"
            >
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true">
                <circle cx="12" cy="12" r="9" />
                <path d="M3 12h18M12 3a15 15 0 0 1 0 18M12 3a15 15 0 0 0 0 18" />
              </svg>
              <span>{{ currentLangLabel }}</span>
              <el-icon class="lang-chevron"><ArrowDown /></el-icon>
            </button>
          </template>

          <div class="lang-menu-panel" role="listbox" :aria-label="$t('login.selectLanguage')">
            <div class="lang-menu-title">{{ $t('login.selectLanguage') }}</div>
            <button
              v-for="item in langOptions"
              :key="item.value"
              type="button"
              class="lang-option"
              :class="{ 'is-active': item.value === currentLang }"
              role="option"
              :aria-selected="item.value === currentLang"
              @click="onChangeLang(item.value)"
            >
              <span>{{ item.label }}</span>
              <el-icon v-if="item.value === currentLang"><Check /></el-icon>
            </button>
          </div>
        </el-popover>
      </div>
    </header>

    <main class="login-stage">
      <section class="story" aria-labelledby="login-story-title">
        <div class="story-content">
          <div class="story-eyebrow">PACKAGING MANUFACTURING CORE PLATFORM</div>
          <h1 id="login-story-title">
            <span>{{ copy.heroLine }}</span>
            <em>{{ copy.heroAccent }}</em>
          </h1>
          <p class="story-lead">
            {{ copy.leadPrefix }} <strong>{{ copy.leadStrong }}</strong>{{ copy.leadSuffix }}
          </p>

          <div class="flow-card">
            <div class="flow-head">
              <span>{{ copy.flowTitle }}</span>
              <span class="connected"><i aria-hidden="true"></i>{{ copy.connected }}</span>
            </div>
            <div class="flow">
              <span class="flow-signal" aria-hidden="true"></span>
              <div
                v-for="(node, index) in flowNodes"
                :key="node.title"
                class="flow-node"
                :class="`node-tone-${index + 1}`"
              >
                <div class="node-icon">
                  <el-icon><component :is="node.icon" /></el-icon>
                </div>
                <strong>{{ node.title }}</strong>
                <span>{{ node.detail }}</span>
              </div>
            </div>
            <div class="foundation">
              <div v-for="item in foundations" :key="item.text" class="foundation-item">
                <el-icon><component :is="item.icon" /></el-icon>
                <span>{{ item.text }}</span>
              </div>
            </div>
          </div>

          <div class="capabilities" aria-label="CP6 clients and security">
            <div v-for="item in capabilities" :key="item.text" class="capability">
              <el-icon><component :is="item.icon" /></el-icon>
              <span>{{ item.text }}</span>
            </div>
          </div>
        </div>

        <svg class="dieline" viewBox="0 0 520 290" fill="none" aria-hidden="true">
          <g stroke="#0e93a0" stroke-width="1.2">
            <path d="M62 76h96v70H62zM158 76h96v70h-96zM254 76h96v70h-96zM350 76h96v70h-96z" />
            <path d="M158 22h96v54h-96zM158 146h96v56h-96zM254 146h96v56h-96z" />
            <path d="M62 76 28 97v28l34 21M446 76l37 20v30l-37 20" stroke-dasharray="5 5" />
            <path d="M158 76l16-54M254 76l-18-54M158 146l18 56M254 146l-18 56" stroke-dasharray="5 5" />
            <circle cx="206" cy="111" r="18" />
            <path d="M198 111h16M206 103v16" />
            <path d="M95 239h330M116 221v36M175 221v36M234 221v36M293 221v36M352 221v36M411 221v36" opacity=".65" />
          </g>
        </svg>
      </section>

      <section class="auth-zone" aria-labelledby="login-form-title">
        <div class="auth-card" :class="{ 'is-success': loginSuccess }">
          <div class="auth-kicker">SECURE WORKSPACE ACCESS</div>
          <h2 id="login-form-title">{{ $t('login.welcomeBack') }}</h2>
          <p class="auth-subtitle">{{ $t('login.subtitle') }}</p>

          <el-form
            ref="formRef"
            :model="form"
            :rules="rules"
            label-position="top"
            class="auth-form"
            @submit.prevent="handleLogin"
          >
            <div class="tenant-strip">
              <div class="tenant-icon">
                <el-icon><OfficeBuilding /></el-icon>
              </div>
              <div class="tenant-copy">
                <span>{{ copy.tenantLabel }}</span>
                <strong>{{ form.tenantCode.trim() || copy.tenantAutomatic }}</strong>
              </div>
              <button
                type="button"
                class="tenant-change"
                :aria-expanded="showTenant"
                aria-controls="tenant-code-field"
                @click="toggleTenant"
              >
                {{ showTenant ? copy.collapseTenant : $t('login.specifyTenant') }}
              </button>
            </div>

            <div id="tenant-code-field" class="tenant-field" :class="{ 'is-open': showTenant }">
              <div class="tenant-field-inner">
                <el-form-item prop="tenantCode" :label="$t('login.tenantCode')">
                  <el-input
                    ref="tenantInputRef"
                    v-model="form.tenantCode"
                    :placeholder="$t('login.tenantCode')"
                    :prefix-icon="OfficeBuilding"
                    autocomplete="organization"
                  />
                </el-form-item>
              </div>
            </div>

            <el-form-item prop="userName" :label="$t('login.username')">
              <el-input
                v-model="form.userName"
                :placeholder="$t('login.username')"
                :prefix-icon="User"
                autocomplete="username"
              />
            </el-form-item>

            <el-form-item prop="password" :label="$t('login.password')">
              <el-input
                v-model="form.password"
                :type="passwordVisible ? 'text' : 'password'"
                :placeholder="$t('login.password')"
                :prefix-icon="Lock"
                autocomplete="current-password"
              >
                <template #suffix>
                  <button
                    type="button"
                    class="password-toggle"
                    :aria-label="passwordVisible ? copy.hidePassword : copy.showPassword"
                    @click="passwordVisible = !passwordVisible"
                  >
                    <el-icon><component :is="passwordVisible ? View : Hide" /></el-icon>
                  </button>
                </template>
              </el-input>
            </el-form-item>

            <div class="form-meta">
              <span class="secure-note"><el-icon><CircleCheck /></el-icon>{{ copy.secureTransport }}</span>
              <span>{{ copy.twoFactorPolicy }}</span>
            </div>

            <el-button
              type="primary"
              native-type="submit"
              class="login-button"
              :class="{ 'is-success': loginSuccess }"
              :loading="loading"
            >
              <span v-if="loginSuccess" class="success-label">
                <el-icon><Check /></el-icon>{{ $t('login.entering') }}
              </span>
              <span v-else>{{ $t('login.button') }}</span>
            </el-button>

            <div class="divider">{{ copy.divider }}</div>

            <button type="button" class="sso-button" :class="{ 'is-loading': ssoLoading }" :disabled="ssoLoading" @click="handleSsoLogin">
              <el-icon><Key /></el-icon>
              <span>{{ ssoLoading ? $t('sec.sso.redirecting') : $t('sec.sso.loginButton') }}</span>
            </button>
          </el-form>

          <div class="security-row">
            <div v-for="item in securityItems" :key="item.text" class="security-item">
              <el-icon><component :is="item.icon" /></el-icon>
              <span>{{ item.text }}</span>
            </div>
          </div>
          <p class="auth-foot">{{ copy.accessNotice }}</p>
        </div>
      </section>
    </main>

    <footer class="page-footer">
      <div class="platform-name"><strong>CP6</strong><span>{{ copy.platformTagline }}</span></div>
      <div>© 2026 CP6 · Web / Windows / Android</div>
    </footer>
  </div>
</template>

<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import type { FormInstance, InputInstance } from 'element-plus'
import {
  ArrowDown,
  Box,
  Calendar,
  Cellphone,
  Check,
  CircleCheck,
  Collection,
  Connection,
  CreditCard,
  Document,
  Hide,
  House,
  Key,
  Lock,
  Monitor,
  OfficeBuilding,
  Operation,
  Platform,
  Tickets,
  User,
  View,
} from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { authApi } from '@/api/sys/auth'
import { ssoApi } from '@/api/sys/sso'
import { langOptions, changeLang } from '@/i18n'
import { addDynamicRoutes } from '@/router'
import { usePlatformStore } from '@/stores/platform'
import { getLoginExperienceCopy } from './loginExperience'

const { t, locale } = useI18n()
const router = useRouter()
const formRef = ref<FormInstance>()
const tenantInputRef = ref<InputInstance>()
const loading = ref(false)
const ssoLoading = ref(false)
const currentLang = ref(String(locale.value))
const pointerX = ref(72)
const pointerY = ref(18)
const loginSuccess = ref(false)
const passwordVisible = ref(false)
const langMenuOpen = ref(false)
const showTenant = ref(false)
const pageReady = ref(false)
let pointerFrame: number | null = null
let readyFrame: number | null = null

const form = ref({
  userName: '',
  password: '',
  tenantCode: '',
})

const rules = computed(() => ({
  userName: [{ required: true, message: t('login.usernameRequired'), trigger: 'blur' }],
  password: [{ required: true, message: t('login.passwordRequired'), trigger: 'blur' }],
}))

const copy = computed(() => getLoginExperienceCopy(currentLang.value))
const shellStyle = computed(() => ({
  '--pointer-x': `${pointerX.value}%`,
  '--pointer-y': `${pointerY.value}%`,
}))
const currentLangLabel = computed(
  () => langOptions.find(item => item.value === currentLang.value)?.label ?? currentLang.value,
)

const flowIcons = [Tickets, Calendar, Operation, House, CreditCard] as const
const foundationIcons = [Document, Connection, Box] as const
const capabilityIcons = [Monitor, Platform, Cellphone, Lock] as const
const securityIcons = [OfficeBuilding, Key, Collection] as const

const flowNodes = computed(() => copy.value.flowNodes.map((node, index) => ({
  ...node,
  icon: flowIcons[index]!,
})))
const foundations = computed(() => copy.value.foundations.map((text, index) => ({
  text,
  icon: foundationIcons[index]!,
})))
const capabilities = computed(() => copy.value.capabilities.map((text, index) => ({
  text,
  icon: capabilityIcons[index]!,
})))
const securityItems = computed(() => copy.value.securityItems.map((text, index) => ({
  text,
  icon: securityIcons[index]!,
})))

onMounted(() => {
  readyFrame = window.requestAnimationFrame(() => {
    pageReady.value = true
  })
})

onBeforeUnmount(() => {
  if (pointerFrame !== null) window.cancelAnimationFrame(pointerFrame)
  if (readyFrame !== null) window.cancelAnimationFrame(readyFrame)
})

async function onChangeLang(lang: string) {
  langMenuOpen.value = false
  await changeLang(lang)
  currentLang.value = lang
}

function handlePointerMove(event: PointerEvent) {
  const currentTarget = event.currentTarget as HTMLElement | null
  if (!currentTarget || pointerFrame !== null) return

  const clientX = event.clientX
  const clientY = event.clientY
  pointerFrame = window.requestAnimationFrame(() => {
    const rect = currentTarget.getBoundingClientRect()
    pointerX.value = Math.max(0, Math.min(100, ((clientX - rect.left) / rect.width) * 100))
    pointerY.value = Math.max(0, Math.min(100, ((clientY - rect.top) / rect.height) * 100))
    pointerFrame = null
  })
}

function handlePointerLeave() {
  if (pointerFrame !== null) {
    window.cancelAnimationFrame(pointerFrame)
    pointerFrame = null
  }
  pointerX.value = 72
  pointerY.value = 18
}

async function toggleTenant() {
  showTenant.value = !showTenant.value
  if (showTenant.value) {
    await nextTick()
    tenantInputRef.value?.focus()
  }
}

async function handleLogin() {
  if (!formRef.value || loading.value) return
  const valid = await formRef.value.validate().catch(() => false)
  if (!valid) return

  loading.value = true
  loginSuccess.value = false
  try {
    const res: any = await authApi.login(form.value)
    // 租户要求 2FA 时后端只写 pending cookie，不在前端设置已登录标志。
    if (res?.twoFactorRequired === true) {
      loginSuccess.value = true
      router.push(res.mustEnroll === true ? '/sys/2fa-enroll' : '/sys/2fa-challenge')
      return
    }

    // token 由后端写入 httpOnly cookie；前端只保存非敏感的界面状态。
    localStorage.setItem('cp6_authed', '1')
    localStorage.setItem('cp6_mustChangePwd', res.mustChangePassword ? '1' : '')
    localStorage.setItem('cp6_isPlatformAdmin', res.isPlatformAdmin ? '1' : '')
    usePlatformStore().refreshFlag()
    localStorage.setItem('userName', res.userName)
    localStorage.setItem('nickName', res.nickName || res.userName)
    const menus = res.menus || []
    localStorage.setItem('menus', JSON.stringify(menus))
    addDynamicRoutes(menus)
    ElMessage.success(t('login.success'))
    loginSuccess.value = true

    if (res.mustChangePassword) {
      router.push('/sys/change-password')
      return
    }

    sessionStorage.setItem('cp6-login-transition', 'pending')
    router.push('/')
  } catch (err: any) {
    // 同名用户存在于多个租户时，后端要求用户补充租户编码。
    if (err?.response?.data?.needTenant) showTenant.value = true
    loginSuccess.value = false
  } finally {
    loading.value = false
  }
}

async function handleSsoLogin() {
  const tenantCode = form.value.tenantCode.trim()
  if (!tenantCode) {
    showTenant.value = true
    await nextTick()
    tenantInputRef.value?.focus()
    ElMessage.warning(t('sec.sso.tenantCodePrompt'))
    return
  }

  ssoLoading.value = true
  try {
    const { authorizeUrl } = await ssoApi.authorize(tenantCode)
    window.location.href = authorizeUrl
  } catch {
    // API 错误由全局 HTTP 拦截器统一提示。
    ssoLoading.value = false
  }
}
</script>

<style scoped>
.login-page {
  --brand: #14b8c4;
  --brand-2: #2bd4cd;
  --brand-deep: #0e93a0;
  --brand-pale: #e8f9f8;
  --ink: #10343c;
  --text: #47616b;
  --muted: #8ca3ab;
  --faint: #c2d2d7;
  --line: #e6eff1;
  --line-soft: #eff6f7;
  --surface: #ffffff;
  --ok: #22b573;
  --warn: #f0940a;
  --info: #4e80ee;
  --violet: #8b7cf0;
  --motion-ease: cubic-bezier(.22, 1, .36, 1);
  position: relative;
  isolation: isolate;
  display: flex;
  flex-direction: column;
  width: 100%;
  min-width: 0;
  height: 100vh;
  height: 100dvh;
  padding: 34px 42px 30px;
  overflow: hidden;
  box-sizing: border-box;
  color: var(--text);
  font-family: Nunito, "Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC", system-ui, sans-serif;
  background:
    radial-gradient(900px 520px at 92% -8%, rgba(43, 212, 205, .16), transparent 58%),
    radial-gradient(760px 560px at -8% 80%, rgba(78, 128, 238, .08), transparent 60%),
    #f2fafb;
}

.login-page::before {
  content: '';
  position: absolute;
  inset: 0;
  z-index: -2;
  opacity: .52;
  pointer-events: none;
  background-image:
    linear-gradient(rgba(16, 52, 60, .025) 1px, transparent 1px),
    linear-gradient(90deg, rgba(16, 52, 60, .025) 1px, transparent 1px);
  background-size: 42px 42px;
  mask-image: linear-gradient(to right, #000, transparent 78%);
  animation: grid-drift 28s linear infinite;
}

.login-page::after {
  content: '';
  position: absolute;
  inset: -12%;
  z-index: -1;
  opacity: .82;
  pointer-events: none;
  filter: blur(12px);
  background:
    radial-gradient(420px 320px at var(--pointer-x) var(--pointer-y), rgba(43, 212, 205, .13), transparent 66%),
    radial-gradient(320px 260px at calc(var(--pointer-x) - 24%) calc(var(--pointer-y) + 26%), rgba(78, 128, 238, .055), transparent 70%);
}

.ambient-orb {
  position: absolute;
  z-index: -1;
  border: 1px solid rgba(20, 184, 196, .12);
  border-radius: 50%;
  pointer-events: none;
}

.ambient-orb-one {
  left: -210px;
  bottom: -190px;
  width: 460px;
  height: 460px;
  animation: orb-float-a 15s ease-in-out infinite;
}

.ambient-orb-two {
  right: 105px;
  top: -165px;
  width: 280px;
  height: 280px;
  animation: orb-float-b 18s ease-in-out -5s infinite;
}

.topbar {
  z-index: 5;
  display: flex;
  flex: 0 0 auto;
  align-items: center;
  justify-content: space-between;
  height: 60px;
  padding: 0 8px 0 10px;
  opacity: 0;
  transform: translateY(-14px);
}

.is-ready .topbar {
  animation: topbar-enter .72s var(--motion-ease) .05s both;
}

.brand,
.top-actions,
.status-pill,
.lang-button {
  display: flex;
  align-items: center;
}

.brand {
  gap: 14px;
}

.brand-mark {
  width: 43px;
  height: 43px;
  filter: drop-shadow(0 8px 14px rgba(20, 184, 196, .22));
  animation: logo-hover 5.2s ease-in-out 1.2s infinite;
}

.brand-copy strong {
  display: block;
  color: var(--ink);
  font-size: 21px;
  font-weight: 800;
  line-height: 1.05;
}

.brand-copy span {
  display: block;
  margin-top: 5px;
  color: var(--muted);
  font-size: 10px;
  font-weight: 800;
  letter-spacing: 2.2px;
}

.top-actions {
  gap: 11px;
}

.status-pill,
.lang-button {
  min-height: 39px;
  padding: 0 14px;
  border: 1px solid var(--line);
  border-radius: 12px;
  box-sizing: border-box;
  color: var(--text);
  font-size: 12px;
  font-weight: 700;
  background: rgba(255, 255, 255, .84);
  box-shadow: 0 4px 16px rgba(16, 52, 60, .035);
}

.status-pill {
  position: relative;
  gap: 9px;
  overflow: hidden;
}

.status-pill::after {
  content: '';
  position: absolute;
  inset: 0 auto 0 -42%;
  width: 34%;
  background: linear-gradient(90deg, transparent, rgba(255, 255, 255, .78), transparent);
  transform: skewX(-18deg);
  animation: status-sheen 7s ease-in-out 2.5s infinite;
}

.status-dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: var(--ok);
  box-shadow: 0 0 0 4px rgba(34, 181, 115, .1);
  animation: status-pulse 2.4s ease-out infinite;
}

.lang-button {
  gap: 9px;
  cursor: pointer;
  transition: transform .18s var(--motion-ease), border-color .18s ease, box-shadow .18s ease;
}

.lang-button:hover,
.lang-button:focus-visible {
  border-color: rgba(20, 184, 196, .48);
  outline: none;
  box-shadow: 0 0 0 4px rgba(20, 184, 196, .1), 0 8px 22px rgba(16, 52, 60, .07);
  transform: translateY(-1px);
}

.lang-button > svg {
  width: 16px;
  height: 16px;
  color: var(--muted);
}

.lang-chevron {
  color: var(--muted);
  transition: transform .22s var(--motion-ease);
}

.lang-button.is-open .lang-chevron {
  transform: rotate(180deg);
}

.lang-menu-panel {
  padding: 8px;
  border: 1px solid var(--line);
  border-radius: 14px;
  background: #fff;
  box-shadow: 0 24px 80px rgba(16, 52, 60, .14);
}

.lang-menu-title {
  padding: 7px 10px 8px;
  color: var(--muted);
  font-size: 10px;
  font-weight: 800;
  letter-spacing: .08em;
  text-transform: uppercase;
}

.lang-option {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  min-height: 38px;
  padding: 0 10px;
  border: 0;
  border-radius: 9px;
  color: var(--text);
  font: inherit;
  font-size: 12px;
  font-weight: 700;
  text-align: left;
  cursor: pointer;
  background: transparent;
  transition: color .16s ease, background .16s ease, transform .16s var(--motion-ease);
}

.lang-option:hover,
.lang-option:focus-visible,
.lang-option.is-active {
  color: var(--brand-deep);
  outline: none;
  background: var(--brand-pale);
}

.lang-option:hover {
  transform: translateX(2px);
}

.login-stage {
  position: relative;
  display: grid;
  grid-template-columns: minmax(0, 1.42fr) minmax(470px, .78fr);
  flex: 1;
  min-height: 0;
  margin-top: 18px;
  overflow: hidden;
  border: 1px solid rgba(230, 239, 241, .95);
  border-radius: 30px;
  background: rgba(255, 255, 255, .72);
  box-shadow: 0 24px 80px rgba(16, 52, 60, .11);
  opacity: 0;
  transform: translateY(18px) scale(.992);
  transform-origin: 50% 45%;
}

.login-stage::after {
  content: '';
  position: absolute;
  right: 0;
  bottom: 0;
  left: 0;
  height: 1px;
  background: linear-gradient(90deg, transparent, rgba(20, 184, 196, .35), transparent);
}

.is-ready .login-stage {
  animation: stage-enter .88s var(--motion-ease) .14s both;
}

.story {
  position: relative;
  isolation: isolate;
  padding: clamp(48px, 5.2vh, 78px) clamp(46px, 4.5vw, 74px) clamp(36px, 4vh, 56px);
  overflow: hidden;
  background:
    radial-gradient(680px 460px at 15% 8%, rgba(43, 212, 205, .12), transparent 64%),
    linear-gradient(150deg, rgba(255, 255, 255, .84), rgba(244, 252, 252, .92));
}

.story::before {
  content: '';
  position: absolute;
  z-index: 0;
  top: -12%;
  bottom: -12%;
  left: -280px;
  width: 210px;
  pointer-events: none;
  background: linear-gradient(90deg, transparent, rgba(43, 212, 205, .045), rgba(255, 255, 255, .28), rgba(43, 212, 205, .035), transparent);
  transform: skewX(-13deg);
  animation: story-scan 10s ease-in-out 2.2s infinite;
}

.story::after {
  content: '';
  position: absolute;
  right: -120px;
  top: -70px;
  width: 520px;
  height: 520px;
  border: 1px solid rgba(20, 184, 196, .1);
  border-radius: 50%;
  box-shadow: 0 0 0 62px rgba(20, 184, 196, .025), 0 0 0 124px rgba(20, 184, 196, .018);
  animation: halo-breathe 8s ease-in-out infinite;
}

.story-content {
  position: relative;
  z-index: 2;
  max-width: 830px;
}

.story-eyebrow {
  display: flex;
  align-items: center;
  gap: 11px;
  color: var(--brand-deep);
  font-size: 11px;
  font-weight: 800;
  letter-spacing: 2.2px;
}

.story-eyebrow::before {
  content: '';
  width: 28px;
  height: 2px;
  border-radius: 2px;
  background: linear-gradient(90deg, var(--brand-2), var(--brand));
}

.story h1 {
  margin: 24px 0 19px;
  color: var(--ink);
  font-size: clamp(36px, 2.65vw, 47px);
  font-weight: 800;
  line-height: 1.18;
  letter-spacing: -1.8px;
}

.story h1 span,
.story h1 em {
  display: block;
}

.story h1 em {
  position: relative;
  width: max-content;
  max-width: 100%;
  color: var(--brand-deep);
  font-style: normal;
}

.story h1 em::after {
  content: '';
  position: absolute;
  right: 0;
  bottom: -5px;
  left: 1px;
  z-index: -1;
  height: 8px;
  border-radius: 8px;
  background: rgba(43, 212, 205, .18);
  transform: scaleX(0);
  transform-origin: left center;
}

.is-ready .story h1 em::after {
  animation: underline-reveal .9s var(--motion-ease) 1.02s both;
}

.story-lead {
  max-width: 700px;
  margin: 0;
  color: var(--text);
  font-size: 15px;
  font-weight: 500;
  line-height: 1.9;
}

.story-lead strong {
  color: var(--ink);
  font-weight: 800;
}

.flow-card {
  position: relative;
  margin-top: 36px;
  padding: 20px 21px 17px;
  overflow: hidden;
  border: 1px solid var(--line);
  border-radius: 21px;
  background: rgba(255, 255, 255, .88);
  box-shadow: 0 1px 2px rgba(16, 52, 60, .04), 0 10px 30px rgba(16, 52, 60, .07);
  transition: transform .28s var(--motion-ease), box-shadow .28s ease, border-color .28s ease;
}

.flow-card::before {
  content: '';
  position: absolute;
  inset: 0;
  pointer-events: none;
  background: linear-gradient(125deg, rgba(43, 212, 205, .055), transparent 42%);
}

.flow-card:hover {
  border-color: rgba(20, 184, 196, .24);
  box-shadow: 0 18px 48px rgba(16, 52, 60, .105);
  transform: translateY(-3px);
}

.flow-head {
  position: relative;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  margin-bottom: 18px;
}

.flow-head > span:first-child {
  color: var(--muted);
  font-size: 10px;
  font-weight: 800;
  letter-spacing: 1.5px;
}

.connected {
  display: flex;
  flex: 0 0 auto;
  align-items: center;
  gap: 7px;
  color: var(--ok);
  font-size: 11px;
  font-weight: 800;
}

.connected i {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: currentColor;
  animation: connected-pulse 2.3s ease-out infinite;
}

.flow {
  position: relative;
  display: grid;
  grid-template-columns: repeat(5, minmax(0, 1fr));
  gap: 10px;
}

.flow::before {
  content: '';
  position: absolute;
  left: 8.5%;
  right: 8.5%;
  top: 28px;
  z-index: 0;
  height: 2px;
  background: linear-gradient(90deg, rgba(20, 184, 196, .12), rgba(20, 184, 196, .62), rgba(20, 184, 196, .12));
  background-size: 220% 100%;
  animation: line-flow 5.8s linear infinite;
}

.flow-signal {
  position: absolute;
  z-index: 0;
  top: 23px;
  left: 8.5%;
  width: 11px;
  height: 11px;
  border: 2px solid var(--brand);
  border-radius: 50%;
  opacity: 0;
  background: #fff;
  box-shadow: 0 0 0 5px rgba(20, 184, 196, .12), 0 0 18px rgba(20, 184, 196, .55);
  animation: signal-travel 6.2s ease-in-out 1.3s infinite;
}

.flow-node {
  position: relative;
  z-index: 1;
  min-width: 0;
  text-align: center;
}

.node-icon {
  position: relative;
  display: grid;
  place-items: center;
  width: 56px;
  height: 56px;
  margin: 0 auto 9px;
  border: 1px solid var(--line);
  border-radius: 17px;
  color: var(--brand-deep);
  background: var(--surface);
  box-shadow: 0 6px 18px rgba(16, 52, 60, .065);
  animation: node-float 4.8s ease-in-out infinite;
  transition: transform .24s var(--motion-ease), box-shadow .24s ease, border-color .24s ease;
}

.node-icon::after {
  content: '';
  position: absolute;
  inset: -5px;
  border: 1px solid transparent;
  border-radius: 21px;
  opacity: 0;
  animation: node-ring 6.2s ease-out infinite;
}

.node-icon .el-icon {
  font-size: 23px;
}

.node-tone-2 .node-icon { color: var(--info); background: #f5f8ff; }
.node-tone-3 .node-icon { color: var(--warn); background: #fff9ef; }
.node-tone-4 .node-icon { color: var(--violet); background: #f8f6ff; }
.node-tone-5 .node-icon { color: var(--ok); background: #f2fbf7; }
.node-tone-2 .node-icon,
.node-tone-2 .node-icon::after { animation-delay: -3.8s; }
.node-tone-3 .node-icon,
.node-tone-3 .node-icon::after { animation-delay: -2.6s; }
.node-tone-4 .node-icon,
.node-tone-4 .node-icon::after { animation-delay: -1.4s; }
.node-tone-5 .node-icon,
.node-tone-5 .node-icon::after { animation-delay: -.2s; }

.flow-node:hover .node-icon {
  border-color: rgba(20, 184, 196, .34);
  box-shadow: 0 14px 28px rgba(16, 52, 60, .11);
  transform: translateY(-5px) scale(1.035);
}

.flow-node strong {
  display: block;
  overflow: hidden;
  color: var(--ink);
  font-size: 12px;
  font-weight: 800;
  line-height: 1.2;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.flow-node > span {
  display: block;
  margin-top: 4px;
  overflow: hidden;
  color: var(--muted);
  font-size: 9.5px;
  font-weight: 700;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.foundation {
  position: relative;
  display: grid;
  grid-template-columns: 1fr 1fr 1.18fr;
  gap: 9px;
  margin-top: 16px;
  padding-top: 15px;
  border-top: 1px dashed var(--line);
}

.foundation-item {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  min-width: 0;
  height: 40px;
  padding: 0 8px;
  border: 1px solid var(--line-soft);
  border-radius: 11px;
  color: var(--text);
  font-size: 10.5px;
  font-weight: 800;
  background: #f9fcfc;
  opacity: 0;
  transition: transform .2s var(--motion-ease), background .2s ease, border-color .2s ease;
}

.foundation-item .el-icon {
  flex: 0 0 auto;
  color: var(--brand-deep);
  font-size: 15px;
}

.foundation-item span {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.foundation-item:hover {
  border-color: rgba(20, 184, 196, .28);
  background: #fff;
  transform: translateY(-2px);
}

.capabilities {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 20px;
  margin-top: 24px;
}

.capability {
  display: flex;
  align-items: center;
  gap: 8px;
  color: var(--muted);
  font-size: 11px;
  font-weight: 700;
  opacity: 0;
}

.capability .el-icon {
  color: var(--brand-deep);
  font-size: 16px;
}

.is-ready .story-eyebrow,
.is-ready .story h1,
.is-ready .story-lead,
.is-ready .flow-card,
.is-ready .capabilities {
  opacity: 0;
  animation: content-enter .78s var(--motion-ease) both;
}

.is-ready .story-eyebrow { animation-delay: .42s; }
.is-ready .story h1 { animation-delay: .51s; }
.is-ready .story-lead { animation-delay: .61s; }
.is-ready .flow-card { animation-delay: .72s; }
.is-ready .capabilities { animation-delay: .86s; }
.is-ready .foundation-item,
.is-ready .capability { animation: mini-enter .55s var(--motion-ease) both; }
.is-ready .foundation-item:nth-child(1) { animation-delay: 1.02s; }
.is-ready .foundation-item:nth-child(2) { animation-delay: 1.1s; }
.is-ready .foundation-item:nth-child(3) { animation-delay: 1.18s; }
.is-ready .capability:nth-child(1) { animation-delay: 1.15s; }
.is-ready .capability:nth-child(2) { animation-delay: 1.22s; }
.is-ready .capability:nth-child(3) { animation-delay: 1.29s; }
.is-ready .capability:nth-child(4) { animation-delay: 1.36s; }

.dieline {
  position: absolute;
  right: -35px;
  bottom: -10px;
  z-index: 1;
  width: 510px;
  height: 285px;
  opacity: .16;
  pointer-events: none;
  animation: dieline-float 9s ease-in-out infinite;
}

.dieline g {
  stroke-dasharray: 7 5;
  animation: blueprint-drift 14s linear infinite;
}

.auth-zone {
  position: relative;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 34px 54px;
  border-left: 1px solid var(--line);
  background: rgba(255, 255, 255, .95);
}

.auth-zone::before {
  content: '';
  position: absolute;
  inset: 0;
  pointer-events: none;
  background: radial-gradient(520px 420px at 100% 100%, rgba(43, 212, 205, .07), transparent 68%);
  background-size: 125% 125%;
  animation: auth-glow 10s ease-in-out infinite alternate;
}

.auth-card {
  position: relative;
  z-index: 2;
  width: min(100%, 455px);
  opacity: 0;
  transform: translateX(24px);
}

.is-ready .auth-card {
  animation: auth-enter .82s var(--motion-ease) .46s both;
}

.auth-card::before {
  content: '';
  position: absolute;
  inset: -24px;
  z-index: -1;
  border: 1px solid transparent;
  border-radius: 24px;
  transition: border-color .3s ease, background .3s ease;
}

.auth-card.is-success::before {
  border-color: rgba(34, 181, 115, .18);
  background: rgba(34, 181, 115, .025);
}

.auth-kicker {
  color: var(--brand-deep);
  font-size: 10.5px;
  font-weight: 800;
  letter-spacing: 1.8px;
}

.auth-card h2 {
  margin: 12px 0 8px;
  color: var(--ink);
  font-size: 31px;
  font-weight: 800;
  line-height: 1.2;
  letter-spacing: -.6px;
}

.auth-subtitle {
  margin: 0 0 26px;
  color: var(--muted);
  font-size: 13px;
  font-weight: 500;
  line-height: 1.7;
}

.tenant-strip {
  display: flex;
  align-items: center;
  height: 50px;
  margin-bottom: 18px;
  padding: 0 13px;
  border: 1px solid var(--line);
  border-radius: 13px;
  box-sizing: border-box;
  background: #fbfdfe;
  transition: transform .2s var(--motion-ease), border-color .2s ease, box-shadow .2s ease;
}

.tenant-strip:hover {
  border-color: rgba(20, 184, 196, .28);
  box-shadow: 0 8px 22px rgba(16, 52, 60, .05);
  transform: translateY(-1px);
}

.tenant-icon {
  display: grid;
  flex: 0 0 auto;
  place-items: center;
  width: 30px;
  height: 30px;
  margin-right: 10px;
  border-radius: 9px;
  color: var(--brand-deep);
  background: var(--brand-pale);
  animation: tenant-icon-breathe 4.6s ease-in-out infinite;
}

.tenant-copy {
  flex: 1;
  min-width: 0;
}

.tenant-copy span,
.tenant-copy strong {
  display: block;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.tenant-copy span {
  color: var(--muted);
  font-size: 9.5px;
  font-weight: 700;
  letter-spacing: .5px;
}

.tenant-copy strong {
  margin-top: 2px;
  color: var(--ink);
  font-size: 12px;
  font-weight: 800;
}

.tenant-change {
  flex: 0 0 auto;
  padding: 7px 8px;
  border: 0;
  border-radius: 8px;
  color: var(--brand-deep);
  font: inherit;
  font-size: 11px;
  font-weight: 800;
  cursor: pointer;
  background: transparent;
}

.tenant-change:hover,
.tenant-change:focus-visible {
  outline: none;
  background: var(--brand-pale);
}

.tenant-field {
  display: grid;
  grid-template-rows: 0fr;
  margin-top: -8px;
  margin-bottom: 0;
  overflow: hidden;
  opacity: 0;
  transition: grid-template-rows .34s var(--motion-ease), opacity .25s ease, margin .34s var(--motion-ease);
}

.tenant-field-inner {
  min-height: 0;
  overflow: hidden;
}

.tenant-field.is-open {
  grid-template-rows: 1fr;
  margin-bottom: 2px;
  opacity: 1;
}

.auth-form :deep(.el-form-item) {
  margin-bottom: 16px;
}

.auth-form :deep(.el-form-item__label) {
  height: auto;
  margin: 0;
  padding: 0 0 8px;
  color: var(--ink);
  font-size: 11.5px;
  font-weight: 800;
  line-height: 1.2;
}

.auth-form :deep(.el-input__wrapper) {
  min-height: 51px;
  padding: 0 14px;
  border: 1px solid var(--line);
  border-radius: 13px;
  box-sizing: border-box;
  background: #fff;
  box-shadow: 0 1px 0 rgba(16, 52, 60, .02) !important;
  transition: border-color .18s ease, box-shadow .18s ease;
}

.auth-form :deep(.el-input__wrapper:hover) {
  border-color: rgba(20, 184, 196, .38);
}

.auth-form :deep(.el-input__wrapper.is-focus) {
  border-color: rgba(20, 184, 196, .72);
  box-shadow: 0 0 0 4px rgba(20, 184, 196, .1) !important;
}

.auth-form :deep(.el-input__inner) {
  color: var(--ink);
  font-size: 13px;
  font-weight: 600;
}

.auth-form :deep(.el-input__inner::placeholder) {
  color: #a8bac0;
  font-weight: 500;
}

.auth-form :deep(.el-input__prefix-inner) {
  color: var(--muted);
  font-size: 18px;
}

.auth-form :deep(.el-form-item__error) {
  padding-top: 3px;
  font-size: 10px;
}

.password-toggle {
  display: grid;
  place-items: center;
  padding: 7px;
  border: 0;
  color: var(--muted);
  cursor: pointer;
  background: transparent;
  transition: color .2s ease, transform .2s var(--motion-ease);
}

.password-toggle:hover,
.password-toggle:focus-visible {
  color: var(--brand-deep);
  outline: none;
  transform: scale(1.08);
}

.form-meta {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin: 2px 0 20px;
  color: var(--muted);
  font-size: 10.5px;
  font-weight: 600;
}

.secure-note {
  display: flex;
  align-items: center;
  gap: 7px;
}

.secure-note .el-icon {
  color: var(--ok);
  font-size: 14px;
  animation: shield-breathe 3.4s ease-in-out infinite;
}

.login-button.el-button {
  position: relative;
  width: 100%;
  height: 51px;
  margin: 0;
  overflow: hidden;
  border: 0;
  border-radius: 13px;
  color: #fff;
  font-size: 13.5px;
  font-weight: 800;
  letter-spacing: .2px;
  background: linear-gradient(118deg, var(--brand-2), var(--brand));
  box-shadow: 0 10px 22px rgba(20, 184, 196, .26);
  transition: transform .18s ease, box-shadow .18s ease, background .18s ease;
}

.login-button.el-button::before {
  content: '';
  position: absolute;
  inset: -2px auto -2px -45%;
  width: 34%;
  background: linear-gradient(90deg, transparent, rgba(255, 255, 255, .55), transparent);
  transform: skewX(-18deg);
  animation: button-sheen 4.8s ease-in-out 2.2s infinite;
}

.login-button.el-button:hover,
.login-button.el-button:focus-visible {
  color: #fff;
  outline: none;
  background: linear-gradient(118deg, #32dbd4, #12abb7);
  box-shadow: 0 13px 26px rgba(20, 184, 196, .31), 0 0 0 4px rgba(20, 184, 196, .1);
  transform: translateY(-1px);
}

.login-button.el-button.is-loading {
  background: linear-gradient(118deg, #20c7c2, var(--brand-deep));
}

.login-button.el-button.is-success {
  background: linear-gradient(118deg, #39c987, var(--ok));
  box-shadow: 0 10px 24px rgba(34, 181, 115, .25);
}

.success-label {
  display: inline-flex;
  align-items: center;
  gap: 8px;
}

.divider {
  display: flex;
  align-items: center;
  gap: 12px;
  margin: 19px 0;
  color: var(--faint);
  font-size: 10px;
  font-weight: 700;
}

.divider::before,
.divider::after {
  content: '';
  flex: 1;
  height: 1px;
  background: var(--line-soft);
}

.sso-button {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 9px;
  width: 100%;
  height: 49px;
  border: 1px solid var(--line);
  border-radius: 13px;
  color: var(--ink);
  font: inherit;
  font-size: 12.5px;
  font-weight: 800;
  cursor: pointer;
  background: #fff;
  transition: transform .2s var(--motion-ease), border-color .2s ease, box-shadow .2s ease, background .2s ease;
}

.sso-button .el-icon {
  color: var(--brand-deep);
  font-size: 17px;
}

.sso-button:hover,
.sso-button:focus-visible {
  border-color: rgba(20, 184, 196, .55);
  color: var(--brand-deep);
  outline: none;
  background: #fbffff;
  box-shadow: 0 10px 24px rgba(16, 52, 60, .06);
  transform: translateY(-1px);
}

.sso-button.is-loading {
  color: var(--brand-deep);
  cursor: wait;
  background: var(--brand-pale);
}

.sso-button.is-loading .el-icon {
  animation: spin 1s linear infinite;
}

.security-row {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 8px;
  margin-top: 23px;
}

.security-item {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  min-width: 0;
  height: 42px;
  padding: 0 6px;
  border: 1px solid var(--line-soft);
  border-radius: 11px;
  color: var(--muted);
  font-size: 9.5px;
  font-weight: 800;
  background: #f8fbfc;
  transition: transform .2s var(--motion-ease), border-color .2s ease, box-shadow .2s ease, background .2s ease;
}

.security-item .el-icon {
  flex: 0 0 auto;
  color: var(--brand-deep);
  font-size: 14px;
}

.security-item span {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.security-item:hover {
  border-color: rgba(20, 184, 196, .24);
  background: #fff;
  box-shadow: 0 8px 18px rgba(16, 52, 60, .045);
  transform: translateY(-2px);
}

.auth-foot {
  margin: 21px 0 0;
  color: var(--faint);
  font-size: 9.5px;
  font-weight: 600;
  line-height: 1.7;
  text-align: center;
}

.page-footer {
  display: flex;
  flex: 0 0 auto;
  align-items: flex-end;
  justify-content: space-between;
  height: 37px;
  padding: 0 9px;
  color: var(--muted);
  font-size: 10px;
  font-weight: 600;
  opacity: 0;
}

.is-ready .page-footer {
  animation: footer-enter .62s var(--motion-ease) .62s both;
}

.platform-name {
  display: flex;
  align-items: center;
  gap: 8px;
}

.platform-name strong {
  color: var(--text);
  font-weight: 800;
}

@keyframes topbar-enter { to { opacity: 1; transform: translateY(0); } }
@keyframes stage-enter { to { opacity: 1; transform: translateY(0) scale(1); } }
@keyframes footer-enter { from { opacity: 0; transform: translateY(8px); } to { opacity: 1; transform: none; } }
@keyframes content-enter { from { opacity: 0; transform: translateY(18px); } to { opacity: 1; transform: none; } }
@keyframes auth-enter { to { opacity: 1; transform: translateX(0); } }
@keyframes mini-enter { from { opacity: 0; transform: translateY(9px) scale(.98); } to { opacity: 1; transform: none; } }
@keyframes underline-reveal { to { transform: scaleX(1); } }
@keyframes grid-drift { to { background-position: 42px 42px, 42px 42px; } }
@keyframes orb-float-a { 0%, 100% { transform: translate(0, 0) scale(1); } 50% { transform: translate(22px, -18px) scale(1.035); } }
@keyframes orb-float-b { 0%, 100% { transform: translate(0, 0); } 50% { transform: translate(-18px, 22px); } }
@keyframes logo-hover { 0%, 100% { transform: translateY(0) rotate(0); } 50% { transform: translateY(-3px) rotate(.6deg); } }
@keyframes status-pulse { 0% { box-shadow: 0 0 0 0 rgba(34, 181, 115, .32); } 70% { box-shadow: 0 0 0 8px rgba(34, 181, 115, 0); } 100% { box-shadow: 0 0 0 0 rgba(34, 181, 115, 0); } }
@keyframes status-sheen { 0%, 70% { left: -42%; } 88%, 100% { left: 120%; } }
@keyframes story-scan { 0%, 12% { left: -280px; opacity: 0; } 22% { opacity: 1; } 50% { left: 115%; opacity: .9; } 60%, 100% { left: 115%; opacity: 0; } }
@keyframes halo-breathe { 0%, 100% { transform: scale(1); opacity: 1; } 50% { transform: scale(1.035); opacity: .72; } }
@keyframes line-flow { to { background-position: -220% 0; } }
@keyframes signal-travel { 0% { left: 8.5%; opacity: 0; transform: scale(.7); } 8% { opacity: 1; transform: scale(1); } 88% { opacity: 1; transform: scale(1); } 96%, 100% { left: 91.5%; opacity: 0; transform: scale(.7); } }
@keyframes node-float { 0%, 100% { transform: translateY(0); } 50% { transform: translateY(-2.5px); } }
@keyframes node-ring { 0%, 10% { opacity: 0; transform: scale(.9); border-color: transparent; } 18% { opacity: 1; border-color: rgba(20, 184, 196, .34); } 32%, 100% { opacity: 0; transform: scale(1.18); border-color: transparent; } }
@keyframes connected-pulse { 0% { box-shadow: 0 0 0 0 rgba(34, 181, 115, .28); } 70% { box-shadow: 0 0 0 7px rgba(34, 181, 115, 0); } 100% { box-shadow: 0 0 0 0 rgba(34, 181, 115, 0); } }
@keyframes blueprint-drift { to { stroke-dashoffset: -48; } }
@keyframes dieline-float { 0%, 100% { transform: translateY(0); } 50% { transform: translateY(-6px); } }
@keyframes auth-glow { from { background-position: 0 0; } to { background-position: 18% 14%; } }
@keyframes tenant-icon-breathe { 0%, 100% { box-shadow: 0 0 0 0 rgba(20, 184, 196, 0); } 50% { box-shadow: 0 0 0 7px rgba(20, 184, 196, .07); } }
@keyframes button-sheen { 0%, 58% { left: -45%; } 78%, 100% { left: 125%; } }
@keyframes shield-breathe { 0%, 100% { transform: scale(1); } 50% { transform: scale(1.08); } }
@keyframes spin { to { transform: rotate(360deg); } }

@media (max-width: 1360px) {
  .login-page { padding: 24px 28px 22px; }
  .login-stage { grid-template-columns: minmax(0, 1.2fr) minmax(430px, .8fr); }
  .story { padding-right: 42px; padding-left: 42px; }
  .auth-zone { padding-right: 38px; padding-left: 38px; }
  .story h1 { font-size: 38px; }
  .story-lead { font-size: 13.5px; line-height: 1.75; }
}

@media (max-width: 1100px) {
  .login-page {
    min-height: 100vh;
    min-height: 100dvh;
    height: auto;
    padding: 22px;
    overflow-x: hidden;
    overflow-y: auto;
  }

  .login-stage {
    display: block;
    flex: 0 0 auto;
    min-height: calc(100dvh - 145px);
    border-radius: 24px;
  }

  .story { display: none; }

  .auth-zone {
    min-height: calc(100dvh - 145px);
    padding: 50px clamp(28px, 8vw, 80px);
    border-left: 0;
  }

  .auth-card { max-width: 500px; }
}

@media (max-width: 640px) {
  .login-page { padding: 14px; }
  .topbar { height: 48px; padding: 0 2px; }
  .brand { gap: 10px; }
  .brand-mark { width: 38px; height: 38px; }
  .brand-copy strong { font-size: 18px; }
  .brand-copy span { display: none; }
  .status-pill { display: none; }
  .lang-button { min-height: 38px; padding: 0 11px; }
  .lang-button > span { max-width: 92px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .login-stage { min-height: calc(100dvh - 112px); margin-top: 10px; border-radius: 20px; }
  .auth-zone { min-height: calc(100dvh - 112px); padding: 34px 22px; }
  .auth-card h2 { font-size: 27px; }
  .auth-subtitle { margin-bottom: 21px; }
  .form-meta { align-items: flex-start; flex-direction: column; gap: 5px; margin-bottom: 16px; }
  .security-row { gap: 6px; margin-top: 18px; }
  .security-item { height: 38px; font-size: 8.5px; }
  .security-item .el-icon { display: none; }
  .auth-foot { margin-top: 16px; }
  .page-footer { height: 26px; padding: 0 3px; font-size: 8.5px; }
  .platform-name span { display: none; }
}

@media (max-width: 390px) {
  .login-page { padding: 10px; }
  .auth-zone { padding: 28px 16px; }
  .tenant-strip { padding: 0 10px; }
  .tenant-icon { margin-right: 8px; }
  .tenant-change { padding-right: 3px; padding-left: 5px; font-size: 10px; }
}

@media (max-height: 820px) and (min-width: 1101px) {
  .login-page { padding: 18px 28px 16px; }
  .topbar { height: 48px; }
  .brand-mark { width: 37px; height: 37px; }
  .brand-copy strong { font-size: 18px; }
  .brand-copy span { margin-top: 3px; font-size: 8.5px; }
  .status-pill,
  .lang-button { min-height: 34px; padding: 0 12px; }
  .login-stage { grid-template-columns: minmax(0, 1.36fr) minmax(410px, .74fr); margin-top: 10px; border-radius: 24px; }
  .story { padding: 34px 48px 26px; }
  .story-eyebrow { font-size: 9px; letter-spacing: 1.8px; }
  .story h1 { margin: 16px 0 11px; font-size: 36px; line-height: 1.13; letter-spacing: -1.3px; }
  .story h1 em::after { bottom: -3px; height: 6px; }
  .story-lead { max-width: 650px; font-size: 12.5px; line-height: 1.65; }
  .flow-card { margin-top: 20px; padding: 13px 16px 12px; border-radius: 17px; }
  .flow-head { margin-bottom: 11px; }
  .flow-head > span:first-child { font-size: 8.5px; }
  .connected { font-size: 9.5px; }
  .flow::before { top: 22px; }
  .flow-signal { top: 17px; width: 10px; height: 10px; }
  .node-icon { width: 44px; height: 44px; margin-bottom: 6px; border-radius: 14px; }
  .node-icon::after { border-radius: 18px; }
  .node-icon .el-icon { font-size: 19px; }
  .flow-node strong { font-size: 10.5px; }
  .flow-node > span { margin-top: 2px; font-size: 8px; }
  .foundation { margin-top: 10px; padding-top: 9px; }
  .foundation-item { height: 31px; font-size: 8.8px; border-radius: 9px; }
  .capabilities { gap: 15px; margin-top: 12px; }
  .capability { gap: 6px; font-size: 9px; }
  .dieline { right: -28px; bottom: -28px; width: 430px; height: 240px; }
  .auth-zone { padding: 20px 38px; }
  .auth-card { width: min(100%, 420px); }
  .auth-kicker { font-size: 9px; }
  .auth-card h2 { margin: 8px 0 5px; font-size: 25px; }
  .auth-subtitle { margin-bottom: 14px; font-size: 11px; line-height: 1.5; }
  .tenant-strip { height: 42px; margin-bottom: 11px; border-radius: 11px; }
  .tenant-icon { width: 27px; height: 27px; }
  .tenant-copy span { font-size: 8px; }
  .tenant-copy strong { font-size: 10.5px; }
  .tenant-change { font-size: 9.5px; }
  .auth-form :deep(.el-form-item) { margin-bottom: 10px; }
  .auth-form :deep(.el-form-item__label) { padding-bottom: 5px; font-size: 10px; }
  .auth-form :deep(.el-input__wrapper) { min-height: 43px; border-radius: 11px; }
  .auth-form :deep(.el-input__inner) { font-size: 11.5px; }
  .form-meta { margin: 0 0 11px; font-size: 8.8px; }
  .login-button.el-button { height: 44px; border-radius: 11px; font-size: 11.5px; }
  .divider { margin: 11px 0; font-size: 8.5px; }
  .sso-button { height: 42px; border-radius: 11px; font-size: 10.5px; }
  .security-row { gap: 6px; margin-top: 13px; }
  .security-item { height: 34px; border-radius: 9px; font-size: 8.2px; }
  .auth-foot { margin-top: 10px; font-size: 8px; line-height: 1.45; }
  .page-footer { height: 26px; font-size: 8.5px; }
}

@media (prefers-reduced-motion: reduce) {
  .login-page *,
  .login-page *::before,
  .login-page *::after {
    scroll-behavior: auto !important;
    animation-duration: .001ms !important;
    animation-iteration-count: 1 !important;
    transition-duration: .001ms !important;
  }

  .topbar,
  .login-stage,
  .auth-card,
  .page-footer,
  .story-eyebrow,
  .story h1,
  .story-lead,
  .flow-card,
  .capabilities,
  .foundation-item,
  .capability {
    opacity: 1 !important;
    transform: none !important;
  }
}
</style>
