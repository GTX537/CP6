<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { nativeTaskOperationsApi as api, type OperationContext, type BackPreview } from '@/api/oa/nativeTaskOperations'
import { useNativeOperation } from './useNativeOperation'
import NativeOperationRecovery from './NativeOperationRecovery.vue'
const props = defineProps<{ taskId: string; contextKind: 'SFS' | 'APP' }>()
const emit = defineEmits<{ completed: []; privateClear: [] }>()
const context = ref<OperationContext>(), preview = ref<BackPreview>(), action = ref(''), target = ref(''), source = ref('before'), backIndex = ref(0), reason = ref(''), loading = ref(false)
const op = useNativeOperation('cp6-native-task-operation', clear, () => emit('completed'), () => emit('privateClear'))
const { pending, busy, canRetry, error, message, locked } = op
const back = computed(() => context.value?.sendBackTargets[backIndex.value])
let generation = 0
function clear() { generation++; context.value = undefined; preview.value = undefined; reason.value = ''; target.value = ''; loading.value = false }
async function load() {
  if (!props.taskId || locked.value || loading.value) return
  const check = op.capture(), request = ++generation; loading.value = true; error.value = ''; context.value = undefined; preview.value = undefined
  try { const value = await api.context(props.taskId); check(); if (request !== generation) return; context.value = value; action.value = value.allowedActions[0] ?? ''; target.value = ''; backIndex.value = 0 }
  catch (e) { try { check(); if (request === generation) op.failed(e) } catch { /* stale */ } }
  finally { if (request === generation) loading.value = false }
}
async function inspect() {
  if (!context.value || !back.value || locked.value || loading.value) return
  const check = op.capture(), request = ++generation; loading.value = true; preview.value = undefined
  const chosen = { targetKind: back.value.targetKind, targetNodeId: back.value.targetNodeId }
  try { const value = await api.preview(props.taskId, chosen); check(); if (request === generation) preview.value = value }
  catch (e) { try { check(); if (request === generation) op.failed(e) } catch { /* stale */ } }
  finally { if (request === generation) loading.value = false }
}
async function submit() {
  const c = context.value
  if (!c || locked.value || loading.value || !c.allowedActions.includes(action.value)) return
  if (!reason.value.trim()) { error.value = '请填写操作原因。'; return }
  if (action.value === 'sendback') {
    const p = preview.value; if (!p) { error.value = '请先预览退回影响。'; return }
    await op.begin('OA_TASK_SENDBACK', props.taskId, p.instanceVersion, JSON.stringify({ targetKind: p.targetKind, targetNodeId: p.targetNodeId, previewDigest: p.previewDigest, expectedTaskVersion: p.taskVersion, comment: reason.value }))
  } else {
    if (!c.candidates.some(x => x.userId === target.value)) { error.value = '请选择当前合格人员。'; return }
    const body = { taskId: props.taskId, toUserId: target.value, reason: reason.value, expectedTaskVersion: c.task.taskRowVersion, ...(action.value === 'addsign' ? { source: source.value } : {}) }
    await op.begin(action.value === 'transfer' ? 'OA_TASK_TRANSFER' : 'OA_TASK_ADDSIGN', props.taskId, c.task.instanceRowVersion, JSON.stringify(body))
  }
}
watch([action, backIndex], () => { generation++; preview.value = undefined; loading.value = false })
watch(() => props.taskId, clear)
</script>
<template>
  <section v-if="taskId || pending || error" aria-label="退回转交加签">
    <h3>退回、转交与加签</h3>
    <NativeOperationRecovery :pending="pending" :busy="busy" :can-retry="canRetry" :error="error" :message="message" @recover="op.recover($event)" />
    <el-button v-if="taskId" :disabled="locked || loading" @click="load">读取当前操作条件</el-button>
    <template v-if="context">
      <p v-if="!context.allowedActions.length">当前没有可办理的退回、转交或加签操作。</p>
      <template v-else>
        <label>操作 <select v-model="action" aria-label="操作" :disabled="locked || loading"><option v-for="a in context.allowedActions" :key="a" :value="a">{{ { sendback: '退回', transfer: '转交', addsign: '加签' }[a] || a }}</option></select></label>
        <template v-if="action === 'sendback'">
          <label>退回目标 <select v-model="backIndex" aria-label="退回目标" :disabled="locked || loading"><option v-for="(t, i) in context.sendBackTargets" :key="i" :value="i">{{ t.name }}</option></select></label>
          <el-button :disabled="locked || loading" @click="inspect">预览退回影响</el-button>
          <div v-if="preview" role="status"><p>将撤销 {{ preview.affectedTaskIds.length }} 个在途任务，影响 {{ preview.affectedTokenIds.length }} 个流转位置。{{ preview.wholeInstanceRefill ? '退回整个流程。' : '只处理预览中的范围。' }}</p>
            <p v-if="contextKind === 'APP' && preview.targetKind === 'STARTER'">业务提交内容保持冻结。发起人需要在原业务页面撤回并创建新修订。</p>
          </div>
        </template>
        <template v-else><label>目标人员 <select v-model="target" :disabled="locked || loading"><option value="">请选择</option><option v-for="c in context.candidates" :key="c.userId" :value="c.userId">{{ c.name }}</option></select></label>
          <label v-if="action === 'addsign'">加签方式 <select v-model="source" :disabled="locked || loading"><option value="before">前加签</option><option value="after">后加签</option></select></label>
        </template>
        <label>原因 <textarea v-model="reason" maxlength="1000" :disabled="locked || loading" /></label>
        <el-button :disabled="locked || loading || action === 'sendback' && !preview" @click="submit">确认操作</el-button>
      </template>
    </template>
  </section>
</template>
<style scoped>section { padding: 12px; border: 1px solid var(--el-border-color); margin: 12px 0; }label { display: block; margin: 8px 0; }textarea { display: block; width: min(500px, 100%); }</style>
