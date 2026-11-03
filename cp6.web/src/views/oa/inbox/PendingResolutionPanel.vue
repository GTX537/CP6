<script setup lang="ts">
import { ref, watch } from 'vue'
import { nativeTaskOperationsApi as api, type ResolutionContext } from '@/api/oa/nativeTaskOperations'
import NativeOperationRecovery from './NativeOperationRecovery.vue'
import { useNativeOperation } from './useNativeOperation'
const props = defineProps<{ instanceId: string }>(), emit = defineEmits<{ completed: []; privateClear: [] }>()
const rows = ref<ResolutionContext[]>([]), selected = ref<ResolutionContext>(), targets = ref<string[]>([]), reason = ref(''), loading = ref(false)
let generation = 0
const op = useNativeOperation('cp6-approver-resolution-operation', clear, () => emit('completed'), () => emit('privateClear'))
const { pending, busy, canRetry, error, message, locked } = op
function clear() { generation++; rows.value = []; selected.value = undefined; targets.value = []; reason.value = ''; loading.value = false }
async function load(id?: string) {
  if (!props.instanceId || locked.value || loading.value) return
  const check = op.capture(); clear(); const request = generation; loading.value = true; error.value = ''
  try {
    if (id) { const value = await api.resolution(id); check(); if (request === generation) selected.value = value }
    else { const value = await api.resolutions(props.instanceId); check(); if (request === generation) rows.value = value }
  } catch (e) { try { check(); if (request === generation) op.failed(e) } catch { /* stale */ } }
  finally { if (request === generation) loading.value = false }
}
async function assign() {
  const c = selected.value; if (!c?.canAssign || locked.value || loading.value) return
  if (!reason.value.trim() || !targets.value.length || targets.value.length > 100 || targets.value.some(id => !c.candidatePrincipalIds.includes(id))) { error.value = '请选择合格人员并填写恢复原因。'; return }
  await op.begin('OA_APPROVER_RESOLVE', c.resolutionId, c.resolutionVersion, JSON.stringify({ expectedInstanceVersion: c.instanceVersion, executionCycleId: c.executionCycleId,
    nodeId: c.nodeId, tokenId: c.tokenId, stageIndex: c.stageIndex, stageRound: c.stageRound, targetPrincipalIds: [...new Set(targets.value)].sort(), routePolicyRef: c.routePolicyRef, reason: reason.value }))
}
watch(() => props.instanceId, clear)
</script>
<template>
  <section v-if="instanceId || pending || error" aria-label="无人审批恢复">
    <h3>无人审批恢复</h3><p>按流程发布时的恢复政策分配审批人，分配本身不会判定通过。</p>
    <NativeOperationRecovery :pending="pending" :busy="busy" :can-retry="canRetry" :error="error" :message="message" @recover="op.recover($event)" />
    <el-button v-if="instanceId" :disabled="locked || loading" @click="load()">读取阻塞位置</el-button>
    <ul><li v-for="r in rows" :key="r.resolutionId">节点 {{ r.nodeId }} · 阶段 {{ r.stageIndex }} / 轮次 {{ r.stageRound }} · {{ r.reason }} · {{ r.state }}
      <el-button v-if="r.state === 'PENDING'" :disabled="locked || loading" @click="load(r.resolutionId)">读取当前恢复条件</el-button></li></ul>
    <template v-if="selected">
      <p>节点 {{ selected.nodeId }}，阶段 {{ selected.stageIndex }} / 轮次 {{ selected.stageRound }}。</p>
      <p v-if="selected.blockingReasons.length">当前不能分配：{{ selected.blockingReasons.join('、') }}</p>
      <label v-for="candidate in selected.candidates" :key="candidate.userId"><input v-model="targets" type="checkbox" :value="candidate.userId" :disabled="locked || loading || !selected.canAssign">{{ candidate.name }}</label>
      <label>恢复原因 <textarea v-model="reason" maxlength="1000" :disabled="locked || loading || !selected.canAssign" /></label>
      <el-button :disabled="locked || loading || !selected.canAssign" @click="assign">确认分配审批人</el-button>
    </template>
  </section>
</template>
<style scoped>section { padding: 12px; border: 1px solid var(--el-border-color); margin: 12px 0; }label { display: block; margin: 8px 0; }textarea { display: block; width: min(500px, 100%); }</style>
