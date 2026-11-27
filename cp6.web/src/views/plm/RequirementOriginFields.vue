<template>
  <div class="origin-fields">
    <label>
      原件 Owner
      <select v-model="origin.reference.owner" :aria-label="label('原件 Owner')" @change="origin.occurrenceId = null">
        <option value="Main.Plm.ControlledDocument.v1">受控文档</option>
        <option value="Main.File.Content.v1">FILE 原件</option>
        <option value="Main.Erp.SalesOrderSource.v1">销售订单原件</option>
        <template v-if="pending">
          <option value="CRM-ACTIVITY/1">CRM 活动</option>
          <option value="OPP/1">CRM 商机</option>
          <option value="ACT-FILE-REFERENCE/1">CRM 活动附件</option>
        </template>
      </select>
    </label>
    <label>原件 ID<input v-model.trim="origin.reference.id" :aria-label="label('原件 ID')" maxlength="36" required /></label>
    <label>原件准确版本<input v-model.trim="origin.reference.version" :aria-label="label('原件准确版本')" pattern="[1-9][0-9]*" required /></label>
    <label>原件 SHA-256<input v-model.trim="origin.reference.digest" :aria-label="label('原件 SHA-256')" pattern="[a-f0-9]{64}" required /></label>
    <label v-if="origin.reference.owner === 'Main.Plm.ControlledDocument.v1'">
      文档内容位置 ID<input v-model.trim="origin.occurrenceId" :aria-label="label('文档内容位置 ID')" maxlength="36" required />
    </label>
  </div>
</template>

<script setup lang="ts">
import type { RequirementOrigin } from '@/api/plm/engineeringRequirements'

const origin = defineModel<RequirementOrigin>({ required: true })
const props = defineProps<{ pending?: boolean; prefix?: string }>()
const label = (name: string) => props.prefix ? `${props.prefix} ${name}` : name
</script>

<style scoped>
.origin-fields { display: grid; grid-template-columns: repeat(auto-fit, minmax(205px, 1fr)); gap: 12px; }
label { display: flex; flex-direction: column; gap: 6px; }
input, select { padding: 8px; font: inherit; min-width: 0; border: 1px solid var(--el-border-color, #c4cbd6); border-radius: 5px; }
</style>
