<template>
  <div v-if="store.impersonating" class="impersonation-banner">
    <span class="imp-text">
      {{ t('platform.impersonation.bannerActive', {
        tenantName: store.impersonating.tenantName,
        userName: store.impersonating.userName
      }) }}
      <span class="imp-sep">·</span>
      {{ t('platform.impersonation.countdown', { min: minutesLeft }) }}
    </span>
    <el-button size="small" type="warning" plain :loading="ending" @click="endImpersonation">
      {{ t('platform.impersonation.end') }}
    </el-button>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { usePlatformStore } from '@/stores/platform'
import { impersonationApi } from '@/api/platform/impersonation'
import { logoutAuthentication, requireCurrentProfile } from '@/auth/browserAuthentication'

// 多租户合规 #5（T9）impersonation 全局横幅 + 倒计时（R8）。挂在 LayoutView 顶部。
// 到期结束整个浏览器会话；不使用 refresh 悄悄变回另一身份。
const { t } = useI18n()
const router = useRouter()
const store = usePlatformStore()
const ending = ref(false)
const now = ref(Date.now())
let timer: number | undefined

const minutesLeft = computed(() => {
  if (!store.impersonating) return 0
  const ms = store.impersonating.expiresAt - now.value
  return Math.max(0, Math.ceil(ms / 60000))
})

function tick() {
  now.value = Date.now()
  if (store.impersonating && store.impersonating.expiresAt - now.value <= 0) {
    if (timer) window.clearInterval(timer)
    void logoutAuthentication().then(() => router.replace('/login')).catch(() => {})
    ElMessage.info(t('platform.impersonation.autoEnded'))
  }
}

async function endImpersonation() {
  ending.value = true
  try {
    const res = await impersonationApi.end()
    requireCurrentProfile(res)
    router.push('/dashboard')
  } catch (error: any) {
    ElMessage.error(error?.response?.data?.message || error?.message || '身份切换结果未确认。')
  } finally {
    ending.value = false
  }
}

onMounted(() => {
  timer = window.setInterval(tick, 1000)
})
onBeforeUnmount(() => {
  if (timer) window.clearInterval(timer)
})
</script>

<style scoped>
.impersonation-banner {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 14px;
  padding: 8px 16px;
  background: linear-gradient(90deg, #b45309, #d97706);
  color: #fff;
  font-size: 14px;
  font-weight: 600;
  box-shadow: 0 2px 8px rgba(180, 83, 9, 0.3);
}
.imp-text {
  display: flex;
  align-items: center;
  gap: 8px;
}
.imp-sep {
  opacity: 0.7;
}
</style>
