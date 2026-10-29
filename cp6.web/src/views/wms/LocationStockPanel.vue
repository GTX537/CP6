<template>
  <section class="stock-assessment"><h2>库位库存与占用依据</h2>
    <p>读取上方已保存候选所在库位的当前库存。已分配数量属于物理库存的一部分；不同单位的数量分别展示。</p>
    <button :disabled="busy || disabled || !canRead" @click="assess">读取当前库存依据</button>
    <p v-if="!canRead">需要规则字段和库存的读取权限。</p><p v-if="error" role="alert">{{ error }}</p>
    <article v-if="result" role="status">
      <h3>完整容量占用尚未确认</h3><p>{{ coverage[result.stockCoverage] }}</p>
      <p>读取时刻：{{ result.observedAt }}。实际应用前须重新核对；本次读取不授予规则生效或作业许可。</p>
      <p v-if="!result.facts.length">本次没有可展示的库存记录；仍需核对未收货、预留和作业义务，不能据此认定容量占用为零。</p>
      <div v-else class="facts"><table><thead><tr><th>产品</th><th>批次</th><th>原记录单位</th><th>物理库存</th><th>其中已分配</th><th>可用库存</th></tr></thead>
        <tbody><tr v-for="r in result.facts" :key="r.stockId"><td>{{ r.productCode }}</td><td>{{ r.lotNumber }}</td><td>{{ r.unitCode || '未记录' }}</td><td>{{ r.physicalQuantity }}</td><td>{{ r.allocatedQuantity }}</td><td>{{ r.availableQuantity }}</td></tr></tbody></table></div>
      <h4>仍需补齐的依据</h4><ul><li v-for="reason in result.missingEvidence" :key="reason">{{ reasons[reason] || '相关来源尚未核定。' }}</li></ul>
    </article>
  </section>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { locationStockAssessmentApi as api, type LocationStockAssessment } from '@/api/wms/locationStockAssessment'
import type { RuleView, RuleScope } from '@/api/wms/locationRule'
import { transferResponse as value } from '@/auth/transferEnvironmentRecovery'
import { identityStamp, requireIdentity, AUTH_EVENT } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
import { masterUuid } from '@/types/wms/warehouseOwner'
const props=defineProps<{source:RuleView;scope:RuleScope;disabled?:boolean}>(),permission=usePermissionStore()
const result=ref<LocationStockAssessment|null>(null),busy=ref(false),error=ref('')
const canRead=computed(()=>permission.loaded&&props.source.redactedFields.length===0&&['wms-location-rule:query','wms-stock:query','wms-location:query','wms-warehouse:query'].every(x=>permission.has(x)))
const signature=computed(()=>JSON.stringify([props.source.reference,props.source.etag,props.source.locationId,props.scope,props.source.redactedFields]))
const coverage={COMPLETE_CURRENT_ROWS:'已读取该库位全部当前库存记录，尚未覆盖完整容量义务。',VISIBLE_ROWS_ONLY:'仅显示当前授权范围内的库存，不能判断整个库位是否为空。',TRUNCATED:'库存记录超过100条，本次仅展示前100条，覆盖不完整。',ARCHIVED_BALANCE_UNRESOLVED:'存在尚未核清的归档库存余额，不能用于完整占用判断。'}
const reasons:Record<string,string>={NON_STOCK_OBLIGATION_COVERAGE_UNPROVEN:'未收货、预留及作业义务的完整来源与重复关系。',EFFECT_OWNER_PROTECTION_UNPROVEN:'实际作业写入时的并发保护与最终核对。',STOCK_CAPACITY_MEASUREMENT_UNPROVEN:'原库存与候选容量指标之间的准确单位、换算及身份关系。',VISIBLE_ROWS_ONLY:'完整库位范围的读取依据。',TRUNCATED:'未展示库存记录的完整核对。',ARCHIVED_BALANCE_UNRESOLVED:'归档库存余额的处理依据。',NATIVE_STOCK_BALANCE_INCONSISTENT:'库存原记录的数量关系存在不一致，需要先核查。'}
let live=true,generation=0
function reset(){generation++;result.value=null;error.value='';busy.value=false}
async function assess(){
  if(busy.value||props.disabled||!canRead.value||!authenticationCanRead())return
  reset();const n=generation,stamp=identityStamp(),key=signature.value;busy.value=true
  const check=()=>{requireIdentity(stamp);if(!live||n!==generation||key!==signature.value||props.disabled||!canRead.value||!authenticationCanRead())throw new Error('当前身份或候选已变化。')}
  try{
    check();const r=value(await api.assess(props.scope.locationId,props.source.reference.id,props.source.etag,props.source.reference));check()
    const expected=props.source.reference,same=r.proposal&&['owner','id','version','digest'].every(k=>r.proposal[k as keyof typeof expected]===expected[k as keyof typeof expected])
    const quantity=(v:unknown)=>typeof v==='string'&&/^-?\d{1,13}\.\d{8}$/.test(v)
    if(r.tenantId!==props.scope.tenantId||r.actorId!==props.scope.actorId||r.locationId!==props.scope.locationId||r.locationId!==props.source.locationId
      ||!masterUuid(r.warehouseId)||!same||r.etag!==props.source.etag||!Number.isFinite(Date.parse(r.observedAt))
      ||r.sourceOwner!=='Main.Wms.NativeStockObservation.v1'||r.qualification!=='CURRENT_NATIVE_STOCK_ONLY'
      ||r.occupancyCoverage!=='UNKNOWN'||r.capacityQuantity!==null||r.admissionGrant!==false||r.requiresFinalEffectRecheck!==true
      ||!(r.stockCoverage in coverage)||typeof r.truncated!=='boolean'||r.truncated!==(r.stockCoverage==='TRUNCATED')
      ||!Array.isArray(r.missingEvidence)||!r.missingEvidence.length||r.missingEvidence.some(x=>typeof x!=='string')
      ||!Array.isArray(r.facts)||r.facts.length>100||new Set(r.facts.map(x=>x.stockId)).size!==r.facts.length
      ||r.facts.some(x=>!masterUuid(x.stockId)||typeof x.productCode!=='string'||typeof x.lotNumber!=='string'||x.unitCode!==null&&typeof x.unitCode!=='string'
        ||!quantity(x.physicalQuantity)||!quantity(x.allocatedQuantity)||!quantity(x.availableQuantity)))throw new Error('返回库存依据与当前候选不一致。')
    result.value=r
  }catch(e:any){if(live&&n===generation){result.value=null;error.value=e?.message||'无法读取库存依据。'}}finally{if(n===generation)busy.value=false}
}
watch(signature,reset);watch(canRead,reset);watch(()=>props.disabled,reset)
onMounted(()=>window.addEventListener(AUTH_EVENT,reset));onBeforeUnmount(()=>{live=false;reset();window.removeEventListener(AUTH_EVENT,reset)})
</script>
<style scoped>
.stock-assessment{border:1px solid #b5c4cf;border-radius:8px;padding:16px;margin-top:24px}.facts{overflow:auto}table{border-collapse:collapse;width:100%}th,td{text-align:left;padding:8px;border-bottom:1px solid #dce2e7;white-space:nowrap}button{font:inherit;padding:8px}p,li{overflow-wrap:anywhere}[role=alert]{color:#a32626}
</style>
