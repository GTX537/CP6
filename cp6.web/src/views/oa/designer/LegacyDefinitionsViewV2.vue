<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { legacyDefinitionsApi as api, type LegacyDefinition } from '@/api/oa/legacyDefinitions'

const props = defineProps<{ kind: 'FLOW' | 'FORM' }>()
const emit = defineEmits<{ close: [] }>()
const rows = ref<LegacyDefinition[]>([]), key = ref<string>(), page = ref(1), busy = ref(false), error = ref('')
let generation = 0, live = true, controller: AbortController | undefined
function clear() { generation++; controller?.abort(); rows.value = []; key.value = undefined; page.value = 1; busy.value = false; error.value = '' }
async function load(nextKey?: string, nextPage = 1) {
  controller?.abort(); controller = new AbortController()
  const request = ++generation, epoch = identityStamp(), kind = props.kind
  rows.value = []; key.value = nextKey; page.value = nextPage; busy.value = true; error.value = ''
  const check = () => { requireIdentity(epoch); if (!live || request !== generation || props.kind !== kind) throw new Error('STALE') }
  try { const value = await api.read(kind, nextKey, nextPage, controller.signal); check(); rows.value = value }
  catch (e: any) {
    try {
      check(); rows.value = []
      error.value = e?.response?.data?.code === 'LEGACY_OWNER_SCOPE_REQUIRED'
        ? '旧记录没有可信的归属信息，需要当前定义的全部数据范围权限才能查看。'
        : [401, 403].includes(e?.response?.status) ? '当前权限不允许查看，页面内容已清除。' : '未能读取历史来源，请重新读取。'
    } catch { /* A response for an obsolete identity or selection must not repopulate this view. */ }
  } finally { if (live && request === generation) busy.value = false }
}
function identityChanged() { clear(); error.value = '身份已变化，请重新读取。' }
watch(() => props.kind, () => { clear(); void load() }, { immediate: true })
window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('storage', identityChanged)
onBeforeUnmount(() => { live = false; controller?.abort(); window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', identityChanged) })
</script>
<template>
  <section class="legacy-definitions">
    <header><h2>历史来源 · {{ kind }}</h2><button @click="emit('close')">返回</button></header>
    <p>历史来源未核实（UNVERIFIED_LEGACY）。这里展示旧记录和当前定义重建内容，不能作为已核实的历史发布凭据；不会创建草稿或修改既有数据。</p>
    <p v-if="error" role="alert">{{ error }}</p>
    <nav><button :disabled="busy" @click="load()">定义列表</button><button :disabled="busy" @click="load(key, page)">重新读取</button></nav>
    <article v-for="item in rows" :key="item.versionId ?? item.definitionId">
      <h3><button v-if="!key" :disabled="busy" @click="load(item.key)">{{ item.key }} · {{ item.fields.Name ?? '名称不可见' }}</button><span v-else>{{ item.key }} · {{ item.fields.Name ?? '名称不可见' }}</span></h3>
      <p>{{ item.source === 'CURRENT_HEAD_RECONSTRUCTION' ? '来源：当前定义重建；没有准确历史版本绑定' : item.source === 'LEGACY_HEAD_BACKFILL' ? '来源：旧定义回填；回填时间不能当作历史发布时间' : '来源：旧版本快照；历史发布凭据未核实' }}</p>
      <p>旧记录版本号：{{ item.recordedVersion }}<span v-if="item.versionId"> · 版本 ID：{{ item.versionId }}</span></p>
      <p v-if="item.versionId">旧记录状态：{{ item.recordedStatus }}；记录的发布人：{{ item.recordedPublishedBy ?? '缺失' }}；记录的发布时间：{{ item.recordedPublishedAtUtc ?? '缺失' }}。这些值未被认定为已核实事实。</p>
      <pre v-if="Object.hasOwn(item.fields, 'SchemaJson')">{{ item.fields.SchemaJson }}</pre>
    </article>
    <p v-if="!busy && !error && !rows.length">没有可读的旧记录。</p>
    <nav><button :disabled="busy || page === 1" @click="load(key, page - 1)">上一页</button><span>第 {{ page }} 页</span><button :disabled="busy || rows.length < 100" @click="load(key, page + 1)">下一页</button></nav>
  </section>
</template>
<style scoped>
.legacy-definitions { padding: 24px; } header, nav { display: flex; gap: 12px; align-items: center; } header { justify-content: space-between; } article { border: 1px solid #d6dce6; padding: 16px; margin: 14px 0; } button { padding: 8px 12px; } pre { white-space: pre-wrap; overflow-wrap: anywhere; max-height: 500px; overflow: auto; } [role=alert] { color: #a63727; }
</style>
