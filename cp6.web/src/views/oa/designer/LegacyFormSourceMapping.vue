<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { ElMessageBox } from 'element-plus'
import { AUTH_EVENT, identityStamp } from '@/auth/identity'
import { formPublicationApi, type PublishedForm } from '@/api/oa/formPublication'
import { legacyDraftAdoptionApi as api, type LegacySourcePreview, type LegacySourceBinding, type LegacySourceOperation } from '@/api/oa/legacyDraftAdoption'
import { personalFormError } from '../catalog/personalFormError'
const props = defineProps<{ legacyVersionId: string }>()
const key = ref(''), versions = ref<PublishedForm[]>([]), version = ref(''), page = ref(1), preview = ref<LegacySourcePreview>(), saved = ref<LegacySourceBinding>(), reason = ref(''), enabled = ref(true)
const busy = ref(false), pending = ref(''), error = ref(''), notice = ref(''), storageError = ref('')
let live = true, generation = 0, stamp = identityStamp(), resolved = ''
const uuid = (value: string) => /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value) && value !== '00000000-0000-0000-0000-000000000000'
const cache = () => 'cp6-legacy-form-source-v1:' + stamp
const current = (n: number, identity: string) => live && generation === n && identity === stamp && identity === identityStamp()
function stored() { const id = localStorage.getItem(cache()) || ''; if (id && !uuid(id)) throw new Error('原操作标识损坏，请保留记录并修复存储。'); return id }
async function run(body: (n: number, identity: string) => Promise<void>) {
  if (busy.value) return; const n = generation, identity = stamp; busy.value = true; error.value = ''; notice.value = ''
  try { await body(n, identity) } catch (e) { if (current(n, identity)) error.value = personalFormError(e) }
  finally { if (current(n, identity)) busy.value = false }
}
function accept(value: LegacySourceOperation | null, id: string) {
  if (!value) { pending.value = id; notice.value = '尚未查到原结果；不代表没有效果，请查询或关闭原操作。'; return }
  pending.value = ''; resolved = id; preview.value = undefined; saved.value = value.result || undefined
  notice.value = value.state === 'CLOSED' ? '原操作已关闭，迟到配置不会生效。' : '当前来源映射已保存；原历史身份仍未核实。'
}
async function recover(close = false) {
  const id = pending.value; if (!id) return
  await run(async (n, identity) => {
    if (close) { try { await ElMessageBox.confirm('已保存则读取原回执，否则关闭并阻止迟到配置；不会自动重新授权。', '核对原操作') } catch { return } }
    if (!current(n, identity)) return; const value = close ? await api.close(id) : await api.operation(id); if (current(n, identity)) accept(value, id)
  })
}
async function loadVersions(next = 1) {
  if (pending.value || storageError.value) return
  await run(async (n, identity) => { preview.value = undefined; versions.value = []; version.value = ''; const target = key.value; const value = await formPublicationApi.versions(target, next); if (current(n, identity) && target === key.value) { versions.value = value; page.value = next; version.value = value[0]?.versionId || '' } })
}
async function readPreview() {
  if (pending.value || storageError.value || !uuid(version.value)) return
  await run(async (n, identity) => { preview.value = undefined; const source = props.legacyVersionId, target = key.value, pin = version.value; const value = await api.sourcePreview(source, target, pin); if (current(n, identity) && props.legacyVersionId === source && key.value === target && version.value === pin) { preview.value = value; enabled.value = value.current?.enabled ?? true } })
}
async function configure() {
  const p = preview.value, note = reason.value.trim(); if (!p?.canConfigure || busy.value || pending.value || storageError.value || !note || enabled.value && !p.compatible || p.legacyVersionId !== props.legacyVersionId || p.targetKey !== key.value || p.targetVersionId !== version.value) return
  const input = { legacyVersionId: p.legacyVersionId, expectedSourceRevision: p.sourceRevision, targetKey: p.targetKey, targetVersionId: p.targetVersionId, expectedVersion: p.current?.version || 'absent', enabled: enabled.value, reason: note }
  await run(async (n, identity) => {
    try { await ElMessageBox.confirm(`${input.enabled ? '启用' : '撤销'}旧版本 ${input.legacyVersionId} 到 ${input.targetKey} v${p.targetVersion} 的当前读取/复制映射；不确认历史发布，不自动复制草稿。`, '确认当前来源映射') } catch { return }
    if (!current(n, identity)) return
    if (!navigator.locks) throw new Error('当前浏览器不能协调原操作，请更换受支持的浏览器。')
    await navigator.locks.request(cache(), async () => {
      if (!current(n, identity)) return; const latest = stored()
      if (latest && latest !== resolved) { pending.value = latest; const value = await api.operation(latest); if (current(n, identity)) accept(value, latest); return }
      const id = crypto.randomUUID(); try { localStorage.setItem(cache(), id) } catch { storageError.value = '无法保存原操作标识，尚未发送配置。'; return }
      pending.value = id; const value = await api.configure({ operationId: id, ...input }); if (current(n, identity)) accept(value, id)
    })
  })
}
function clear() { generation++; versions.value = []; version.value = ''; preview.value = undefined; saved.value = undefined; reason.value = ''; busy.value = false; error.value = ''; notice.value = '' }
function identityChanged() { if (stamp === identityStamp()) return; clear(); stamp = identityStamp(); key.value = ''; pending.value = ''; resolved = ''; storageError.value = '' }
watch(() => props.legacyVersionId, clear)
watch([key, version], () => { preview.value = undefined })
window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('storage', identityChanged)
onMounted(async () => { try { pending.value = stored(); if (pending.value) await recover() } catch (e) { storageError.value = (e as Error).message } })
onBeforeUnmount(() => { live = false; generation++; window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', identityChanged) })
</script>
<template>
  <section class="legacy-source-mapping" aria-label="旧FORM当前来源映射">
    <h3>设置当前内容映射</h3><p>此操作只建立现在的读取与复制依据，不确认原历史发布人或发布时间，也不会自动采用旧草稿。</p>
    <p>准确旧版本：{{ legacyVersionId }}</p>
    <el-alert v-if="error || storageError" :title="error || storageError" type="error" :closable="false" /><el-alert v-if="notice" :title="notice" type="info" :closable="false" />
    <div v-if="pending"><p>当前映射原操作待确认：{{ pending }}</p><el-button :disabled="busy" @click="recover()">查询映射原结果</el-button><el-button :disabled="busy" @click="recover(true)">关闭映射原操作</el-button></div>
    <el-input v-model="key" aria-label="目标原生表单 Key" :disabled="busy || !!pending" placeholder="填写已发布原生表单 Key" />
    <el-button :disabled="busy || !!pending || !key" @click="loadVersions()">读取已核实的发布版本</el-button>
    <template v-if="versions.length">
      <select v-model="version" aria-label="目标原生发布版本" :disabled="busy || !!pending"><option v-for="item in versions" :key="item.versionId" :value="item.versionId">v{{ item.version }} · {{ item.versionId }}</option></select>
      <el-button :disabled="busy || !!pending || page === 1" @click="loadVersions(page - 1)">上一页发布</el-button><el-button :disabled="busy || !!pending || versions.length < 100" @click="loadVersions(page + 1)">下一页发布</el-button>
      <el-button :disabled="busy || !!pending || !version" @click="readPreview">核对准确来源映射</el-button>
    </template>
    <div v-if="preview" class="mapping-preview">
      <p>旧记录 v{{ preview.recordedVersion }} · 历史未核实；目标 {{ preview.targetKey }} v{{ preview.targetVersion }}。</p>
      <p>表单结构准确一致：{{ preview.compatible ? '是' : '否，不能直接采用' }}；当前映射：{{ preview.current ? preview.current.enabled ? '启用' : '已撤销' : '尚未登记' }}。</p>
      <el-checkbox v-model="enabled" :disabled="busy || !!pending || !preview.canConfigure">启用当前读取与复制映射</el-checkbox>
      <el-input v-model="reason" aria-label="当前来源映射依据" maxlength="500" :disabled="busy || !!pending || !preview.canConfigure" placeholder="说明当前来源核对和授权依据" />
      <el-button :disabled="busy || !!pending || !preview.canConfigure || !reason.trim() || enabled && !preview.compatible" @click="configure">保存当前来源映射</el-button>
    </div>
    <div v-if="saved" class="mapping-saved"><p>已保存准确映射：{{ saved.legacyVersionId }} → {{ saved.targetKey }} / {{ saved.formVersionId }}，状态：{{ saved.enabled ? '启用' : '已撤销' }}。</p><p>当前授权记录时间：{{ saved.boundAtUtc }}；原历史仍未核实。</p></div>
  </section>
</template>
<style scoped>.legacy-source-mapping { margin-top: 20px; padding: 16px; border: 1px solid var(--el-border-color); } p { line-height: 1.6; overflow-wrap: anywhere; } select { min-height: 32px; max-width: 100%; } .el-input { margin: 8px 0; }</style>
