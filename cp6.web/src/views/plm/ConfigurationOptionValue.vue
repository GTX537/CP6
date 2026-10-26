<template>
  <label>{{ label }}<input v-if="model.type === 'STRING'" v-model="model.text" :aria-label="label" maxlength="8000" />
    <input v-else-if="model.type === 'DECIMAL'" v-model="model.decimal" :aria-label="label" inputmode="decimal" maxlength="2050" required />
    <select v-else :value="model.boolean == null ? '' : String(model.boolean)" :aria-label="label" required @change="model.boolean = ($event.target as HTMLSelectElement).value === '' ? null : ($event.target as HTMLSelectElement).value === 'true'"><option value="">请选择</option><option value="true">是</option><option value="false">否</option></select>
  </label>
</template>
<script setup lang="ts">
import type { RevisionOption } from '@/api/plm/engineeringRevisions'
defineProps<{ label: string }>()
const model = defineModel<RevisionOption>({ required: true })
</script>
