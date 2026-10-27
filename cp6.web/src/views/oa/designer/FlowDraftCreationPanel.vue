<template>
  <section class="draft-creation" aria-label="创建下一草稿">
    <p>当前显示已发布原版。选择一个已发布版本，明确创建下一草稿后再编辑。</p>
    <p v-if="error" role="alert">{{ error }}</p>
    <p v-if="context?.draftVersionId">已有草稿，请重新加载流程。</p>
    <template v-else-if="context">
      <select v-model="source" aria-label="草稿来源版本" :disabled="busy || !!pending">
        <option v-for="item in context.sources" :key="item.versionId" :value="item.versionId">V{{ item.version }} · {{ item.name }}</option>
      </select>
      <button v-permission="'oa-designer:edit'" :disabled="busy || !source" @click="create">{{ pending ? '以原意图重试创建' : '从所选版本创建草稿' }}</button>
    </template>
    <button v-if="pending" :disabled="busy" @click="recover">查询原创建结果</button>
    <button v-else :disabled="busy" @click="reload">重新读取</button>
    <button v-if="context?.draftVersionId" :disabled="busy" @click="emit('created')">加载已有草稿</button>
  </section>
</template>
<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { flowDraftApi, type DraftCommand, type DraftContext, type DraftCreated } from '@/api/oa/flowDraft'
const props = defineProps<{ flowKey: string }>()
const emit = defineEmits<{ created: []; denied: [] }>()
const context = ref<DraftContext>(), source = ref(''), pending = ref<DraftCommand>(), busy = ref(false), error = ref('')
let generation = 0, live = true
function clear() { generation++; context.value = undefined; source.value = ''; pending.value = undefined; error.value = ''; busy.value = false }
async function run(action: (key: string, check: () => void) => Promise<void>) {
  if (busy.value) return
  const key = props.flowKey, epoch = identityStamp(), current = generation
  const check = () => { requireIdentity(epoch); if (!live || generation !== current || props.flowKey !== key) throw new Error('STALE') }
  busy.value = true; error.value = ''
  try { await action(key, check); check() } catch (e: any) {
    try {
      check()
      if ([401, 403].includes(e?.response?.status)) { clear(); emit('denied'); error.value = '权限已变化，请重新读取。' }
      else if ([409, 412, 422, 400].includes(e?.response?.status)) { pending.value = undefined; context.value = undefined; error.value = '来源或流程版本已变化，请重新读取后明确选择。' }
      else error.value = '创建结果尚未确认。请查询原创建结果，或以原意图重试。'
    } catch { /* Obsolete identity/object responses never populate the current view. */ }
  } finally { if (live && current === generation) busy.value = false }
}
function accept(result: DraftCreated) {
  const p = pending.value, c = context.value, original = c?.sources.find(x => x.versionId === p?.sourceVersionId)
  if (!p || !c || !original || result.owner !== 'Main.Workflow.Draft.v1' || result.operationId !== p.operationId
    || result.sourceVersionId !== p.sourceVersionId || result.sourceDigest !== original.schemaDigest || result.draft.definitionId !== c.definitionId)
    throw new Error('原结果与当前创建意图不匹配')
  pending.value = undefined; emit('created')
}
function reload() {
  if (pending.value) return
  return run(async (key, check) => { const value = await flowDraftApi.context(key); check(); context.value = value; source.value = value.sources[0]?.versionId ?? '' })
}
function create() {
  return run(async (key, check) => {
    if (!context.value || !source.value || context.value.draftVersionId) return
    pending.value ??= { operationId: crypto.randomUUID(), sourceVersionId: source.value, expectedHeadVersion: context.value.headVersion }
    const result = await flowDraftApi.create(key, { ...pending.value }); check(); accept(result)
  })
}
function recover() {
  return run(async (key, check) => {
    if (!pending.value) return
    const value = await flowDraftApi.operation(key, pending.value.operationId); check()
    if (value.state === 'SUCCEEDED' && value.result) accept(value.result)
    else error.value = '尚未观察到原结果；这不能证明未创建。可继续查询或以原意图重试。'
  })
}
function identityChanged() { clear(); emit('denied') }
watch(() => props.flowKey, () => { clear(); void reload() }, { immediate: true })
window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('storage', identityChanged)
onBeforeUnmount(() => { live = false; generation++; window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', identityChanged) })
</script>
<style scoped>
.draft-creation { padding: 10px 16px; background: #fff8e8; border-bottom: 1px solid #e5d4ac; }
p { margin: 0 0 6px; } button, select { margin-right: 8px; padding: 5px 9px; }
</style>
