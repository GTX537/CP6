<template>
  <main class="transfer-environment">
    <h1>调拨来源环境</h1>
    <p>登记本租户使用的原生产品来源。启用后仍需核对实际业务权限、库位和库存条件。</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <button :disabled="busy" @click="reload">重新载入</button>
    <template v-if="context">
      <dl><dt>环境</dt><dd>{{ context.deployment.environmentId }}</dd><dt>模式</dt><dd>{{ context.deployment.mode === 'SIMULATION' ? '模拟' : '生产' }}</dd><template v-if="context.deployment.sandboxScopeId"><dt>沙箱范围</dt><dd>{{ context.deployment.sandboxScopeId }}</dd></template><dt>当前注册</dt><dd>{{ context.registration ? (context.registration.original.enabled ? '已启用' : '已停用') + ' · v' + context.version : '尚未注册' }}</dd></dl>
      <p>环境由服务端配置确定。停用会阻止后续来源消费，已经保存的来源归属和历史原件保持可追溯。</p>
      <details><summary>当前支持的来源</summary><ul><li v-for="owner in context.availableOwners" :key="owner">{{ ownerName(owner) }}</li></ul></details>
      <section v-if="pending"><p>有一笔提交结果待确认，请先查询原操作。</p><button :disabled="busy" @click="recover(false)">查询原操作</button><button :disabled="busy" @click="recover(true)">关闭未提交原操作</button></section>
      <form @submit.prevent="save">
        <fieldset :disabled="busy || !!pending || !canEdit">
          <label>注册状态<select v-model="enabled" aria-label="注册状态" required><option disabled value="">请选择</option><option value="enabled">启用</option><option v-if="context.registration" value="disabled">停用</option></select></label>
          <label>操作原因<textarea v-model="reason" aria-label="操作原因" maxlength="1000" required /></label><button>保存注册</button>
        </fieldset>
      </form>
      <form v-if="context.registration" @submit.prevent="readHistory"><label>历史版本<input v-model="historyVersion" aria-label="历史版本" pattern="[1-9][0-9]*" required /></label><button :disabled="busy">读取历史</button></form>
      <article v-if="history"><h2>历史 v{{ history.reference.version }}</h2><p>{{ history.original.enabled ? '已启用' : '已停用' }} · {{ history.original.reason }}</p><details><summary>查看保存原件</summary><pre>{{ history.originalJson }}</pre></details></article>
    </template>
  </main>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { transferEnvironmentApi as api, type TransferEnvironmentContext, type TransferRegistration } from '@/api/wms/transferEnvironment'
import { transferResponse, transferContextKey, pendingTransferEnvironment, saveTransferEnvironment, resolveTransferEnvironment, type PendingTransferEnvironment } from '@/auth/transferEnvironmentRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
const permission = usePermissionStore(), canEdit = computed(() => permission.has('wms-transfer-environment:edit'))
const context = ref<TransferEnvironmentContext | null>(null), history = ref<TransferRegistration | null>(null), pending = ref<PendingTransferEnvironment | null>(null)
const busy = ref(false), enabled = ref(''), reason = ref(''), historyVersion = ref('1'), error = ref(''), notice = ref('')
let live = true, generation = 0
const names: Record<string, string> = { 'Main.Erp.ProductIdentity.v1': '商务产品', 'Main.Erp.BusinessPartnerIdentity.v1': '客户身份', 'Main.Erp.UomProfile.v1': '计量单位', 'Main.Erp.ProductUnit.v1': '产品单位绑定', 'Main.Plm.EngineeringProduct.v1': '工程产品', 'Main.Plm.EngineeringMapping.v1': '工程与商务映射', 'Main.Space.PhysicalSiteIdentity.v1': '物理站点', 'Main.Plm.EngineeringBaseline.v1': '冻结工程基线', 'Main.Wms.InventoryIdentity.v1': '库存货权、用途及产品身份' }
const ownerName = (owner: string) => names[owner] || '未知来源，请联系管理员核对'
function clear() { context.value = null; history.value = null; pending.value = null; reason.value = ''; enabled.value = '' }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return; const epoch = generation, stamp = identityStamp()
  const check = () => { requireIdentity(stamp); if (!live || epoch !== generation || !authenticationCanRead()) throw new Error('当前身份已变化。') }
  busy.value = true; error.value = ''; notice.value = ''
  try { check(); await action(check) }
  catch (e: any) { if (live && epoch === generation) { if ([401, 403].includes(e?.response?.status)) clear(); error.value = e?.message || '暂时无法完成，请核对权限和来源环境。' } }
  finally { if (live && epoch === generation) { busy.value = false; if (context.value) { try { pending.value = pendingTransferEnvironment(context.value) } catch (e: any) { error.value = e.message; pending.value = { operationId: '', registrationId: null } } } } }
}
async function load(check: () => void) {
  const v = transferResponse(await api.context()); check(); transferContextKey(v); context.value = v
  enabled.value = v.registration ? v.registration.original.enabled ? 'enabled' : 'disabled' : ''; pending.value = pendingTransferEnvironment(v)
}
async function reload() { await run(async check => { clear(); if (!permission.loaded) await permission.loadMyActions(); check(); await load(check) }) }
async function save() {
  await run(async check => {
    if (!context.value || !canEdit.value || !['enabled', 'disabled'].includes(enabled.value) || !reason.value.trim()) throw new Error('请选择注册状态、填写操作原因并确认权限。')
    await saveTransferEnvironment(context.value, enabled.value === 'enabled', reason.value, check); check(); reason.value = ''; history.value = null
    await load(check); notice.value = '注册已保存。'
  })
}
async function recover(close: boolean) {
  await run(async check => {
    if (!context.value) return
    const v = await resolveTransferEnvironment(context.value, close, check); check()
    if (v.state !== 'UNKNOWN') { reason.value = ''; history.value = null; await load(check) }
    notice.value = v.state === 'COMMITTED' ? '已找回原操作。' : v.state === 'CLOSED' ? '未提交原操作已关闭。' : '结果仍未知，请继续查询原操作。'
  })
}
async function readHistory() { await run(async check => { history.value = null; const v = transferResponse(await api.history(historyVersion.value)); check(); history.value = v }) }
function reset() { generation++; busy.value = false; clear(); error.value = ''; notice.value = ''; if (authenticationCanRead()) void reload() }
onMounted(() => { window.addEventListener(AUTH_EVENT, reset); void reload() })
onBeforeUnmount(() => { live = false; generation++; clear(); window.removeEventListener(AUTH_EVENT, reset) })
</script>
<style scoped>
.transfer-environment{max-width:880px;margin:auto;padding:24px}.transfer-environment label{display:flex;flex-direction:column;gap:6px;margin:12px 0}.transfer-environment fieldset,.transfer-environment section{margin:16px 0;padding:16px;border:1px solid #bbc8d2;border-radius:8px}.transfer-environment button{padding:8px 14px;margin:6px}.transfer-environment select,.transfer-environment textarea,.transfer-environment input{padding:8px}.transfer-environment dd{margin:4px 0 14px;overflow-wrap:anywhere}.transfer-environment pre{white-space:pre-wrap;overflow-wrap:anywhere}[role=alert]{color:#a32626}
</style>
