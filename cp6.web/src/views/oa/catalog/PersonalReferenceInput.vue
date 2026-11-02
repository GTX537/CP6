<script setup lang="ts">
import { personalFormError } from './personalFormError'
import { onBeforeUnmount, ref, watch } from 'vue'
import { personalFormApi as api } from '@/api/oa/personalForm'
import { identityStamp, requireIdentity } from '@/auth/identity'
import type { FormValue } from './personalFormJson'
const props = defineProps<{ formKey: string; field: string; draftId?: string; modelValue?: FormValue; multiple: boolean; disabled: boolean }>()
const emit = defineEmits<{ 'update:modelValue': [value: FormValue | undefined]; forbidden: [] }>()
const options = ref<{ id: string; name: string }[]>([]), loading = ref(false), error = ref('')
let generation = 0, live = true
async function search(keyword: string) {
  if (props.disabled) return
  const current = ++generation, identity = identityStamp(); loading.value = true; error.value = ''
  try { const values = await api.references(props.formKey, props.field, keyword, props.draftId); requireIdentity(identity); if (live && current === generation) options.value = values }
  catch (e: any) { if (live && current === generation && identity === identityStamp()) { options.value = []; error.value = personalFormError(e); if ([401, 403, 404].includes(e?.response?.status)) emit('forbidden') } }
  finally { if (live && current === generation) loading.value = false }
}
watch(() => [props.formKey, props.draftId, props.field], () => { options.value = []; void search('') }, { immediate: true })
watch(() => props.disabled, disabled => { if (!disabled) void search('') })
onBeforeUnmount(() => { live = false; generation++ })
</script>
<template>
  <el-select :model-value="modelValue" :multiple="multiple" :disabled="disabled" filterable remote :remote-method="search" :loading="loading" clearable @update:model-value="emit('update:modelValue', $event === '' ? undefined : $event)">
    <el-option v-for="option in options" :key="option.id" :value="option.id" :label="option.name" />
  </el-select>
  <small v-if="error" role="alert">{{ error }}</small>
</template>
