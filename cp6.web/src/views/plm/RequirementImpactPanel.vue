<template>
  <section aria-label="需求后继影响">
    <h3>需求后继影响</h3>
    <p>当前覆盖本系统工程迭代的实际需求集引用；外部系统和其他业务链不在此查询范围。</p>
    <p v-if="candidateVersion">已有批准前版时，后继需先发起影响请求，并完成当前迭代评估，才能送审和批准。当前没有消费者时，也要登记请求以覆盖以后才发生的旧版采用。</p>
    <p v-else>未处理或因计划变化而失效的影响评估，会阻止相关工作推进。请先核对当前案例；准备采用新版后，还需显式替换需求输入。</p>
    <label v-if="candidateVersion">影响候选版本<input v-model="selectedVersion" aria-label="影响候选版本" pattern="[1-9][0-9]*" :disabled="disabled" /></label>
    <button :disabled="disabled" @click="$emit('load', selectedVersion || undefined)">核对后继影响</button>
    <template v-if="source || iteration">
      <p v-if="source">准确需求集版本 {{ source.candidate.version }} · {{ source.request ? impactLabel(source.request.action) : '尚未发起请求' }}</p>
      <p v-if="!rows.length">当前覆盖范围没有{{ source ? '采用更早版本的迭代' : '影响案例' }}。</p>
      <article v-for="row in rows" :key="row.case?.id || row.iteration.id">
        <h4>{{ row.name }}</h4><p>实际采用版本 {{ row.adopted.version }} · 输入版本 {{ row.inputVersion }} · 计划版本 {{ row.planVersion }} · {{ impactLabel(row.state) }}</p>
        <p v-if="row.case">后继需求集版本 {{ row.case.candidate.version }} · 首次登记 {{ row.case.recordedAt }}</p>
        <p v-if="row.case?.assessment">评估人 {{ row.case.assessment.actorId }} · {{ row.case.assessment.recordedAt }} · 依据：{{ row.case.assessment.reason }}</p>
        <details v-if="row.case?.assessmentHistory?.length"><summary>评估历史</summary><p v-for="entry in row.case.assessmentHistory" :key="entry.id">{{ entry.recordedAt }} · {{ entry.actorId }} · 输入 {{ entry.inputVersion }} / 计划 {{ entry.planVersion }} · {{ impactLabel(entry.decision) }}：{{ entry.reason }}</p></details>
        <button :disabled="disabled" @click="$emit('difference', row)">查看准确前后差异</button>
        <button v-if="iteration && canWrite && row.case && !['WITHDRAWN', 'ADOPTED', 'INPUT_REPLACED'].includes(row.state)" :disabled="disabled" @click="select(row)">评估此迭代影响</button>
      </article>
      <form v-if="source && canWrite" @submit.prevent="confirmRequest = true">
        <label>影响请求依据<textarea v-model="reason" aria-label="影响请求依据" required maxlength="8000" :disabled="disabled" /></label>
        <button v-if="source.isCurrent !== false" :disabled="disabled || !reason.trim()">发起影响请求</button>
        <button v-if="source.request?.action === 'REQUEST' && source.candidateState !== 'APPROVED'" type="button" :disabled="disabled || !reason.trim()" @click="confirmWithdraw = true">撤回此影响请求</button>
      </form>
      <div v-if="confirmRequest || confirmWithdraw" role="group" aria-label="确认影响请求">
        <p>本次操作针对已核对的准确候选版本 {{ source?.candidate.version }}。</p>
        <p>{{ confirmWithdraw ? '撤回只结束此未批准候选的影响请求，保留原需求问题和全部历史。' : '向上列实际迭代登记评估请求。请求本身不会改写已采用的需求输入。' }}</p>
        <button :disabled="disabled" @click="request">确认{{ confirmWithdraw ? '撤回' : '发起' }}</button><button :disabled="disabled" @click="confirmRequest = confirmWithdraw = false">取消</button>
      </div>
      <form v-if="selected" @submit.prevent="confirmAssessment = true">
        <h4>评估：{{ selected.name }}</h4>
        <label>影响结论<select v-model="decision" aria-label="影响结论" :disabled="disabled"><option value="BLOCKED">尚有阻塞</option><option value="KEEP_CURRENT">保留原输入继续执行</option><option value="READY_TO_ADOPT">新版批准后准备采用</option></select></label>
        <p>保留原输入须说明继续按原需求执行的依据；准备采用只登记计划，实际采用仍需通过“替换需求输入”。</p>
        <label>评估依据<textarea v-model="assessmentReason" aria-label="影响评估依据" required maxlength="8000" :disabled="disabled" /></label>
        <button :disabled="disabled || !assessmentReason.trim()">登记影响评估</button><button type="button" :disabled="disabled" @click="selected = null">取消评估</button>
      </form>
      <div v-if="confirmAssessment && selected" role="group" aria-label="确认影响评估"><p>确认按当前输入和计划登记“{{ impactLabel(decision) }}”？</p><button :disabled="disabled" @click="assess">确认评估</button><button :disabled="disabled" @click="confirmAssessment = false">返回修改</button></div>
    </template>
    <section v-if="difference" aria-label="准确需求前后差异">
      <h4>准确需求差异：{{ difference.before.version }} → {{ difference.after.version }}</h4>
      <p v-if="!difference.changes.length">两版的需求内容与范围没有差异。</p>
      <article v-for="(change, index) in difference.changes" :key="index"><p>{{ area(change.area) }} · {{ field(change.item) }} <span v-if="change.requirementId">· {{ change.requirementId }}</span></p><div class="comparison"><div><strong>原版</strong><pre>{{ content(change.before) }}</pre></div><div><strong>后继版</strong><pre>{{ content(change.after) }}</pre></div></div></article>
    </section>
  </section>
</template>
<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { impactLabel, type ImpactContext, type IterationImpactContext, type ImpactConsumer, type ImpactDecision, type SuccessorDifference } from '@/api/plm/requirementImpact'
const props = defineProps<{ source?: ImpactContext | null; iteration?: IterationImpactContext | null; difference?: SuccessorDifference | null; disabled: boolean; canWrite: boolean; candidateVersion?: string }>()
const emit = defineEmits<{ load: [version?: string]; difference: [row: ImpactConsumer]; request: [action: 'REQUEST' | 'WITHDRAW', reason: string]; assess: [row: ImpactConsumer, decision: ImpactDecision, reason: string] }>()
const selectedVersion = ref(props.candidateVersion || '')
const rows = computed(() => props.source?.consumers || props.iteration?.cases || [])
const reason = ref(''), assessmentReason = ref(''), selected = ref<ImpactConsumer | null>(null), decision = ref<ImpactDecision>('BLOCKED')
const confirmRequest = ref(false), confirmWithdraw = ref(false), confirmAssessment = ref(false)
const area = (value: string) => ({ SET: '集合范围', MEMBERSHIP: '准确成员', REQUIREMENT: '需求定义', CRITERION: '验收判据', SOURCE_CLAUSE: '来源条款及适用条件' } as Record<string, string>)[value] || value
const field = (value: string) => ({ name: '名称', scope: '适用范围', completenessBasis: '完整性依据', conflicts: '冲突裁决', member: '成员处置', interpretation: '解释', necessity: '必要性', applicableObject: '适用对象', questions: '澄清问题', relations: '关系', source: '准确来源', id: '编号', version: '版本', originalText: '原文', locator: '原文位置', terms: '适用条件', customers: '客户', products: '产品', configurations: '配置', mode: '范围方式', namespace: '标识体系', keys: '对象标识', from: '起始时间', until: '截止时间', fromUnbounded: '起始无限制', untilUnbounded: '截止无限制', waivability: '可放宽性', basis: '依据', kind: '类型', object: '对象', conditions: '条件', methodRequirement: '方法要求', lower: '下限', upper: '上限', lowerInclusive: '包含下限', upperInclusive: '包含上限', proposedUnit: '单位', unitBasis: '单位依据', precision: '精度', qualitativeBasis: '定性依据', requirement: '准确需求', evaluationControl: '评审控制', approval: '批准', disposition: '处置', evaluation: '判定依据', unit: '单位版本', lowerUnbounded: '无下限', upperUnbounded: '无上限' } as Record<string, string>)[value] || value
function content(raw: string | null): string {
  if (raw === null) return '无此项'
  const format = (value: unknown): string => {
    if (value === null) return '未指定'
    if (typeof value === 'boolean') return value ? '是' : '否'
    if (Array.isArray(value)) return value.length ? value.map(format).join('\n') : '无'
    if (typeof value === 'object') return Object.entries(value).filter(([key]) => !['schema', 'owner', 'digest'].includes(key)).map(([key, item]) => `${field(key)}：${format(item)}`).join('\n')
    return String(value)
  }
  return format(JSON.parse(raw))
}
function select(row: ImpactConsumer) { selected.value = row; assessmentReason.value = ''; decision.value = 'BLOCKED'; confirmAssessment.value = false }
function request() { emit('request', confirmWithdraw.value ? 'WITHDRAW' : 'REQUEST', reason.value); confirmRequest.value = confirmWithdraw.value = false }
function assess() { if (selected.value) emit('assess', selected.value, decision.value, assessmentReason.value); confirmAssessment.value = false }
watch(() => [props.source, props.iteration, props.canWrite], () => { selected.value = null; confirmAssessment.value = confirmRequest.value = confirmWithdraw.value = false })
watch(selectedVersion, () => { confirmRequest.value = confirmWithdraw.value = false })
</script>
<style scoped>
section,article{padding:16px;border:1px solid var(--el-border-color,#d8dce3);border-radius:8px;margin:16px 0}label{display:flex;flex-direction:column;gap:6px;margin:12px 0}button,select,textarea{font:inherit;padding:8px;margin:4px}textarea{min-height:80px}pre{white-space:pre-wrap;overflow-wrap:anywhere}.comparison{display:grid;grid-template-columns:1fr 1fr;gap:16px}.comparison>div{min-width:0}button:disabled{opacity:.55}@media(max-width:640px){.comparison{grid-template-columns:1fr}}
</style>
