<template>
  <div class="sso-landing-shell">
    <el-card class="sso-landing-card" shadow="never">
      <!-- 失败：回调 302 带 ?error=E-SEC-0xx 落地，按码本地化 + 回登录 -->
      <template v-if="errorCode">
        <el-result icon="error" :title="t('sec.sso.landingError')" :sub-title="errorText">
          <template #extra>
            <el-button type="primary" @click="backToLogin">{{ t('sec.sso.backToLogin') }}</el-button>
          </template>
        </el-result>
        <el-alert v-if="identityNotice" :title="identityNotice" type="info" :closable="false" />
        <p v-if="operationRef">原操作编号：<code>{{ operationRef }}</code></p>
      </template>

      <!-- 成功：同站 XHR 拉 profile（携 cp6_at Cookie，SameSite=Strict 放行）→ 存登录态跳首页 -->
      <template v-else>
        <div class="sso-landing-loading" v-loading="true" :element-loading-text="t('sec.sso.landing')">
          <div class="sso-landing-spacer"></div>
        </div>
      </template>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { recoverAuthentication } from '@/auth/browserAuthentication'
import { navigateAuthentication } from '@/auth/navigation'

const { t, te } = useI18n()
const route = useRoute()
const router = useRouter()

// 回调失败时 302 到 /sso/landing?error=E-SEC-0xx；成功时无 error。
const errorCode = ref<string>(typeof route.query.error === 'string' ? route.query.error : '')
const identityNotice = ref('')
// A reference is only a support locator. Query parameters never prove an identity mapping or session.
const operationRef = ref(typeof route.query.operationRef === 'string' && /^[a-f0-9]{64}$/i.test(route.query.operationRef) ? route.query.operationRef : '')
// 已知错误码（E-SEC-020~029）走五语本地化；未知则原样显示。
const errorText = computed(() => (errorCode.value && te(errorCode.value) ? t(errorCode.value) : errorCode.value))

function backToLogin() {
  router.replace('/login')
}

onMounted(async () => {
  try {
    // The query is only a display hint. Owner status and the original attempt decide the next page.
    if (errorCode.value) {
      const result = await recoverAuthentication(true)
      if (result.identityState === 'IDENTITY_BOUND' || result.identityState === 'USER_PROVISIONED')
        identityNotice.value = '原身份映射已完成并保留。本次登录已结束，可重新发起 SSO；后续登录仍需满足当前认证政策。'
      else if (result.identityState === 'CLOSED') identityNotice.value = '已关闭此浏览器的原身份映射操作，迟到请求不能继续写入。'
      if (errorCode.value === 'SSO_IDENTITY_MAPPING_UNKNOWN') {
        errorCode.value = '首次身份映射结果曾中断。请保留原操作编号；若仍被阻止，请由本租户管理员查询或关闭该原操作。'
      }
      return
    }
    const result = await recoverAuthentication()
    if (result.profile) navigateAuthentication(result.profile)
    else if (result.state === 'PENDING') navigateAuthentication({ twoFactorRequired: true })
    else errorCode.value = '登录结果尚未确认，请检查状态或结束原交互。'
  } catch {
    errorCode.value = '登录结果尚未确认，请检查状态或结束原交互。'
  }
})

</script>

<style scoped>
.sso-landing-shell {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 100vh;
  min-height: 100dvh;
  padding: 2rem;
  background: linear-gradient(135deg, #07111f 0%, #0b1d34 42%, #12345d 100%);
}
.sso-landing-card {
  width: min(100%, 460px);
  border-radius: 18px;
}
.sso-landing-spacer {
  height: 140px;
}
</style>
