<template>
  <div class="pending-authentication-actions">
    <p v-if="expiresAt">本次验证有效至 {{ new Date(expiresAt).toLocaleString() }}；刷新页面不会延长有效期。</p>
    <p v-if="error" role="alert">{{ error }}</p>
    <el-button :loading="busy" @click="check">检查验证状态</el-button>
    <el-button :disabled="busy" @click="close">结束本次登录</el-button>
  </div>
</template>
<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { authenticationSnapshot, logoutAuthentication, recoverAuthentication } from './browserAuthentication'
import { navigateAuthentication } from './navigation'
const router = useRouter(); const busy = ref(false); const error = ref('')
const expiresAt = authenticationSnapshot().expiresAt
async function check() {
  busy.value = true; error.value = ''
  try {
    const result = await recoverAuthentication()
    if (result.profile) navigateAuthentication(result.profile)
    else if (result.state === 'CLOSED') await router.replace('/login')
    else if (result.state === 'PENDING') error.value = '验证仍待完成。已绑定验证器可输入新验证码；材料丢失时请结束本次登录后重试。'
    else error.value = '验证结果尚未确认。请保留原记录，检查状态或结束本次登录。'
  } catch { error.value = '暂时无法确认结果，请稍后检查。' }
  finally { busy.value = false }
}
async function close() {
  busy.value = true; error.value = ''
  try { await logoutAuthentication(); await router.replace('/login') }
  catch { error.value = '关闭结果尚未确认，请稍后检查。' }
  finally { busy.value = false }
}
</script>
<style scoped>
.pending-authentication-actions { margin-top: 16px; font-size: 13px; line-height: 1.6; color: #526976; }
</style>
