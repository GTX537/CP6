<template>
  <section class="tenant-privacy-export-panel">
    <h3>租户已分类来源受控导出</h3>
    <p>按准确租户读取已分类身份、安全事件和角色关系。确认用途、来源和内部收件人后生成不可变制品，再按当前权限下载；其他来源明确未接入。</p>
    <el-button :disabled="busy" @click="load">读取当前权限与原操作</el-button>
    <el-alert v-if="message" :title="message" type="info" :closable="false" class="notice" />
    <el-form v-if="context" label-width="130px" :disabled="busy || !!pending || !!storageError">
      <el-form-item label="目标租户 ID"><el-input v-model="draft.targetTenantId" aria-label="目标租户 ID" /></el-form-item>
      <el-form-item label="申请引用"><el-input v-model="draft.requestRef" aria-label="申请引用" /></el-form-item>
      <el-form-item label="用途"><el-select v-model="draft.purpose" aria-label="导出用途"><el-option v-for="p in context.purposes" :key="p" :label="purposeName(p)" :value="p" /></el-select></el-form-item>
      <el-form-item label="来源范围"><el-checkbox-group v-model="draft.sources"><el-checkbox v-for="s in context.sources" :key="s" :value="s" :label="s" :disabled="!can('tenant-scope-' + s)">{{ sourceName(s) }}</el-checkbox></el-checkbox-group></el-form-item>
      <el-form-item label="收件用户 ID"><el-input v-model="draft.recipientId" aria-label="收件用户 ID" /><span>同申请租户内的启用平台用户；下载时须仍有对应权限。</span></el-form-item>
      <el-form-item label="可下载分钟数"><el-input-number v-model="draft.accessMinutes" :min="1" :max="context.maximumAccessMinutes" aria-label="可下载分钟数" /></el-form-item>
      <p>下载窗口最长 {{ context.maximumAccessMinutes }} 分钟，仅控制访问有效期，不代表法律保留期限。申请人和执行人为当前登录用户：{{ context.actorId }}。</p>
      <el-button type="primary" :disabled="!ready || !can('tenant-export')" @click="generate">确认范围并生成制品</el-button>
    </el-form>
    <div v-if="pending" class="notice">
      <p>原操作结果待确认：<code>{{ pending }}</code>。查询不到也不能认定请求未到达；关闭该操作后，迟到请求不会生成制品。</p>
      <el-button :disabled="busy || !!storageError" @click="recover(false)">查询原操作</el-button>
      <el-button :disabled="busy || !!storageError" @click="recover(true)">关闭未完成操作</el-button>
    </div>
    <div v-if="context" class="artifact-lookup">
      <el-input v-model="artifactRef" aria-label="制品 ID" placeholder="填写内部收件制品 ID" :disabled="busy" />
      <el-button :disabled="busy || !uuid(artifactRef)" @click="readArtifact">读取制品当前状态</el-button>
    </div>
    <div v-if="receipt" class="artifact-result">
      <h4>租户导出原回执 · 整体仍为部分范围</h4>
      <p>制品：{{ receipt.artifactId }} · 准确目标租户：{{ receipt.targetTenantId }}</p>
      <p>原申请人：{{ receipt.requesterId }} · 原执行人：{{ receipt.operatorId }} · 捕获于：{{ receipt.capturedAt }}</p>
      <p>内部收件人：{{ receipt.recipientId }} · 用途：{{ purposeName(receipt.purpose) }} · 可访问至：{{ receipt.expiresAt }}</p>
      <p>实际大小：{{ receipt.length }} 字节 · SHA-256：{{ receipt.sha256 }}</p>
      <p v-if="!artifact">当前访问状态尚未核实，请读取准确制品状态。已提交的原回执保留。</p>
      <div v-if="artifact">
        <p>当前访问：{{ availabilityName(artifact.availability) }}</p>
        <el-button :disabled="busy || artifact.availability !== 'AVAILABLE' || !can('tenant-download') || receipt.recipientId !== context?.actorId" @click="download">按当前权限下载租户制品</el-button>
        <el-button type="warning" :disabled="busy || artifact.availability === 'REVOKED' || !can('tenant-revoke') || receipt.requesterId !== context?.actorId" @click="revoke">撤销租户制品下载</el-button>
      </div>
      <SubjectExportManifestView :manifest="receipt.manifest" />
      <p>下载到期或撤销只关闭访问。租户制品的物理保留/处置、其他Owner及外部副本仍有独立剩余范围。</p>
    </div>
    <el-alert v-if="storageError" :title="storageError" type="error" :closable="false" />
  </section>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import { ElMessageBox } from 'element-plus'
import { AUTH_EVENT, identityStamp } from '@/auth/identity'
import { tenantPrivacyExportApi as api, saveTenantPrivacyDownload, type TenantPrivacyExportContext, type TenantPrivacyExportOperation, type TenantPrivacyExportArtifact, type TenantPrivacyExportReceipt } from '@/api/platform/tenantPrivacyExport'
import SubjectExportManifestView from './SubjectExportManifest.vue'
const context = ref<TenantPrivacyExportContext | null>(null), artifact = ref<TenantPrivacyExportArtifact | null>(null)
const originalReceipt = ref<TenantPrivacyExportReceipt | null>(null), receipt = computed(() => originalReceipt.value ?? artifact.value?.receipt ?? null)
const pending = ref(''), artifactRef = ref(''), busy = ref(false), message = ref(''), storageError = ref('')
const draft = reactive({ targetTenantId: '', requestRef: '', purpose: '', sources: [] as string[], recipientId: '', accessMinutes: 60 })
let stamp = identityStamp(), disposed = false, resolvedOperation = ''
const active = (start: string) => !disposed && start === stamp && start === identityStamp()
const uuid = (value: string) => /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value) && value !== '00000000-0000-0000-0000-000000000000'
const can = (action: string) => !!context.value?.actions.includes(action)
const ready = computed(() => !busy.value && !pending.value && !storageError.value && [draft.targetTenantId, draft.requestRef, draft.recipientId].every(uuid) && draft.sources.length > 0)
const sourceName = (value: string) => ({ identity: '身份资料', security: '安全事件', roles: '角色关联' }[value] || value)
const purposeName = (value: string) => ({ TENANT_ACCESS_REQUEST: '租户资料访问申请', TENANT_SECURITY_REVIEW: '租户安全审查' }[value] || value)
const availabilityName = (value: string) => ({ AVAILABLE: '可申请下载', EXPIRED: '访问已过期，载荷未因此删除', REVOKED: '访问已撤销，载荷未因此删除', POLICY_CHANGED: '政策已改变' }[value] || value)
function key() { const c = context.value!; return `cp6-tenant-privacy-export-v1:${c.environmentId}:${c.actorTenantId}:${c.actorId}` }
function stored() {
  const value = localStorage.getItem(key()) || ''
  if (value && !uuid(value)) throw new Error('原操作记录无效，请保留记录并修复浏览器存储后继续。')
  return value
}
function error(e: unknown) { message.value = (e as any)?.response?.data?.code || (e as Error)?.message || '操作未完成，请查询原结果。' }
async function apply(operation: TenantPrivacyExportOperation, start: string) {
  if (!active(start)) return
  if (operation.state === 'NOT_FOUND') { pending.value = operation.operationId; message.value = '尚未查到原结果，可继续查询或关闭该操作。'; return }
  pending.value = ''
  resolvedOperation = operation.operationId
  if (operation.state === 'CLOSED') { artifact.value = null; originalReceipt.value = null; message.value = '原操作已关闭，迟到请求不会生成制品。'; return }
  originalReceipt.value = operation.receipt!; artifact.value = null
  artifactRef.value = operation.receipt!.artifactId
  const value = await api.artifact(operation.receipt!.artifactId)
  if (active(start)) { artifact.value = value; message.value = '已读取原制品。下载前会重新核验当前权限。' }
}
async function load() {
  const start = stamp; busy.value = true; message.value = ''; storageError.value = ''
  try {
    const value = await api.context(); if (!active(start)) return; context.value = value
    draft.recipientId = value.actorId; draft.purpose = value.purposes[0] || ''; draft.sources = value.sources.filter(s => value.actions.includes('tenant-scope-' + s))
    if (!draft.requestRef) draft.requestRef = crypto.randomUUID()
    let original: string
    try { original = stored() } catch (e) { storageError.value = (e as Error).message; return }
    if (original) { pending.value = original; await apply(await api.operation(original), start) }
  } catch (e) { if (active(start)) error(e) } finally { if (active(start)) busy.value = false }
}
async function generate() {
  if (!ready.value || !context.value || !can('tenant-export')) return
  const start = stamp, c = context.value
  const command = { ...draft, sources: [...draft.sources], operationId: crypto.randomUUID(), policyVersion: c.policyVersion }
  busy.value = true; message.value = ''
  try {
    await ElMessageBox.confirm(`目标租户 ${command.targetTenantId}；用途 ${purposeName(command.purpose)}；来源 ${command.sources.map(sourceName).join('、')}；收件人 ${command.recipientId}；可下载 ${command.accessMinutes} 分钟。资料仍为部分范围。`, '确认导出范围', { type: 'warning' })
    if (!active(start)) return
    if (!navigator.locks) throw new Error('当前浏览器不支持安全协调，请更换受支持的浏览器。')
    await navigator.locks.request(key(), async () => {
      if (!active(start)) return
      const latest = stored()
      if (latest && latest !== resolvedOperation) { pending.value = latest; await apply(await api.operation(latest), start); return }
      try { localStorage.setItem(key(), command.operationId) } catch { storageError.value = '无法保存原操作号，尚未发送生成请求。'; return }
      pending.value = command.operationId; artifact.value = null; originalReceipt.value = null
      await apply(await api.generate(command), start)
    })
  } catch (e) { if (active(start) && e !== 'cancel' && e !== 'close') error(e) }
  finally { if (active(start)) busy.value = false }
}
async function recover(close: boolean) {
  if (!pending.value || busy.value) return
  const start = stamp, operation = pending.value; busy.value = true
  try {
    if (close) await ElMessageBox.confirm('关闭此原操作：如果已经生成，将返回既有回执；如果尚未到达，将阻止迟到生成。', '关闭原操作')
    if (active(start)) await apply(await (close ? api.close(operation) : api.operation(operation)), start)
  } catch (e) { if (active(start) && e !== 'cancel' && e !== 'close') error(e) }
  finally { if (active(start)) busy.value = false }
}
async function readArtifact() {
  const start = stamp, id = artifactRef.value, known = receipt.value; busy.value = true; artifact.value = null; originalReceipt.value = known?.artifactId === id ? known : null
  try { const value = await api.artifact(id); if (active(start)) artifact.value = value }
  catch (e) { if (active(start)) error(e) } finally { if (active(start)) busy.value = false }
}
async function download() {
  if (!artifact.value) return
  const start = stamp, id = artifact.value.receipt.artifactId; busy.value = true
  try { const blob = await api.download(id); if (active(start)) saveTenantPrivacyDownload(blob, id) }
  catch (e) { if (active(start)) error(e) } finally { if (active(start)) busy.value = false }
}
async function revoke() {
  if (!artifact.value) return
  const start = stamp, id = artifact.value.receipt.artifactId; busy.value = true
  try {
    await ElMessageBox.confirm('撤销后不能再下载此制品，已经取得的外部副本不会因此收回。', '撤销制品下载')
    if (!active(start)) return
    originalReceipt.value = artifact.value!.receipt; artifact.value = null
    const value = await api.revoke(id); if (active(start)) artifact.value = value
  } catch (e) { if (active(start) && e !== 'cancel' && e !== 'close') error(e) }
  finally { if (active(start)) busy.value = false }
}
function identityChanged() {
  const next = identityStamp(); if (next === stamp) return
  stamp = next; resolvedOperation = ''; context.value = null; artifact.value = null; originalReceipt.value = null; pending.value = ''; artifactRef.value = ''; busy.value = false; message.value = ''; storageError.value = ''
  Object.assign(draft, { targetTenantId: '', requestRef: '', purpose: '', sources: [], recipientId: '', accessMinutes: 60 })
}
window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('storage', identityChanged)
onMounted(load)
onBeforeUnmount(() => { disposed = true; window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', identityChanged) })
</script>
<style scoped>
.tenant-privacy-export-panel { max-width: 920px; }
.notice, .artifact-lookup, .artifact-result { margin-top: 18px; }
.artifact-lookup { display: flex; gap: 12px; }
p { line-height: 1.6; overflow-wrap: anywhere; }
</style>
