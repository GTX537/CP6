<template>
  <div>
    <h3>工程完整性政策</h3>
    <label><input type="checkbox" :checked="!!model" aria-label="包含工程完整性政策" @change="toggle(($event.target as HTMLInputElement).checked)" />本版本同时规定工程完整性与适用范围</label>
    <div v-if="model" class="check-policy">
      <p>仅在此准确规则版本批准后生效。自行制造须具备工程 BOM、制造 BOM 和工艺；不适用需要明确类别与依据。</p>
      <label>准确产品类别<input v-model="model.productCategory" aria-label="政策产品类别" required /></label>
      <label>制造方式<select v-model="model.manufacturing" aria-label="政策制造方式" required><option value="">请选择</option><option value="INTERNAL">内部制造</option><option value="PURCHASED_FINISHED">采购成品</option></select></label>
      <label>配置适用性<select v-model="model.configurationApplicability" aria-label="政策配置适用性" required><option value="">请选择</option><option value="APPLIES">适用</option><option value="NOT_APPLICABLE">不适用</option></select></label>
      <label>政策依据<textarea v-model="model.reason" aria-label="工程政策依据" required /></label>
      <div v-for="row in model.composition" :key="row.kind">
        <h4>{{ names[row.kind] || row.kind }}</h4>
        <label>适用性<select v-model="row.applicability" :aria-label="`${row.kind} 政策适用性`" required><option value="">请选择</option><option value="APPLIES">适用</option><option value="NOT_APPLICABLE">不适用</option></select></label>
        <label>至少需要的组成位置数<input v-model.number="row.minimumOccurrences" type="number" min="0" max="1000" :aria-label="`${row.kind} 最少位置数`" required /></label>
        <label>类别与适用依据<textarea v-model="row.reason" :aria-label="`${row.kind} 政策依据`" required /></label>
      </div>
    </div>
  </div>
</template>
<script setup lang="ts">
import type { EngineeringCheckPolicy } from '@/api/plm/engineeringSourceCheck'
const model = defineModel<EngineeringCheckPolicy | null>({ required: true })
const names: Record<string, string> = { EBOM: '工程 BOM', MBOM: '制造 BOM', ROUTING: '工艺', DOCUMENT: '受控文档', QUALITY_SPEC: '质量规格' }
function toggle(enabled: boolean) {
  model.value = enabled ? { schema: 'engineering-check-policy/1', productCategory: '', manufacturing: '', configurationApplicability: '', reason: '',
    composition: Object.keys(names).map(kind => ({ kind, applicability: '', minimumOccurrences: 0, reason: '' })) } : null
}
</script>
<style scoped>.check-policy { display: grid; gap: 12px; } label { display: grid; gap: 5px; }</style>
