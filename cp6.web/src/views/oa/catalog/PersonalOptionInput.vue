<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { formOptionApi as api, type FormOptionContext, type FormOptionPage } from '@/api/oa/formOptions'
import { personalFormError } from './personalFormError'
import { writeFormJson, type FormValue } from './personalFormJson'
const props = defineProps<{ context?: FormOptionContext; fieldPath: string[]; modelValue?: FormValue; multiple: boolean; disabled: boolean }>()
const emit = defineEmits<{ 'update:modelValue': [value: FormValue | undefined]; forbidden: [] }>()
const options = ref<FormOptionPage['items']>([]), search = ref(''), error = ref(''), busy = ref(false), page = ref(0), more = ref(false)
let epoch = 0, live = true, identity = identityStamp()
const selected = computed(() => (Array.isArray(props.modelValue) ? props.modelValue : [props.modelValue]).filter((v): v is string => typeof v === 'string' && (props.multiple || v !== '')))
const selection = computed<string | string[]>({ get: () => props.multiple ? selected.value : selected.value[0] ?? '', set: value => { if (!props.disabled) emit('update:modelValue', value === '' ? undefined : value) } })
const shown = computed(() => [...selected.value.filter(v => !options.value.some(o => o.value === v)).map(value => ({ value, label: `${value}（已保存原值）` })), ...options.value])
const raw = computed(() => props.modelValue === undefined ? '未填写' : writeFormJson(props.modelValue))
const unsupported = computed(() => props.modelValue != null && (props.multiple ? !Array.isArray(props.modelValue) || props.modelValue.some(v => typeof v !== 'string') : typeof props.modelValue !== 'string'))
function reset() { epoch++; options.value = []; error.value = ''; page.value = 0; more.value = false; busy.value = false }
function changed() { if (identity !== identityStamp()) { identity = identityStamp(); reset(); emit('forbidden') } }
window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', changed)
async function load(append = false) {
  if (props.disabled || !props.context) return
  const request = ++epoch, stamp = identityStamp(); busy.value = true; error.value = ''
  try {
    const value = await api.query(props.context, props.fieldPath, search.value, append ? page.value + 1 : 1)
    requireIdentity(stamp); if (!live || request !== epoch) return
    options.value = append ? [...options.value, ...value.items.filter(v => !options.value.some(o => o.value === v.value))] : value.items
    page.value = value.page; more.value = value.hasMore
  } catch (e: any) {
    if (live && request === epoch && stamp === identityStamp()) { options.value = []; more.value = false; error.value = personalFormError(e); if ([401, 403, 404].includes(e?.response?.status)) emit('forbidden') }
  } finally { if (live && request === epoch) busy.value = false }
}
watch(() => JSON.stringify([props.context, props.fieldPath, props.disabled]), () => { reset(); void load() }, { immediate: true })
onBeforeUnmount(() => { live = false; epoch++; window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', changed) })
</script>
<template>
  <div class="source-options">
    <span v-if="disabled">{{ raw }}</span>
    <template v-else>
      <p v-if="unsupported" role="status">当前保留值：{{ raw }}。此值的类型与字典不同，请明确重新选择；原值不会自动转换。</p>
      <div class="search"><input v-model="search" aria-label="查找选项" maxlength="100" placeholder="查找当前选项"><button type="button" :disabled="busy || !context" @click="load()">查询</button></div>
      <select v-model="selection" :multiple="multiple" :disabled="!context" aria-label="当前字典选项">
        <option v-if="!multiple" value="">请选择</option>
        <option v-for="option in shown" :key="option.value" :value="option.value">{{ option.label }}</option>
      </select>
      <button v-if="more" type="button" :disabled="busy" @click="load(true)">更多选项</button>
      <small>共享参考字典；提交时核对当前可用值。已保存原值会保留。</small>
      <small v-if="!context" role="alert">当前记录不支持选项查询。</small>
      <small v-if="error" role="alert">{{ error }}</small>
    </template>
  </div>
</template>
<style scoped>
.search { display: flex; gap: 6px; margin-bottom: 6px; } input, select { width: 100%; padding: 7px; box-sizing: border-box; } small { display: block; margin-top: 5px; color: var(--el-text-color-secondary); } [role=alert] { color: var(--el-color-danger); }
</style>
