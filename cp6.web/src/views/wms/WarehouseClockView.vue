<template>
  <main class="warehouse-clock">
    <h1>仓库时钟政策</h1><p>指定规则时间评估采用的已认可时钟来源与有效期。每次使用都会重新核对当前政策。</p>
    <button :disabled="busy || !canRead" @click="reload">重新载入</button><p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <template v-if="context">
      <p>仓库 {{ context.warehouseCode }} · 政策版本 {{ context.version }} · {{ context.policy?.original.definition.enabled ? '已配置' : '未启用' }}</p>
      <section v-if="context.source"><h2>可采用的时钟来源</h2>
        <p>{{ context.source.authority.evidenceMode === 'DEMO' ? '演练来源' : '正式来源登记' }} · 来源版本 {{ context.source.reference.version }}</p>
        <p>认可依据：{{ context.source.authority.attestationRef }}</p><p>来源有效期：{{ context.source.authority.validFrom }} 至 {{ context.source.authority.validTo }}</p>
        <p>已声明误差上界 {{ context.source.authority.maxErrorMilliseconds }} 毫秒。实际评估还会计入本次读取耗时；此登记本身不证明外部时钟同步状态。</p>
      </section><p v-else role="status">当前没有可采用的时钟来源，不能启用新政策；已保存原件仍可读取或停用。</p>
      <section v-if="pending"><p>有一笔结果待确认。</p><button :disabled="busy" @click="recover(false)">查询原操作</button><button :disabled="busy || !permission.has('wms-warehouse:abandon')" @click="recover(true)">关闭未提交原操作</button></section>
      <form @submit.prevent="save"><fieldset :disabled="busy || !!pending || !canEdit"><legend>本仓库政策</legend>
        <label>政策状态<select v-model="state" aria-label="政策状态" required><option disabled value="">请选择</option><option value="enabled" :disabled="!context.source">启用当前来源</option><option v-if="context.policy" value="disabled">停用保存的政策</option></select></label>
        <fieldset :disabled="state === 'disabled'"><legend>政策有效期</legend>
          <label>开始时刻<input v-model="validFrom" aria-label="开始时刻" placeholder="2026-10-28T08:00:00+08:00" required /></label>
          <label>结束时刻<input v-model="validTo" aria-label="结束时刻" placeholder="2026-10-29T08:00:00+08:00" required /></label>
          <p>输入完整日期、时间与时区偏移，政策区间须包含在来源有效期内。结束时刻不包含在有效区间中。</p>
        </fieldset><label>操作原因<textarea v-model="reason" aria-label="操作原因" maxlength="1000" required /></label><button>保存政策</button>
      </fieldset></form>
      <form @submit.prevent="readHistory"><label>历史版本<input v-model="historyVersion" aria-label="历史版本" pattern="[1-9][0-9]*" required /></label><button :disabled="busy">读取历史</button></form>
      <article v-if="history"><h2>历史版本 {{ history.reference.version }}</h2><p>{{ history.original.reason }}</p><details><summary>查看保存原件</summary><pre>{{ history.originalJson }}</pre></details></article>
      <p>本页配置不会使规则生效。规则应用仍须完成审批、占用及作业写入保护检查。</p>
    </template><RouterLink to="/wms/warehouse">返回仓库查询</RouterLink>
  </main>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { warehouseClockApi as api, type ClockContext, type ClockPolicy } from '@/api/wms/warehouseClock'
import { clockKey, pendingClock, saveClock, recoverClock } from '@/auth/warehouseClockRecovery'
import { transferResponse as value } from '@/auth/transferEnvironmentRecovery'
import { identityStamp, requireIdentity, AUTH_EVENT } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { masterUuid } from '@/types/wms/warehouseOwner'
import { usePermissionStore } from '@/stores/permission'
const route=useRoute(), permission=usePermissionStore(), warehouse=computed(()=>String(route.params.id||''))
const canRead=computed(()=>permission.loaded&&permission.has('wms-warehouse:query')), canEdit=computed(()=>permission.has('wms-warehouse:clock-policy'))
const context=ref<ClockContext|null>(null), history=ref<ClockPolicy|null>(null), state=ref(''), validFrom=ref(''), validTo=ref(''), reason=ref(''), historyVersion=ref('')
const busy=ref(false), error=ref(''), notice=ref(''), pending=ref<string|null>(null)
let live=true, generation=0
function clear(){context.value=null;history.value=null;state.value='';validFrom.value='';validTo.value='';reason.value='';historyVersion.value='';pending.value=null}
async function run(action:(check:()=>void)=>Promise<void>){
  if(busy.value||!canRead.value||!authenticationCanRead())return
  const epoch=generation,stamp=identityStamp(),id=warehouse.value;busy.value=true;error.value='';notice.value=''
  const check=()=>{requireIdentity(stamp);if(!live||epoch!==generation||id!==warehouse.value||!authenticationCanRead()||!canRead.value)throw new Error('当前身份或仓库已变化。')}
  try{check();if(!masterUuid(id))throw new Error('请从仓库查询进入时钟政策。');await action(check)}
  catch(e:any){if(live&&epoch===generation){if([401,403,404].includes(e?.response?.status))clear();error.value=e?.message||'操作未完成，请核对来源与权限。'}}
  finally{if(live&&epoch===generation){busy.value=false;if(context.value)try{pending.value=pendingClock(context.value)}catch(e:any){pending.value='invalid';error.value=e.message}}}
}
async function load(check:()=>void){const c=value(await api.context(warehouse.value));check();clockKey(c);if(c.warehouseId!==warehouse.value)throw new Error('返回仓库不一致。');context.value=c;pending.value=pendingClock(c);state.value=c.policy?(c.policy.original.definition.enabled?'enabled':'disabled'):'';validFrom.value=c.policy?.original.definition.validFrom||'';validTo.value=c.policy?.original.definition.validTo||''}
async function reload(){await run(async check=>{clear();await load(check)})}
async function save(){await run(async check=>{
  const c=context.value;if(!c||!canEdit.value||!['enabled','disabled'].includes(state.value)||!reason.value.trim())throw new Error('请选择政策状态并填写原因。')
  if(state.value==='enabled'&&(!c.source||!validFrom.value||!validTo.value))throw new Error('请取得认可的时钟来源并填写完整有效期。')
  const definition=state.value==='disabled'&&c.policy?{...c.policy.original.definition,enabled:false}:{enabled:true,source:c.source!.reference,validFrom:validFrom.value,validTo:validTo.value}
  await saveClock(c,{definition,reason:reason.value},check);check();reason.value='';history.value=null;await load(check);notice.value='政策已保存；规则尚未因此生效。'
})}
async function recover(close:boolean){await run(async check=>{if(!context.value||close&&!permission.has('wms-warehouse:abandon'))throw new Error('当前权限不足。');const r=await recoverClock(context.value,close,check);check();if(r.state!=='UNKNOWN'){clear();await load(check)}notice.value=r.state==='UNKNOWN'?'结果仍未知，请继续查询。':r.state==='COMMITTED'?'已找回原操作并载入当前政策。':'未提交原操作已关闭。'})}
async function readHistory(){await run(async check=>{history.value=null;const v=historyVersion.value;if(!/^[1-9][0-9]*$/.test(v))throw new Error('请输入准确历史版本。');const r=value(await api.history(warehouse.value,v));check();if(r.original.warehouseId!==warehouse.value||r.original.tenantId!==context.value?.tenantId||r.reference.version!==v||r.reference.id!==context.value?.policy?.reference.id)throw new Error('历史原件不一致。');history.value=r})}
function reset(){generation++;busy.value=false;clear();error.value='';notice.value='';if(canRead.value&&authenticationCanRead())void reload()}
watch(warehouse,reset);watch(canRead,reset);onMounted(()=>{window.addEventListener(AUTH_EVENT,reset);void reload()});onBeforeUnmount(()=>{live=false;generation++;clear();window.removeEventListener(AUTH_EVENT,reset)})
</script>
<style scoped>
.warehouse-clock{max-width:900px;margin:auto;padding:24px}fieldset,section,article{border:1px solid #b5c4cf;border-radius:8px;padding:16px;margin:16px 0}label{display:flex;flex-direction:column;gap:6px;margin:12px 0}input,textarea,select,button{font:inherit;padding:8px}button{margin:6px}pre{white-space:pre-wrap;overflow-wrap:anywhere}[role=alert]{color:#a32626}
</style>
