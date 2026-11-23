<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import type { AuthoringView } from '@/api/oa/flowAuthoring'
import { flowSimulationApi as api, type FlowSimulation } from '@/api/oa/flowSimulation'

const props = defineProps<{ current?: AuthoringView; dirty: boolean; disabled: boolean }>()
const emit = defineEmits<{ privateClear: [] }>()
const opened = ref(false), varsJson = ref('{}'), busy = ref(false), error = ref(''), result = ref<FlowSimulation>()
const available = computed(() => !!props.current && !props.current.published && !props.dirty && !props.disabled
  && Object.hasOwn(props.current.fields, 'SchemaJson') && Object.hasOwn(props.current.fields, 'FormKey'))
let generation = 0, live = true, controller: AbortController | undefined
let observedIdentity = identityStamp()
function invalidate() { generation++; controller?.abort(); busy.value = false; result.value = undefined; error.value = '' }
function clear() { invalidate(); varsJson.value = '{}'; opened.value = false }
watch(() => [props.current?.definitionId, props.current?.draftId, props.current?.version, props.current?.published], clear)
watch(() => [props.dirty, props.disabled, available.value], invalidate)
watch(varsJson, invalidate)
function open() { if (available.value && !busy.value) opened.value = true }
async function simulate() {
  if (!available.value || busy.value) return
  const current = props.current!, identity = identityStamp(), request = generation
  controller = new AbortController(); busy.value = true; result.value = undefined; error.value = ''
  const check = () => { requireIdentity(identity); if (!live || generation !== request) throw new Error('STALE') }
  try {
    // Send the original JSON text: JavaScript numbers must not round business input.
    const response = await api.simulate(current.key, { draftVersionId: current.draftId, expectedVersion: current.version, varsJson: varsJson.value }, controller.signal)
    check(); result.value = response
  } catch (e: any) {
    try {
      check(); const status = e?.response?.status
      if (status === 401 || status === 403) { clear(); emit('privateClear'); error.value = '读取权限已变化，请重新读取草稿。' }
      else if (status === 409 || status === 404) error.value = '草稿版本已变化或不可读取，请重新读取后预演。'
      else if (status === 400) error.value = '模拟变量须为合法 JSON 对象；请检查重复字段、格式及大小。'
      else error.value = '预演未完成，请稍后重试。'
    } catch { /* The response belongs to an obsolete identity or draft. */ }
  } finally { if (live && generation === request) busy.value = false }
}
function identityChanged() {
  const currentIdentity = identityStamp()
  if (currentIdentity === observedIdentity) return
  observedIdentity = currentIdentity; clear()
}
window.addEventListener(AUTH_EVENT, identityChanged)
window.addEventListener('storage', identityChanged)
onBeforeUnmount(() => { live = false; invalidate(); window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', identityChanged) })
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
    <span v-if="dirty">请先保存当前修改。</span><span v-else-if="current?.published">请选择未发布的准确草稿。</span>
    <div v-if="opened">
      <p>仅说明预计路径，不产生审批记录或业务提交。以当前登录人为模拟发起人，假定审批全部同意且变量不再改变。</p>
      <label>非敏感模拟变量（JSON）<textarea v-model="varsJson" rows="5" spellcheck="false" :disabled="busy" /></label>
      <button :disabled="!available || busy" @click="simulate">{{ busy ? '正在预演…' : '运行预演' }}</button>
      <p v-if="error" role="alert">{{ error }}</p>
      <div v-if="result" class="result" aria-live="polite">
        <h3>仅解释：预计经过的节点</h3>
        <p>下面是按上述假设计算的路径；人员仅反映当前解析结果，实际提交时仍须重新校验。</p>
        <ol><li v-for="(node, index) in result.nodes" :key="index"><strong>{{ node.nodeId }}</strong> · {{ node.type }}
          <p v-for="stage in node.stages" :key="stage.stageIndex">第 {{ stage.stageIndex + 1 }} 档：{{ stage.state === 'RESOLVED_NOW' ? '当前候选' : '未解析' }} {{ stage.approverIds.join('、') }}</p>
        </li></ol>
        <h4 v-if="result.diagnostics.length">尚未证实的部分</h4>
        <ul><li v-for="(item, index) in result.diagnostics" :key="index">{{ item.path }}：{{ explanations[item.code] ?? item.code }}</li></ul>
      </div>
    </div>
  </section>
</template>

<style scoped>
.simulation { margin: 12px 0; padding: 12px; border: 1px solid #ccd6e3; border-radius: 6px; }
label { display: grid; gap: 6px; margin: 12px 0; } textarea { width: 100%; max-width: 760px; font-family: monospace; }
button { margin-right: 10px; } .result { margin-top: 14px; } [role=alert] { color: #a63727; }
</style>
