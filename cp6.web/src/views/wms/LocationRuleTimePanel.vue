<template>
  <section class="rule-time"><h2>规则有效期评估</h2>
    <p>核对当前候选的时间条件。时间满足后，仍须核对审批、占用与作业写入保护，规则才可申请生效。</p>
    <form @submit.prevent="evaluate"><fieldset :disabled="busy || disabled || !canRead">
      <label>评估时点<select v-model="kind" aria-label="评估时点"><option value="CURRENT">本次可信观察时点</option><option value="PLANNED">指定时刻预测</option></select></label>
      <label v-if="kind === 'PLANNED'">指定时刻<input v-model="at" aria-label="指定时刻" placeholder="2026-10-28T08:00:00+08:00" required /></label><button>评估有效期</button>
    </fieldset></form><p v-if="error" role="alert">{{ error }}</p>
    <article v-if="result" role="status"><h3>{{ outcomes[result.outcome] }}</h3>
      <p>{{ result.isPrediction ? '指定时刻预测，不代表已兑现效果。' : '仅对下列观察时点有效，实际应用时必须重新检查。' }}</p>
      <p v-if="result.evaluatedAt">评估时刻：{{ result.evaluatedAt }}</p><p v-if="result.outcome === 'UNKNOWN'">{{ reasons[result.reason] || '时间条件无法确认，请核对当前政策和来源。' }}</p>
      <template v-if="result.clock"><p>{{ result.clock.original.evidenceMode === 'DEMO' ? '演练证据' : '已登记正式来源的观察证据' }} · 政策版本 {{ result.clock.original.policy.version }}</p>
        <p>观察时刻：{{ result.clock.original.observedAt }}</p><p>保守区间：{{ result.clock.original.earliest }} 至 {{ result.clock.original.latest }}</p>
      </template><RouterLink :to="`/wms/warehouse/${result.warehouseId}/clock-policy`">查看仓库时钟政策</RouterLink>
    </article>
  </section>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { warehouseClockApi as api, type RuleTimeAssessment, type RuleTimeRequest } from '@/api/wms/warehouseClock'
import type { LocationRuleReviewScope, LocationRuleReviewSource } from '@/api/wms/locationRuleReview'
import { transferResponse as value } from '@/auth/transferEnvironmentRecovery'
import { identityStamp, requireIdentity, AUTH_EVENT } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
import { masterUuid } from '@/types/wms/warehouseOwner'
const props=defineProps<{source:LocationRuleReviewSource;scope:LocationRuleReviewScope;disabled?:boolean}>(), permission=usePermissionStore()
const kind=ref<'CURRENT'|'PLANNED'>('CURRENT'), at=ref(''), result=ref<RuleTimeAssessment|null>(null), busy=ref(false), error=ref('')
const canRead=computed(()=>permission.loaded&&permission.has('wms-location-rule:query')&&permission.has('wms-warehouse:query'))
const signature=computed(()=>JSON.stringify([props.source.reference,props.source.etag,props.scope]))
const outcomes={UNKNOWN:'时间条件未知',TIME_SATISFIED:'该时点满足有效期',NOT_YET_VALID:'尚未到有效期',EXPIRED:'已超过有效期'}
const reasons:Record<string,string>={CLOCK_POLICY_REQUIRED:'仓库尚未配置时钟政策。',CLOCK_POLICY_DISABLED:'仓库时钟政策已停用。',CLOCK_SOURCE_UNAVAILABLE:'认可的时钟来源不可用。',CLOCK_SOURCE_CHANGED:'时钟来源已变化，需要重新绑定政策。',CLOCK_SOURCE_OUTSIDE_VALIDITY:'观察区间超出来源认可的有效期。',CLOCK_POLICY_OUTSIDE_VALIDITY:'观察区间超出仓库政策有效期。',CLOCK_MOVED_BACKWARD:'检测到来源时钟回拨，暂不能确认。',CLOCK_READ_TOO_SLOW:'本次读取耗时超过政策上限，请重新评估。',CLOCK_INTERVAL_CROSSES_RULE_BOUNDARY:'观察误差覆盖了规则的生效或失效边界，暂不能确定。'}
let live=true,generation=0
function reset(){generation++;result.value=null;error.value='';busy.value=false}
async function evaluate(){
  if(busy.value||props.disabled||!canRead.value||!authenticationCanRead())return
  reset();const n=generation,stamp=identityStamp(),key=signature.value;busy.value=true
  const check=()=>{requireIdentity(stamp);if(!live||n!==generation||key!==signature.value||!canRead.value||!authenticationCanRead())throw new Error('当前身份或候选已变化。')}
  try{
    check();const request:RuleTimeRequest={proposal:props.source.reference,kind:kind.value,at:kind.value==='PLANNED'?at.value:null}
    if(request.kind==='PLANNED'&&!request.at)throw new Error('请输入带时区偏移的指定时刻。')
    const r=value(await api.evaluate(props.source.body.locationId,props.source.reference.id,props.source.etag,request));check()
    const same=(a:unknown,b:unknown)=>JSON.stringify(a)===JSON.stringify(b)
    if(r.tenantId!==props.scope.tenantId||r.actorId!==props.scope.actorId||r.locationId!==props.source.body.locationId||!masterUuid(r.warehouseId)
      ||!same(r.proposal,request.proposal)||r.etag!==props.source.etag||!same(r.requested,request)||r.isPrediction!==(request.kind==='PLANNED')
      ||r.qualification!=='RULE_TIME_ONLY'||r.admissionGrant!==false||r.requiresFinalEffectRecheck!==true||!(r.outcome in outcomes)
      ||r.outcome!=='UNKNOWN'&&!r.clock||r.clock&&(r.clock.original.tenantId!==r.tenantId||r.clock.original.warehouseId!==r.warehouseId||r.clock.original.environmentId!==props.scope.environmentId))
      throw new Error('返回评估与当前候选不一致。')
    result.value=r
  }catch(e:any){if(live&&n===generation){result.value=null;error.value=e?.message||'无法读取时间评估。'}}finally{if(n===generation)busy.value=false}
}
watch(signature,reset);watch(canRead,reset);watch([kind,at],reset)
onMounted(()=>window.addEventListener(AUTH_EVENT,reset));onBeforeUnmount(()=>{live=false;reset();window.removeEventListener(AUTH_EVENT,reset)})
</script>
<style scoped>
.rule-time{border:1px solid #b5c4cf;border-radius:8px;padding:16px;margin-top:24px}fieldset{border:0;padding:0}label{display:flex;flex-direction:column;gap:6px;margin:12px 0}input,select,button{font:inherit;padding:8px}p{overflow-wrap:anywhere}[role=alert]{color:#a32626}
</style>
