<template>
  <section v-if="visible" class="authentication-boundary" role="alertdialog" aria-modal="true" aria-labelledby="authentication-recovery-title">
    <div class="authentication-recovery-card">
      <h2 id="authentication-recovery-title">{{ title }}</h2>
      <p>{{ message }}</p>
      <p v-if="error" role="alert">{{ error }}</p>
      <div class="authentication-recovery-actions">
        <el-button :loading="busy" @click="check">检查登录状态</el-button>
        <el-button :disabled="busy" @click="close">结束本次登录</el-button>
      </div>
      <p class="authentication-recovery-note">此操作不会重新提交业务请求。业务结果未确认时，请仍按原操作记录查询。</p>
    </div>
  </section>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { authenticationSnapshot, logoutAuthentication, recoverAuthentication } from './browserAuthentication'
import { AUTH_EVENT, AUTH_EPOCH, AUTH_RECORD, clearTabIdentity } from './identity'
import { navigateAuthentication } from './navigation'
import { usePermissionStore } from '@/stores/permission'
import { usePlatformStore } from '@/stores/platform'
import { resetRoutes, addDynamicRoutes } from '@/router'

const route = useRoute(); const router = useRouter()
const current = ref(authenticationSnapshot()); const busy = ref(false); const error = ref('')
let installedProfile = ''
let wasAuthenticated = localStorage.getItem('cp6_authed') === '1'
const visible = computed(() => ['unknown', 'closing', 'sso', 'working'].includes(current.value.phase)
  || current.value.phase === 'pending' && !['/sys/2fa-challenge', '/sys/2fa-enroll'].includes(route.path))
const title = computed(() => current.value.phase === 'sso' ? '等待统一登录返回' : current.value.phase === 'closing' ? '正在结束登录' : '确认登录状态')
const message = computed(() => current.value.phase === 'sso'
  ? '另一个页面可能正在完成统一登录。返回后可检查状态；取消时会先关闭原交互。'
  : '本次登录或退出结果尚未确认。可以安全检查状态，或先结束原交互，再重新登录。')

function synchronize() {
  const next = authenticationSnapshot()
  const authenticated = localStorage.getItem('cp6_authed') === '1'
  if (next.epoch !== current.value.epoch || wasAuthenticated && !authenticated) {
    clearTabIdentity(); usePermissionStore().reset(); resetRoutes(); installedProfile = ''
    if (!localStorage.getItem('cp6_authed') && route.path !== '/login') void router.replace('/login')
  }
  usePlatformStore().refreshFlag()
  const profileStamp = next.epoch + ':' + localStorage.getItem('menus')
  if (['active', 'restricted'].includes(next.phase) && localStorage.getItem('cp6_authed') === '1' && installedProfile !== profileStamp) {
    installedProfile = profileStamp
    addDynamicRoutes(JSON.parse(localStorage.getItem('menus') || '[]'))
    if (next.phase === 'restricted') void router.replace('/sys/change-password')
    else if (['/login', '/sys/2fa-challenge', '/sys/2fa-enroll'].includes(route.path)) void router.replace('/')
  }
  current.value = next
  wasAuthenticated = authenticated
  if (next.phase === 'unknown' && route.path !== '/login') void router.replace('/login')
  if (next.phase === 'anonymous' && !['/login', '/sso/landing'].includes(route.path)) void router.replace('/login')
}
function storage(event: StorageEvent) {
  if ([AUTH_EPOCH, AUTH_RECORD, 'cp6_authed'].includes(event.key || '')) synchronize()
}
onMounted(() => {
  window.addEventListener(AUTH_EVENT, synchronize); window.addEventListener('storage', storage); window.addEventListener('focus', synchronize)
  window.addEventListener('pageshow', synchronize); document.addEventListener('visibilitychange', synchronize)
  synchronize()
})
onBeforeUnmount(() => {
  window.removeEventListener(AUTH_EVENT, synchronize); window.removeEventListener('storage', storage); window.removeEventListener('focus', synchronize)
  window.removeEventListener('pageshow', synchronize); document.removeEventListener('visibilitychange', synchronize)
})
async function check() {
  busy.value = true; error.value = ''
  try {
    const result = await recoverAuthentication()
    if (result.profile) navigateAuthentication(result.profile)
    else if (result.state === 'PENDING') navigateAuthentication({ twoFactorRequired: true })
    else if (result.state === 'CLOSED') await router.replace('/login')
    else error.value = '尚不能确认可用登录，请稍后检查，或结束本次登录。'
  } catch { error.value = '暂时无法确认结果。保留当前记录，稍后再检查。' }
  finally { busy.value = false; synchronize() }
}
async function close() {
  busy.value = true; error.value = ''
  try { await logoutAuthentication(); await router.replace('/login') }
  catch { error.value = '关闭结果尚未确认。请保持隔离，稍后再检查。' }
  finally { busy.value = false; synchronize() }
}
</script>

<style scoped>
.authentication-boundary { position: fixed; inset: 0; z-index: 5000; display: grid; place-items: center; padding: 24px; background: rgb(12 28 40 / 80%); }
.authentication-recovery-card { width: min(100%, 540px); padding: 28px; border-radius: 16px; background: white; color: #183848; box-shadow: 0 20px 60px #0005; }
.authentication-recovery-card h2 { margin-top: 0; }
.authentication-recovery-card p { line-height: 1.7; }
.authentication-recovery-actions { display: flex; flex-wrap: wrap; gap: 10px; }
.authentication-recovery-note { font-size: 13px; color: #526976; }
</style>
