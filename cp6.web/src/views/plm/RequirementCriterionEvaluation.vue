<template>
  <div class="unit-basis">
    <p>专业评价单位：{{ criterion.evaluation ? `${criterion.evaluation.unit.id} · 版本 ${criterion.evaluation.unit.version}` : '尚未选择准确单位' }}</p>
    <label>单位搜索<input v-model="search" :aria-label="`评价单位搜索 ${index}`" /></label>
    <button type="button" :disabled="busy" @click="find(1)">查找评价单位</button>
    <button type="button" :disabled="busy || page === 1" @click="find(page - 1)">上一页单位</button>
    <button type="button" :disabled="busy || choices.length < 20" @click="find(page + 1)">下一页单位</button>
    <label>准确单位版本<select v-model="selected" :aria-label="`评价单位版本 ${index}`"><option value="">请选择</option><option v-for="u in choices" :key="key(u)" :value="key(u)">{{ u.profile.code }} / {{ u.profile.name }} · {{ u.profile.dimensionKey }} · 版本 {{ u.reference.version }}</option></select></label>
    <button type="button" :disabled="busy || !selected" @click="choose">采用评价单位</button>
    <template v-if="criterion.evaluation">
      <label><input v-model="criterion.evaluation.lowerUnbounded" :aria-label="`明确无下界 ${index}`" type="checkbox" :disabled="!!criterion.lower" /> 明确无下界（须有来源依据）</label>
      <label><input v-model="criterion.evaluation.upperUnbounded" :aria-label="`明确无上界 ${index}`" type="checkbox" :disabled="!!criterion.upper" /> 明确无上界（须有来源依据）</label>
      <button type="button" :disabled="busy" @click="criterion.evaluation = null">清除评价单位引用</button>
    </template>
    <p>未勾选且未填写的边界仍为未定。采用单位不会转换原数值或降低原精度。</p><p v-if="error" role="alert">{{ error }}</p>
  </div>
</template>
<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import type { RequirementCriterion } from '@/api/plm/engineeringRequirements'
import { uomApi, type UomVersion } from '@/api/erp/uomProfiles'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
const props = defineProps<{ criterion: RequirementCriterion; index: number }>()
const search = ref(''), selected = ref(''), choices = ref<UomVersion[]>([]), page = ref(1), busy = ref(false), error = ref('')
const key = (u: UomVersion) => `${u.reference.id}:${u.reference.version}:${u.reference.digest}`
let generation = 0, live = true
function clear() { generation++; choices.value = []; search.value = selected.value = error.value = ''; busy.value = false }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return
  const epoch = generation, stamp = identityStamp(), criterion = props.criterion
  const check = () => { requireIdentity(stamp); if (!live || epoch !== generation || props.criterion !== criterion || !authenticationCanRead()) throw new Error('身份或判据已变化。') }
  busy.value = true; error.value = ''
  try { await action(check); check() } catch (e: any) { try { check(); choices.value = []; selected.value = ''; error.value = e?.response?.status === 403 ? '当前无权读取所选单位。' : '无法采用单位，请核对当前版本和权限。' } catch { /* Obsolete response. */ } }
  finally { if (live && epoch === generation) busy.value = false }
}
async function find(p: number) { await run(async check => { choices.value = []; selected.value = ''; const r = await uomApi.list(search.value, p); check(); if (r.code !== 0 || !Array.isArray(r.data)) throw new Error(); choices.value = r.data; page.value = p }) }
async function choose() { await run(async check => {
  const choice = choices.value.find(x => key(x) === selected.value); if (!choice) return
  const r = await uomApi.read(choice.reference.id, choice.reference.version); check()
  if (r.code !== 0 || !r.data || key(r.data) !== key(choice) || !r.data.isCurrent || !r.data.profile.enabled) throw new Error()
  props.criterion.evaluation = { schema: 'requirement-evaluation/1', unit: r.data.reference, lowerUnbounded: false, upperUnbounded: false }
  props.criterion.proposedUnit = r.data.profile.code
}) }
onMounted(() => window.addEventListener(AUTH_EVENT, clear))
onBeforeUnmount(() => { live = false; clear(); window.removeEventListener(AUTH_EVENT, clear) })
</script>
<style scoped>
.unit-basis{border:1px solid #c4cbd6;border-radius:6px;padding:12px;grid-column:1/-1}label{display:flex;gap:8px;margin:8px 0;align-items:center}input,select,button{font:inherit;padding:6px;margin:4px}p{overflow-wrap:anywhere}[role=alert]{color:#b42318}
</style>
