<script setup lang="ts">
import type { ApproverRecoveryPolicy } from './designerModel'
defineProps<{ modelValue?: ApproverRecoveryPolicy; roles: { label: string; value: number }[]; stage?: boolean }>()
const emit = defineEmits<{ 'update:modelValue': [value: ApproverRecoveryPolicy | undefined]; loadRoles: [] }>()
const initial = (): ApproverRecoveryPolicy => ({ mode: 'MANUAL_ASSIGNMENT', eligibleRoleIds: [], excludeStarter: true, excludePublisher: true })
</script>
<template>
  <fieldset aria-label="无人审批恢复策略">
    <legend>无人审批时的人工指派</legend>
    <label><input type="checkbox" :checked="!!modelValue" @change="emit('update:modelValue', ($event.target as HTMLInputElement).checked ? initial() : undefined)" />{{ stage ? '单独设置本档策略' : '允许按指定角色恢复审批' }}</label>
    <p v-if="!modelValue">{{ stage ? '沿用节点策略；节点也未配置时，不允许人工指派。' : '无人审批时暂停流程，不能临时指定未授权人员。' }}</p>
    <template v-else>
      <label>允许指派的角色<el-select aria-label="允许指派的角色" multiple :model-value="modelValue.eligibleRoleIds" :multiple-limit="100" @visible-change="(visible: boolean) => visible && emit('loadRoles')" @update:model-value="(value: number[]) => emit('update:modelValue', { ...modelValue!, eligibleRoleIds: value })"><el-option v-for="role in roles" :key="role.value" :value="role.value" :label="role.label" /></el-select></label>
      <p v-if="!modelValue.eligibleRoleIds.length" role="alert">至少选择一个角色后才能发布。</p>
      <label><input type="checkbox" :checked="modelValue.excludeStarter" @change="emit('update:modelValue', { ...modelValue!, excludeStarter: ($event.target as HTMLInputElement).checked })" />排除流程发起人</label>
      <label><input type="checkbox" :checked="modelValue.excludePublisher" @change="emit('update:modelValue', { ...modelValue!, excludePublisher: ($event.target as HTMLInputElement).checked })" />排除流程发布人</label>
      <p>接收人还须符合当前资源权限及业务职责分离要求；指派本身不算审批。</p>
    </template>
  </fieldset>
</template>
