<template>
  <section aria-label="版型原订单关联" :aria-busy="busy">
    <h3>原订单关联</h3><p>按当前版型原件登记已有订单关联，或通过原订单入口创建订单。已有引用丢失时不会自动重建。</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <div v-if="pending"><p>存在待确认操作，请先查询原结果。</p><button :disabled="busy" @click="recover(false)">查询原操作</button><button :disabled="busy" @click="recover(true)">结束未提交操作</button></div>
    <div v-if="link?.original"><p>原订单：{{ link.original.order.number }} / 明细 {{ link.original.order.lineNumber }}；手配号：{{ link.original.order.arrangeNumber }}</p>
      <p>{{ link.currentApplicable ? '当前关联可核对' : '原件已变化，请核对当前工具与订单；原结果仍保留' }}。</p>
      <details><summary>原对象身份</summary><p>订单 {{ link.original.order.orderId }}；明细 {{ link.original.order.lineId }}；版型 {{ link.original.tool.id }}。</p></details>
    </div><p v-else-if="link">尚未登记原关联。旧手配号必须经原订单读取确认。</p>
    <p>PE 未执行；此关联不代表订单批准、正式 Sales 发布或实物资格。</p>
    <form v-if="canEdit && !link?.original" @submit.prevent="save"><fieldset :disabled="busy || !!pending || !scope || completed">
      <label>新建订单所属部门编码 <input v-model="department" aria-label="原订单部门编码" maxlength="10" /></label>
      <p>部门范围使用已有可信映射。仅登记已有订单时不改动其部门。</p><p v-if="!canAdd">当前没有新建订单权限，仅可登记已有原关联。</p>
      <button>创建或登记原关联</button></fieldset></form>
    <p v-if="completed">操作已确认。请刷新工具来源后核对当前关联。</p>
  </section>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import type { PlateSourceVersion } from '@/api/erp/plateMoldSources'
import { toolLinkApi, type ToolLinkScope, type ToolLinkView } from '@/api/erp/toolOrderLinks'
import { pendingToolOrder, resolveToolOrder, saveToolOrder, toolResponse, type PendingToolOrder } from '@/auth/toolOrderOperationRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { usePermissionStore } from '@/stores/permission'
const props = defineProps<{ source: PlateSourceVersion }>(), emit = defineEmits<{ denied: [] }>()
const permissions = usePermissionStore(), canEdit = computed(() => permissions.has('erp-plate-mold:edit') && permissions.has('erp-order:query'))
const canAdd = computed(() => permissions.has('erp-order:add'))
const scope = ref<ToolLinkScope | null>(null), link = ref<ToolLinkView | null>(null), pending = ref<PendingToolOrder | null>(null)
const busy = ref(false), department = ref(''), error = ref(''), notice = ref(''), completed = ref(false)
let live = true, generation = 0
function clear() { scope.value = null; link.value = null; pending.value = null; department.value = ''; notice.value = ''; completed.value = false }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return
  const stamp = identityStamp(), current = generation; const check = () => { requireIdentity(stamp); if (!live || generation !== current) throw new Error('页面已变化。') }
  busy.value = true; error.value = ''
  try { await action(check); check() } catch (failure: any) { try { check()
    if ([401, 403].includes(failure?.response?.status)) { clear(); emit('denied') }
    else error.value = failure?.response?.status === 412 ? '工具已变化，输入已保留；请查询或结束原操作，再刷新来源。' : failure?.response?.status === 409 ? '原关联无法确认或操作冲突，请查询原操作；不会自动重建订单。' : failure?.message || '结果暂未确认，请查询原操作。'
  } catch { /* Discard obsolete responses. */ } }
  finally { try { check(); if (scope.value) pending.value = pendingToolOrder(scope.value); busy.value = false } catch { busy.value = false } }
}
async function load() { await run(async check => {
  const current = toolResponse(await toolLinkApi.scope()); check(); if (current.tenantId !== props.source.tenantId) throw new Error('当前租户已变化。')
  scope.value = current; const result = toolResponse(await toolLinkApi.read(props.source.reference.id)); check(); link.value = result
}) }
async function save() {
  if (!scope.value || !canEdit.value || pending.value || completed.value || props.source.source.control.isDeleted) return
  const current = scope.value, source = props.source, orderDepartment = department.value || null
  await run(async check => { await saveToolOrder(current, source.reference.id, check, operationId => toolLinkApi.save({ operationId, tool: source.reference, expectedNativeRowVersion: source.currentNativeRowVersion, orderDepartment })); check(); completed.value = true; notice.value = '原订单关联已提交；PE 未执行。' })
}
async function recover(close: boolean) { const current = scope.value; if (!current) return
  await run(async check => { const result = await resolveToolOrder(current, close, check); check(); completed.value = result.state === 'COMMITTED'
    notice.value = result.state === 'COMMITTED' ? '原操作已提交，请刷新来源核对。' : result.state === 'CLOSED' ? '未提交操作已结束。' : '结果仍未知，请继续查询或结束未提交操作。' })
}
function identityChanged() { generation++; clear(); busy.value = false; error.value = ''; emit('denied') }
onMounted(() => { window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('storage', identityChanged); void load() })
onBeforeUnmount(() => { live = false; generation++; clear(); window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', identityChanged) })
</script>
<style scoped>
section{margin-top:24px;border-top:1px solid #d7e3e7;padding-top:14px}button,input{font:inherit;padding:8px 12px;margin:6px;border:1px solid #bdcdd3;border-radius:6px;background:#fff}fieldset{border:0;padding:0}p{overflow-wrap:anywhere}[role=alert]{color:#a02828}button:disabled{opacity:.5}
</style>
