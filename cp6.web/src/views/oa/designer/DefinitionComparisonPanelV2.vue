<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { definitionComparisonApi as api, type DefinitionComparison } from '@/api/oa/definitionComparison'
const props = defineProps<{ kind: 'FLOW' | 'FORM'; current?: { key: string; version: string }; versions: { versionId: string; version: number }[]; disabled: boolean }>()
const emit = defineEmits<{ privateClear: [] }>()
const left = ref(''), right = ref(''), result = ref<DefinitionComparison>(), busy = ref(false), error = ref('')
let generation = 0, live = true, controller: AbortController | undefined, identity = identityStamp()
const unavailable = computed(() => busy.value || props.disabled || !props.current || !left.value || !right.value)
const categories: Record<string, string> = { METADATA: '定义信息', SCHEMA: '结构及扩展', ORDER: '顺序', FIELD: '字段', NODE: '节点', EDGE: '连线', TYPE: '类型', RULE: '规则', APPROVER: '审批人', ROUTING: '路径', DEPENDENCY: '固定依赖', UNKEYED_SEQUENCE: '无稳定标识的序列' }
const operations: Record<string, string> = { ADDED: '新增', REMOVED: '删除', CHANGED: '变更' }
function clear(selections = false) {
  generation++; controller?.abort(); busy.value = false; result.value = undefined; error.value = ''
  if (selections) { left.value = ''; right.value = '' }
}
watch(() => [props.kind, props.current], () => clear(true), { deep: true })
watch(() => [left.value, right.value, props.versions], () => clear())
watch(() => props.disabled, value => { if (value) clear() })
function identityChanged() { if (identityStamp() !== identity) { identity = identityStamp(); clear(true) } }
window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('storage', identityChanged)
async function compare(page = 1) {
  if (unavailable.value) return
  const request = ++generation, stamp = identityStamp(), current = props.current!, a = left.value, b = right.value
  controller?.abort(); controller = new AbortController(); busy.value = true; error.value = ''; result.value = undefined
  const check = () => { requireIdentity(stamp); if (!live || generation !== request) throw new Error('STALE') }
  try { const response = await api.compare(props.kind, current.key, a, b, page, controller.signal); check(); result.value = response }
  catch (e: any) {
    try {
      check()
      if (e?.response?.status === 401 || e?.response?.status === 403) { clear(true); emit('privateClear'); error.value = '当前权限无法读取比较内容，已清除结果。' }
      else error.value = '未能完成比较。请重新读取历史版本并确认所选版本，或稍后重试。'
    } catch { /* Obsolete replies cannot restore private content. */ }
  } finally { if (live && generation === request) busy.value = false }
}
onBeforeUnmount(() => { live = false; clear(true); window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', identityChanged) })
</script>
<template>
  <section aria-label="发布版本比较" class="comparison">
    <h4>发布版本比较</h4>
    <p>先读取历史版本，再明确选择左右两个版本。比较内容为发布时的快照。</p>
    <div class="selectors">
      <label>左侧版本<select v-model="left" aria-label="左侧版本" :disabled="busy || disabled">
        <option value="">请选择版本</option><option v-if="left && !versions.some(v => v.versionId === left)" :value="left">已选 {{ left }}</option>
        <option v-for="v in versions" :key="v.versionId" :value="v.versionId">版本 {{ v.version }} · {{ v.versionId }}</option>
      </select></label>
      <label>右侧版本<select v-model="right" aria-label="右侧版本" :disabled="busy || disabled">
        <option value="">请选择版本</option><option v-if="right && !versions.some(v => v.versionId === right)" :value="right">已选 {{ right }}</option>
        <option v-for="v in versions" :key="v.versionId" :value="v.versionId">版本 {{ v.version }} · {{ v.versionId }}</option>
      </select></label>
      <button :disabled="unavailable" @click="compare()">比较指定版本</button>
    </div>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="busy" role="status">正在读取指定版本…</p>
    <div v-if="result">
      <p v-if="result.scope === 'VISIBLE_FIELDS_ONLY'">仅比较当前可见字段；受限字段未参与比较，不能据此判断完整版本相同。</p>
      <p v-if="!result.dependenciesVisible">当前权限未披露固定依赖，依赖未参与比较。</p>
      <div class="versions"><div v-for="side in ['left', 'right'] as const" :key="side">
        <strong>{{ side === 'left' ? '左侧' : '右侧' }}：版本 {{ result[side].version }} · {{ result[side].nameSnapshot ?? '名称不可见' }}</strong>
        <p>{{ result[side].versionId }} · 发布于 {{ new Date(result[side].publishedAtUtc).toLocaleString() }}</p>
        <p>已发布 · 发布人 {{ result[side].publisherRef }} · {{ result[side].evidenceState === 'ORIGINAL_PUBLICATION' ? '原始发布记录' : result[side].evidenceState }}</p>
        <p v-if="result[side].schemaDigest">结构摘要 {{ result[side].schemaDigest }}</p>
        <ul v-if="result.dependenciesVisible"><li v-for="dependency in result[side].dependencyRefs" :key="dependency.kind + dependency.location">固定依赖：{{ dependency.kind }} · {{ dependency.reference }} · {{ dependency.location }} · {{ dependency.version }}</li></ul>
      </div></div>
      <p role="status">{{ result.totalChanges ? `当前可见范围共 ${result.totalChanges} 项变化。` : '当前可见范围没有差异。' }}</p>
      <ol><li v-for="(change, index) in result.changes" :key="change.path + index">
        <strong>{{ categories[change.category] ?? change.category }} · {{ operations[change.change] ?? change.change }}</strong><code>{{ change.path }}</code>
        <div class="versions"><div><span>左侧</span><pre>{{ change.beforeJson ?? '不存在' }}</pre></div><div><span>右侧</span><pre>{{ change.afterJson ?? '不存在' }}</pre></div></div>
      </li></ol>
      <div v-if="result.hasMore || result.page > 1"><button :disabled="unavailable || result.page === 1" @click="compare(result.page - 1)">上一页差异</button><span>第 {{ result.page }} 页</span><button :disabled="unavailable || !result.hasMore" @click="compare(result.page + 1)">下一页差异</button></div>
    </div>
  </section>
</template>
<style scoped>
.comparison { margin-top: 16px; border-top: 1px solid #dbe3ed; padding-top: 12px; } .selectors { display: flex; flex-wrap: wrap; gap: 12px; align-items: end; } label { display: grid; gap: 6px; max-width: 100%; } select { max-width: 100%; padding: 8px; } button { padding: 8px 12px; cursor: pointer; } button:disabled { cursor: default; opacity: .5; } .versions { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px; } p, code { overflow-wrap: anywhere; } code { display: block; margin: 6px 0; } pre { white-space: pre-wrap; overflow-wrap: anywhere; max-height: 320px; overflow: auto; background: #f5f7fa; padding: 8px; } li { margin-bottom: 18px; } [role=alert] { color: #a63727; } @media(max-width: 700px) { .versions { grid-template-columns: 1fr; } }
</style>
