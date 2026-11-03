<script setup lang="ts">
import { onBeforeUnmount, ref } from 'vue'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { formOptionApi as api, type FormOptionSource } from '@/api/oa/formOptions'
import { personalFormError } from '@/views/oa/catalog/personalFormError'
const props = defineProps<{ modelValue?: FormOptionSource; disabled?: boolean }>()
const emit = defineEmits<{ 'update:modelValue': [source: FormOptionSource | undefined] }>()
const types = ref<{ id: number; name: string }[]>([]), search = ref(''), page = ref(1), error = ref(''), busy = ref(false)
let epoch = 0, live = true, stamp = identityStamp()
function changed() { if (stamp !== identityStamp()) { stamp = identityStamp(); epoch++; types.value = []; error.value = ''; busy.value = false } }
window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', changed)
async function load(next = false) {
  if (props.disabled) return
  const request = ++epoch, identity = identityStamp(); busy.value = true; error.value = ''
  try { const target = next ? page.value + 1 : 1, result = await api.types(search.value, target); requireIdentity(identity); if (!live || request !== epoch) return; types.value = result; page.value = target }
  catch (e: any) { if (live && request === epoch && identity === identityStamp()) { types.value = []; error.value = personalFormError(e) } }
  finally { if (live && request === epoch) busy.value = false }
}
function choose(event: Event) {
  if (props.disabled) return
  const id = Number((event.target as HTMLSelectElement).value)
  emit('update:modelValue', id ? { owner: 'Main.Sys.DictionaryOptions.v1', scope: 'SHARED', typeId: id } : undefined)
}
onBeforeUnmount(() => { live = false; epoch++; window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', changed) })
</script>
<template>
  <div class="source-picker">
    <label>选项来源
      <select :value="modelValue?.typeId ?? 0" :disabled="disabled" aria-label="选项来源" @change="choose">
        <option :value="0">静态选项</option>
        <option v-if="modelValue && !types.some(t => t.id === modelValue?.typeId)" :value="modelValue.typeId">共享字典 #{{ modelValue.typeId }}</option>
        <option v-for="type in types" :key="type.id" :value="type.id">{{ type.name }}（#{{ type.id }}）</option>
      </select>
    </label>
    <template v-if="!disabled">
      <input v-model="search" maxlength="100" aria-label="查找共享字典" placeholder="查找共享字典">
      <button type="button" :disabled="busy" @click="load()">查询字典</button>
      <button v-if="types.length === 100" type="button" :disabled="busy" @click="load(true)">下一页</button>
      <small>选择共享字典会替换静态选项；可撤销恢复。值保持字典原字符串。</small>
    </template>
    <small v-if="error" role="alert">{{ error }}</small>
  </div>
</template>
<style scoped>
.source-picker { margin: 8px 0; } select, input { width: 100%; box-sizing: border-box; padding: 6px; margin: 5px 0; } small { display: block; color: var(--el-text-color-secondary); } [role=alert] { color: var(--el-color-danger); }
</style>
