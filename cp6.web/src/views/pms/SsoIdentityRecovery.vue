<template>
  <section class="identity-recovery">
    <h4>首次身份映射恢复</h4>
    <p>使用登录失败页提供的原操作编号查询。本工具不会重放登录、改绑账号或删除已提交身份。</p>
    <el-input v-model="reference" aria-label="身份映射原操作编号" :disabled="busy" maxlength="64" />
    <el-button :loading="busy" :disabled="!valid || busy" @click="resolve(false)">查询身份映射</el-button>
    <el-button :loading="busy" :disabled="busy || queriedRef !== reference || state !== 'UNKNOWN'" @click="resolve(true)">关闭这次未确认映射</el-button>
    <p role="status">{{ notice }}</p>
  </section>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import http from '@/api/http'
const reference = ref(''), queriedRef = ref(''), state = ref(''), notice = ref(''), busy = ref(false)
const valid = computed(() => /^[a-f0-9]{64}$/i.test(reference.value))
watch(reference, () => { queriedRef.value = ''; state.value = ''; notice.value = '' })
async function resolve(close: boolean) {
  if (busy.value || !valid.value || close && (queriedRef.value !== reference.value || state.value !== 'UNKNOWN')) return
  busy.value = true; const original = reference.value
  try {
    const url = '/sys/sso-config/identity-operations/' + original
    const result = await (close ? http.post(url + '/close', {}) : http.get(url)) as unknown as { operationRef: string; state: string }
    if (result.operationRef !== original) throw new Error('original operation mismatch')
    queriedRef.value = original; state.value = result.state
    notice.value = ({ IDENTITY_BOUND: '原操作已完成身份绑定，绑定继续保留。用户可重新发起 SSO。',
      USER_PROVISIONED: '原操作已创建账号，账号继续保留。用户可重新发起 SSO。',
      CLOSED: '原操作已关闭，迟到请求不能继续写入。用户可重新发起 SSO。',
      UNKNOWN: '原操作结果尚未确认。可明确关闭原操作，或保留编号继续查询。' } as Record<string, string>)[result.state] || '原操作状态无法确认。'
  } catch { notice.value = '查询或关闭未确认。请确认当前租户、有效登录及 SSO 编辑权限，并继续使用同一原操作编号。' }
  finally { busy.value = false }
}
</script>
<style scoped>
.identity-recovery { border-top: 1px solid var(--el-border-color); margin-top: 20px; padding-top: 12px; }
.identity-recovery .el-input { margin-bottom: 12px; }
</style>
