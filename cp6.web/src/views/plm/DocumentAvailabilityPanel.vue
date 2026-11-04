<template>
  <section class="availability" :aria-label="`修订 ${document.reference.version} 可用性`">
    <h3>修订 {{ document.reference.version }} 的可用性</h3>
    <p>{{ stateName(document.availability.state) }} · {{ reasonText(document.availability) }}</p>
    <p>只作用于这个准确修订，保留原内容、专业批准记录和历史引用。</p>
    <form v-if="canManage && document.availability.state !== 'WITHDRAWN'" @submit.prevent="prepare">
      <fieldset :disabled="disabled">
        <label>处理方式<select v-model="action" aria-label="可用性处理方式"><option value="">请选择</option><option v-if="document.availability.state === 'AVAILABLE'" value="HOLD">暂停取用</option><option v-if="document.availability.state === 'HELD'" value="RELEASE">恢复取用</option><option value="WITHDRAW">撤回此修订</option></select></label>
        <label>处理原因<textarea v-model="reason" aria-label="可用性处理原因" maxlength="4000" required /></label>
        <button :disabled="!action || !reason.trim()">核对处理</button>
      </fieldset>
    </form>
    <div v-if="confirmation" role="alert">
      <p>{{ document.body.name }} · 修订 {{ document.reference.version }}：{{ actionName(confirmation.action) }}。</p>
      <p v-if="confirmation.action === 'WITHDRAW'">撤回后此修订不能恢复取用；需另行形成后继修订。已发出的现场副本仍需按原业务处理。</p>
      <p>{{ confirmation.reason }}</p>
      <button :disabled="disabled" @click="confirm">确认处理此修订</button><button :disabled="disabled" @click="confirmation = null">取消处理</button>
    </div>
    <button :disabled="disabled" @click="$emit('history', false)">读取可用性记录</button>
    <ol v-if="history"><li v-for="item in history" :key="item.stateVersion">{{ item.recordedAt }} · {{ actionName(item.action) }} · {{ reasonText(item) }}</li></ol>
    <button v-if="history?.length && history[history.length - 1]?.stateVersion !== '1'" :disabled="disabled" @click="$emit('history', true)">更早的可用性记录</button>
  </section>
</template>
<script setup lang="ts">
import { ref, watch } from 'vue'
import type { DocumentAvailability, DocumentAvailabilityCommand, DocumentSnapshot } from '@/api/plm/controlledDocument'
const props = defineProps<{ document: DocumentSnapshot; disabled: boolean; canManage: boolean; history?: DocumentAvailability[] }>()
const emit = defineEmits<{ change: [command: Omit<DocumentAvailabilityCommand, 'operationId'>]; history: [more: boolean] }>()
const action = ref<'' | DocumentAvailabilityCommand['action']>(''), reason = ref(''), confirmation = ref<Omit<DocumentAvailabilityCommand, 'operationId'> | null>(null)
const stateName = (state: string) => ({ AVAILABLE: '允许按当前权限取用', HELD: '已暂停取用', WITHDRAWN: '已撤回' }[state] ?? '可用性未确认')
const actionName = (value: string) => ({ REGISTER: '登记可用性', HOLD: '暂停取用', RELEASE: '恢复取用', WITHDRAW: '撤回此修订' }[value] ?? value)
const reasonText = (value: DocumentAvailability) => value.action === 'REGISTER' ? '初始登记，取用仍需当前权限与内容检查。' : value.reason
function prepare() {
  if (props.disabled || !props.canManage || !action.value || !reason.value.trim()) return
  confirmation.value = { document: { ...props.document.reference }, action: action.value, expectedStateVersion: props.document.availability.stateVersion, expectedEtag: props.document.availability.etag, reason: reason.value }
}
function confirm() {
  if (!confirmation.value || props.disabled || !props.canManage) return
  emit('change', confirmation.value); confirmation.value = null
}
watch(() => [props.document.reference, props.document.availability.etag, props.canManage], () => { confirmation.value = null; action.value = ''; reason.value = '' })
watch([action, reason], () => { confirmation.value = null })
</script>
<style scoped>
.availability{border:1px solid var(--el-border-color);padding:16px;margin-block:12px}fieldset{border:0;padding:0}label{display:grid;gap:6px;margin-block:10px}button,select,textarea{font:inherit;padding:8px;margin-right:8px}textarea{max-width:100%;box-sizing:border-box}button:disabled{opacity:.5}ol{padding-left:22px}li{margin-block:8px}
</style>
