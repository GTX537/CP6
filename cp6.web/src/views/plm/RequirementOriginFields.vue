<template>
  <div class="origin-fields">
    <label>
      原件 Owner
      <select :value="owner" :aria-label="label('原件 Owner')" @change="selectOwner(($event.target as HTMLSelectElement).value)">
        <option value="Main.Plm.ControlledDocument.v1">受控文档</option>
        <option value="Main.File.Content.v1">FILE 原件</option>
        <option value="Main.Erp.SalesOrderSource.v1">销售订单原件</option>
        <template v-if="pending">
          <option value="CRM-ACTIVITY/1">CRM 活动</option>
          <option value="OPP/1">CRM 商机</option>
        </template>
        <option v-if="pending || owner === actFileOwner" :value="actFileOwner">CRM 活动附件</option>
      </select>
    </label>
    <template v-if="actFile">
      <label v-for="field in actFileFields" :key="field.key">
        {{ field.label }}<input :value="actFile[field.key] ?? ''" :aria-label="label(field.label)" :pattern="field.pattern" :maxlength="field.maxLength"
          @input="setActField(field.key, ($event.target as HTMLInputElement).value)" />
      </label>
    </template>
    <template v-else-if="origin.reference">
      <label>原件 ID<input v-model.trim="origin.reference.id" :aria-label="label('原件 ID')" maxlength="36" required /></label>
      <label>原件准确版本<input v-model.trim="origin.reference.version" :aria-label="label('原件准确版本')" pattern="[1-9][0-9]*" required /></label>
      <label>原件 SHA-256<input v-model.trim="origin.reference.digest" :aria-label="label('原件 SHA-256')" pattern="[a-f0-9]{64}" required /></label>
      <label v-if="origin.reference.owner === 'Main.Plm.ControlledDocument.v1'">
        文档内容位置 ID<input v-model.trim="origin.occurrenceId" :aria-label="label('文档内容位置 ID')" maxlength="36" required />
      </label>
    </template>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import type { RequirementActFileCoordinates, RequirementOrigin } from '@/api/plm/engineeringRequirements'

const origin = defineModel<RequirementOrigin>({ required: true })
const props = defineProps<{ pending?: boolean; prefix?: string }>()
const label = (name: string) => props.prefix ? `${props.prefix} ${name}` : name
const actFileOwner = 'ACT-FILE-REFERENCE/1'
const owner = computed(() => origin.value.actFile ? actFileOwner : origin.value.reference?.owner ?? '')
const actFile = computed<RequirementActFileCoordinates | null>(() => {
  if (origin.value.actFile) return origin.value.actFile
  const legacy = origin.value.reference
  if (legacy?.owner === actFileOwner) return { attachmentId: legacy.id, version: legacy.version, contentSha256: legacy.digest }
  return null
})
const actFileFields: { key: keyof RequirementActFileCoordinates; label: string; pattern?: string; maxLength?: number }[] = [
  { key: 'activityId', label: '活动 ID', maxLength: 36 },
  { key: 'activityRevision', label: '活动准确版本', pattern: '[1-9][0-9]*', maxLength: 19 },
  { key: 'referenceSetVersion', label: '引用集合准确版本', pattern: '[1-9][0-9]*', maxLength: 19 },
  { key: 'referenceId', label: '引用成员 ID', maxLength: 36 },
  { key: 'attachmentId', label: '附件原键' },
  { key: 'version', label: '附件准确版本' },
  { key: 'contentSha256', label: '附件 SHA-256', pattern: '[a-f0-9]{64}', maxLength: 64 },
]
function selectOwner(next: string) {
  if (next === actFileOwner) {
    origin.value = { reference: null, occurrenceId: null, actFile: {} }
    return
  }
  const previous = origin.value.reference
  const reference = previous && previous.owner !== actFileOwner
    ? { ...previous, owner: next }
    : { owner: next, id: '', version: '', digest: '' }
  origin.value = { reference, occurrenceId: null }
}
function setActField(field: keyof RequirementActFileCoordinates, value: string) {
  origin.value = { reference: null, occurrenceId: null, actFile: { ...actFile.value, [field]: value || null } }
}
</script>

<style scoped>
.origin-fields { display: grid; grid-template-columns: repeat(auto-fit, minmax(205px, 1fr)); gap: 12px; }
label { display: flex; flex-direction: column; gap: 6px; }
input, select { padding: 8px; font: inherit; min-width: 0; border: 1px solid var(--el-border-color, #c4cbd6); border-radius: 5px; }
</style>
