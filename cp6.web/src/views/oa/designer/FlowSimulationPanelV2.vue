<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import type { AuthoringView } from '@/api/oa/flowAuthoring'
import { flowSimulationApi as api, type FlowForecast, type ForecastInput, type ForecastPin } from '@/api/oa/flowSimulation'

const props = defineProps<{ current?: AuthoringView; dirty: boolean; disabled: boolean }>()
const emit = defineEmits<{ privateClear: [] }>()
const opened = ref(false), varsJson = ref('{}'), busy = ref(false), error = ref(''), result = ref<FlowForecast>()
const source = ref<'draft' | 'published'>('draft'), publishedVersionId = ref('')
const versions = ref<Record<string, string>>({})
const uuid = /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i
const available = computed(() => !!props.current && !props.dirty && !props.disabled
  && Object.hasOwn(props.current.fields, 'SchemaJson') && Object.hasOwn(props.current.fields, 'FormKey'))
const pins = computed<Omit<ForecastPin, 'versionId'>[]>(() => {
  const fields = props.current?.fields
  const result: Omit<ForecastPin, 'versionId'>[] = fields?.FormKey ? [{ kind: 'FORM', location: 'formKey' }] : []
  try {
    const nodes = JSON.parse(fields?.SchemaJson ?? '{}').nodes
    if (Array.isArray(nodes)) {
      for (const node of nodes)
        if (typeof node?.id === 'string' && typeof node.type === 'string' && node.type.toLowerCase() === 'subflow')
          result.push({ kind: 'FLOW', location: node.id })
    }
  } catch { /* 保存后的图由服务端诊断。 */ }
  return result
})
const pinKey = (pin: Omit<ForecastPin, 'versionId'>) => pin.kind + ':' + pin.location
const ready = computed(() => available.value && (source.value === 'published' ? uuid.test(publishedVersionId.value)
  : !props.current?.published && Object.values(versions.value).every(id => !id || uuid.test(id))))
let generation = 0, live = true, controller: AbortController | undefined
let observedIdentity = identityStamp()

function invalidate() {
  generation++
  controller?.abort()
  busy.value = false
  result.value = undefined
  error.value = ''
}
function clear() {
  varsJson.value = '{}'
  versions.value = {}
  publishedVersionId.value = ''
  opened.value = false
  invalidate()
}
watch(() => [props.current?.definitionId, props.current?.draftId, props.current?.version, props.current?.published], () => {
  clear()
  source.value = props.current?.published ? 'published' : 'draft'
  if (props.current?.published) publishedVersionId.value = props.current.draftId
}, { immediate: true })
watch(() => [props.dirty, props.disabled, available.value, source.value, publishedVersionId.value, varsJson.value], invalidate, { flush: 'sync' })
watch(versions, invalidate, { deep: true, flush: 'sync' })

function open() { if (available.value && !busy.value) opened.value = true }
function downloadForecast(value: FlowForecast) {
  const url = URL.createObjectURL(new Blob([JSON.stringify(value, null, 2)], { type: 'application/json' }))
  const link = document.createElement('a')
  link.href = url
  link.download = 'workflow-forecast.json'
  link.click()
  setTimeout(() => URL.revokeObjectURL(url), 0)
}

async function simulate(download = false) {
  if (!ready.value || busy.value) return
  const current = props.current!, identity = identityStamp(), request = generation
  controller = new AbortController()
  busy.value = true
  result.value = undefined
  error.value = ''
  const input: ForecastInput = source.value === 'published'
    ? { publishedVersionId: publishedVersionId.value.toLowerCase(), varsJson: varsJson.value }
    : { draftVersionId: current.draftId, expectedVersion: current.version, varsJson: varsJson.value,
        draftDependencies: pins.value.flatMap(pin => versions.value[pinKey(pin)] ? [{ ...pin, versionId: versions.value[pinKey(pin)]! }] : []) }
  const check = () => { requireIdentity(identity); if (!live || generation !== request) throw new Error('STALE') }
  try {
    // 保留原始小数文本供精确条件计算。
    const response = await api.forecast(current.key, input, controller.signal, download)
    check()
    if (response.mode !== 'EXPLANATION_ONLY' || response.isExecutionPermit !== false ||
        response.definitionId !== current.definitionId ||
        ('publishedVersionId' in input
          ? response.source !== 'PUBLISHED_PIN' || response.versionId !== input.publishedVersionId
          : response.source !== 'DRAFT_SIMULATION' || response.versionId !== input.draftVersionId || response.draftRevision !== input.expectedVersion))
      throw new Error('FORECAST_SOURCE_MISMATCH')
    result.value = response
    if (download) downloadForecast(response)
  } catch (e: any) {
    try {
      check()
      const status = e?.response?.status
      if (status === 401 || status === 403 || status === 404) {
        clear()
        emit('privateClear')
        error.value = '当前版本不可读取，已清除预演内容。'
      } else if (status === 409 || status === 412) error.value = '版本已变化，请重新读取后预演。'
      else if (status === 400) error.value = '请核对固定版本及模拟变量，变量须为无重复字段的 JSON 对象。'
      else error.value = '预演未完成，请稍后重试。'
    } catch { /* 丢弃旧身份或旧选择的回包。 */ }
  } finally { if (live && generation === request) busy.value = false }
}
function authorityChanged() { observedIdentity = identityStamp(); clear() }
function storageChanged() { if (identityStamp() !== observedIdentity) authorityChanged() }
window.addEventListener(AUTH_EVENT, authorityChanged)
window.addEventListener('storage', storageChanged)
onBeforeUnmount(() => {
  live = false
  invalidate()
  window.removeEventListener(AUTH_EVENT, authorityChanged)
  window.removeEventListener('storage', storageChanged)
})
const peopleLabels = {
  RESOLVED_AT_CUT: '当前候选', ARRIVAL_TIME: '到达时解析', REDACTED: '当前未获人员读取权限',
}
const explanations: Record<string, string> = {
  APPROVER_AUTHORITY_UNPROVEN: '当前无权查询所需人员或组织资料，未披露候选人。',
  APPROVER_UNRESOLVED: '当前规则未解析到有效审批人。',
  APPROVER_EXPRESSION_UNPROVEN: '模拟变量不足或人员表达式无法求值。',
  EDGE_EXPRESSION_UNPROVEN: '此路径条件无法用当前模拟变量求值；未继续推断。',
  DEPENDENT_EXECUTION_NOT_SIMULATED: '已到达服务任务或子流程，未执行依赖，后续路径尚未预测。',
  JOIN_SYNCHRONIZATION_UNPROVEN: '已到达并行汇聚，任务同步结果尚未预测。',
  REPEATED_NODE_UNPROVEN: '路径再次到达同一节点，循环后续尚未预测。',
  NO_MATCHING_SUCCESS_EDGE: '当前模拟变量没有匹配的后续成功路径。',
  SIMULATION_BUDGET_EXCEEDED: '本次路径预测达到数量上限，后续尚未预测。',
  APPROVER_EXPANSION_LIMIT: '人员规则展开超过本次预演上限。',
  APPROVER_RESULT_LIMIT: '候选人员超过本次预演显示上限。',
  START_UNPROVEN: '草稿没有可预测的起始节点。',
  DEPENDENCY_NOT_AVAILABLE: '所需依赖当前不可取得。',
  DEPENDENCY_NOT_READABLE: '当前无权读取所需依赖。',
  SERVICE_REFERENCE_INVALID: '所需服务当前未注册。',
}
</script>

<template>
  <section class="simulation" aria-label="流程预演">
    <button :disabled="!available || busy" @click="open">预演流程</button>
    <span v-if="dirty">请先保存当前修改。</span>
    <p v-if="error" role="alert">{{ error }}</p>
    <div v-if="opened">
      <p>仅说明预计路径。以当前登录人为模拟发起人，假定审批同意、模拟变量不变。</p>
      <label>预演来源
        <select v-model="source" :disabled="busy">
          <option value="draft" :disabled="current?.published">已保存草稿</option>
          <option value="published">准确发布版本</option>
        </select>
      </label>
      <label v-if="source === 'published'">发布版本 ID
        <input v-model.trim="publishedVersionId" :disabled="busy" spellcheck="false" maxlength="36" />
      </label>
      <template v-else>
        <p>草稿修订 {{ current?.version }}</p>
        <label v-for="pin in pins" :key="pinKey(pin)">{{ pin.kind === 'FORM' ? '表单版本 ID' : '子流程版本 ID · ' + pin.location }}
          <input v-model.trim="versions[pinKey(pin)]" :disabled="busy" spellcheck="false" maxlength="36" />
        </label>
      </template>
      <label>非敏感模拟变量（JSON）<textarea v-model="varsJson" rows="5" spellcheck="false" :disabled="busy" /></label>
      <button :disabled="!ready || busy" @click="simulate()">{{ busy ? '正在预演…' : '运行预演' }}</button>
      <button v-if="result" :disabled="!ready || busy" @click="simulate(true)">重新核权并导出</button>
      <div v-if="result" class="result" aria-live="polite">
        <h3>仅解释：预计经过的节点</h3>
        <p>{{ result.source }} · {{ result.coverage }} · 截点 {{ result.knowledgeCutUtc }}</p>
        <p>节点 {{ result.visitedNodeCount }}/{{ result.totalNodeCount }}，路径 {{ result.traversedEdgeCount }}/{{ result.totalEdgeCount }}</p>
        <ol><li v-for="(node, index) in result.nodes" :key="index"><strong>{{ node.nodeId }}</strong> · {{ node.type }}
          <p v-for="stage in node.stages" :key="stage.stageIndex">第 {{ stage.stageIndex + 1 }} 档：{{ peopleLabels[stage.state] }}
            {{ stage.people.map(p => p.displayName).join('、') }}</p>
        </li></ol>
        <details v-if="result.edges.length"><summary>条件及路径</summary>
          <ul><li v-for="edge in result.edges" :key="edge.index">{{ edge.from }} → {{ edge.to }} · {{ edge.condition }} · {{ edge.traversed ? '已遍历' : '未遍历' }}</li></ul>
        </details>
        <details v-if="result.unvisited.length"><summary>未遍历位置</summary>
          <ul><li v-for="item in result.unvisited" :key="item.path">{{ item.path }} · {{ item.reason }}</li></ul>
        </details>
        <details v-if="result.dependencies.length"><summary>依赖诊断</summary>
          <ul><li v-for="item in result.dependencies" :key="item.kind + ':' + item.location">{{ item.kind }} · {{ item.location }} · {{ item.versionId }} · {{ item.evidenceState }}</li></ul>
        </details>
        <h4 v-if="result.diagnostics.length">尚未证实的部分</h4>
        <ul><li v-for="(item, index) in result.diagnostics" :key="index">{{ item.path }}：{{ explanations[item.code] ?? item.code }}</li></ul>
      </div>
    </div>
  </section>
</template>

<style scoped>
.simulation { margin: 12px 0; padding: 12px; border: 1px solid #ccd6e3; border-radius: 6px; }
label { display: grid; gap: 6px; margin: 12px 0; max-width: 760px; }
input, textarea { font-family: monospace; }
button { margin-right: 10px; } .result { margin-top: 14px; } [role=alert] { color: #a63727; }
</style>
