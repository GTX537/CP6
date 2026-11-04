<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { ElMessageBox } from 'element-plus'
import { identityStamp, requireIdentity } from '@/auth/identity'
import { personalFormApi as api, type PersonalDraft, type RecoverySources, type RecoveryPreview } from '@/api/oa/personalForm'
import { formObject, readFormJson, schemaFields } from './personalFormJson'
import { personalFormError } from './personalFormError'
import PersonalFormFields from './PersonalFormFields.vue'
const props = defineProps<{ draft: PersonalDraft; locked: boolean }>()
const emit = defineEmits<{ recover: [history: string, expected: string, reason: string] }>()
const sources = ref<RecoverySources>(), snapshot = ref<RecoveryPreview>(), page = ref(1), loading = ref(false), error = ref(''), reason = ref('')
const fields = computed(() => snapshot.value ? schemaFields(snapshot.value.snapshot.schemaJson) : [])
let generation = 0, live = true, controller: AbortController | undefined
function clear() { generation++; controller?.abort(); sources.value = undefined; snapshot.value = undefined; reason.value = ''; error.value = ''; loading.value = false; page.value = 1 }
async function read(history?: string) {
  if (props.locked || loading.value) return
  const current = ++generation, identity = identityStamp(), draft = props.draft.draftId
  controller?.abort(); controller = new AbortController(); loading.value = true; error.value = ''; snapshot.value = undefined
  if (!history) sources.value = undefined
  try {
    const value = history ? await api.recoveryPreview(draft, history, controller.signal) : await api.rebaseHistory(draft, page.value, controller.signal)
    requireIdentity(identity); if (!live || generation !== current) return
    if (history) snapshot.value = value as RecoveryPreview
    else sources.value = value as RecoverySources
  } catch (e) { if (live && generation === current && identity === identityStamp()) error.value = personalFormError(e) }
  finally { if (live && generation === current) loading.value = false }
}
async function recover() {
  const value = snapshot.value, identity = identityStamp(), current = generation
  if (!value?.source.canRecover || props.locked || loading.value || !reason.value.trim()) return
  try { await ElMessageBox.confirm(`将重基前 v${value.source.sourceVersion} 的内容恢复为新草稿，当前草稿及历史记录继续保留。页面尚未保存的修改不会复制到新草稿。`, '恢复为新草稿') } catch { return }
  if (!live || current !== generation || identity !== identityStamp() || props.locked) return
  emit('recover', value.source.historyId, value.expectedVersion, reason.value.trim())
}
watch(() => [props.draft.draftId, props.draft.rowVersion], clear)
onBeforeUnmount(() => { live = false; clear() })
</script>
<template>
  <section aria-label="重基前快照恢复" class="draft-recovery">
    <h4>重基前快照</h4>
    <el-button :disabled="locked" :loading="loading" @click="read()">查看重基历史</el-button>
    <el-alert v-if="error" :title="error" type="error" :closable="false" />
    <template v-if="sources">
      <p v-if="!sources.total">此草稿没有可读取的重基前快照。</p>
      <ul><li v-for="source in sources.items" :key="source.historyId">
        v{{ source.sourceVersion }} → v{{ source.targetVersion }} · {{ source.rebasedAtUtc }}
        <el-button :disabled="locked || loading" @click="read(source.historyId)">查看此快照</el-button>
      </li></ul>
      <el-pagination v-if="sources.total > 20" v-model:current-page="page" :total="sources.total" :page-size="20" layout="prev, pager, next" :disabled="locked || loading" @current-change="read()" />
    </template>
    <div v-if="snapshot" class="recovery-preview">
      <p>重基前版本 v{{ snapshot.source.sourceVersion }} · {{ snapshot.snapshot.title || snapshot.snapshot.name }}</p>
      <PersonalFormFields :fields="fields" :model-value="formObject(readFormJson(snapshot.snapshot.dataJson))" :mask="snapshot.snapshot.fieldMask" disabled />
      <p>恢复会创建新草稿；附件在恢复时重新核对当前可用性，不可用时停止恢复并保留原件。</p>
      <el-alert v-if="!snapshot.source.canRecover" title="当前权限或留存策略不允许恢复此快照，原记录仍保留。" type="warning" :closable="false" />
      <template v-else>
        <el-input v-model="reason" aria-label="恢复说明" maxlength="500" placeholder="填写恢复说明" :disabled="locked || loading" />
        <el-button :disabled="locked || loading || !reason.trim()" @click="recover">恢复为新草稿</el-button>
      </template>
    </div>
  </section>
</template>
<style scoped>.draft-recovery { margin-top: 20px; } li { margin: 8px 0; } .recovery-preview { padding: 12px; border: 1px solid var(--el-border-color); }</style>
