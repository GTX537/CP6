<template>
  <main class="rule-review">
    <h1>库位规则审批</h1><p>送审固定候选原件与准确来源。审批通过后仍须完成规则生效检查。</p>
    <button :disabled="busy" @click="load">重新载入候选</button><p v-if="error" role="alert">{{ error }}</p>
    <template v-if="source && scope">
      <RouterLink :to="`/wms/location/${source.body.locationId}/rules`">返回库位规则候选</RouterLink>
      <LocationRuleReviewPanel :location-rule="source" :scope="scope" :disabled="busy" :initial-submission="submission" />
    </template>
  </main>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute, RouterLink } from 'vue-router'
import { locationRuleReviewApi as api, type LocationRuleReviewSource, type LocationRuleReviewScope } from '@/api/wms/locationRuleReview'
import { locationRuleResponse as value } from '@/auth/locationRuleReviewResponse'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
import LocationRuleReviewPanel from './LocationRuleReviewPanel.vue'
const route=useRoute(), permissions=usePermissionStore(), source=ref<LocationRuleReviewSource|null>(null), scope=ref<LocationRuleReviewScope|null>(null)
const busy=ref(false), error=ref(''), id=computed(()=>String(route.params.id??'')), submission=computed(()=>typeof route.query.submission==='string'?route.query.submission:undefined)
const canRead=computed(()=>permissions.loaded&&permissions.has('wms-location-rule:query'))
let generation=0,live=true
function reset(){generation++;source.value=null;scope.value=null;busy.value=false;error.value=''}
async function load(){
  if(busy.value||!canRead.value||!authenticationCanRead())return
  const n=++generation,stamp=identityStamp(),target=id.value;busy.value=true;source.value=null;scope.value=null;error.value=''
  const check=()=>{requireIdentity(stamp);if(!live||n!==generation||target!==id.value||!canRead.value||!authenticationCanRead())throw new Error('当前身份或候选已变化。')}
  try{check();const s=value(await api.scope());check();const c=value(await api.context(target));check();
    if(c.tenantId!==s.tenantId||c.locationRuleId!==target||c.locationRule.reference.id!==target||c.locationRule.body.tenantId!==s.tenantId)throw new Error('候选来源不一致。')
    scope.value=s;source.value=c.locationRule
  }catch(e){if(live&&n===generation)error.value=e instanceof Error?e.message:'无法读取规则审批。'}finally{if(n===generation)busy.value=false}
}
function changed(){reset();void load()}
watch(id,changed);watch(canRead,changed)
onMounted(()=>{window.addEventListener(AUTH_EVENT,changed);void load()})
onBeforeUnmount(()=>{live=false;reset();window.removeEventListener(AUTH_EVENT,changed)})
</script>
<style scoped>
.rule-review{max-width:1100px;margin:0 auto;padding:24px}button{font:inherit;padding:8px;margin-bottom:12px}[role=alert]{color:var(--el-color-danger)}
</style>
