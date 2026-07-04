<!--
  CpFilterBar —— 列表页查询区（设计系统 §9.2）。
  按 fields 声明渲染一排查询字段（text / select / date / daterange / number，控件观感由 Element Plus overrides 保证），
  右侧提供「展开更多 / 重置 / 查询」。字段超过 4 个时自动折叠，多出的字段隐藏在「展开更多」后。

  Props:
    - fields: FilterField[]              字段声明表（见 FilterField）。
        type:'date'      单日 el-date-picker 透传（独立起/止字段可各自留空做单侧开区间查询）。
        type:'number'    el-input-number 透传，min/max/step 可选；model 值为 number | undefined。
        valueFormat?     date / daterange 透传 el-date-picker value-format（如 'YYYY-MM-DD' → model 为字符串）。
                         opt-in：不传保持 el-date-picker 默认（返回 Date 对象）——不设默认值，避免静默改变既有消费者的返回类型。
    - modelValue: Record<string,unknown> 查询条件对象；键为 field.key，值为该字段当前值。
    - labels?: FilterBarLabels           按钮文案覆盖（search/reset/expand/collapse）；缺省中文，供业务侧接 i18n。
  说明：daterange 字段的 el-date-picker 忽略单个 placeholder，故把 field.placeholder 同时接到
        start-placeholder / end-placeholder（起止用同一串），保证占位提示可见。
  Emits:
    - update:modelValue (next)  任一字段变更/重置时抛出「全新对象」（不原地修改入参）。
    - search                    点击「查询」时抛出（无载荷；父级读自身 model）。
    - reset                     点击「重置」时抛出（清空 model 后一并触发）。

  使用示例：
    <CpFilterBar
      v-model="query"
      :fields="[
        { key:'q',    label:'单号', type:'text', placeholder:'搜索单号' },
        { key:'cust', label:'客户', type:'select', options:[{label:'ASAHI',value:'a'}] },
        { key:'date', label:'日期', type:'daterange' }
      ]"
      @search="load" @reset="load" />
-->
<script lang="ts">
export interface FilterField {
  key: string
  label: string
  type: 'text' | 'select' | 'date' | 'daterange' | 'number'
  options?: { label: string; value: unknown }[]
  placeholder?: string
  /** date / daterange：透传 el-date-picker value-format（opt-in，不传返回 Date 对象） */
  valueFormat?: string
  /** number：透传 el-input-number */
  min?: number
  max?: number
  step?: number
}
// 按钮文案覆盖：缺省中文，业务侧可传 i18n 词条覆盖任一按钮
export interface FilterBarLabels {
  search?: string
  reset?: string
  expand?: string
  collapse?: string
}
</script>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { ElInput, ElSelect, ElOption, ElDatePicker, ElInputNumber, ElButton } from 'element-plus'

const props = defineProps<{
  fields: FilterField[]
  modelValue: Record<string, unknown>
  labels?: FilterBarLabels
}>()
const emit = defineEmits<{
  (e: 'update:modelValue', next: Record<string, unknown>): void
  (e: 'search'): void
  (e: 'reset'): void
}>()

const COLLAPSE_AT = 4
const expanded = ref(false)
const showToggle = computed(() => props.fields.length > COLLAPSE_AT)
const visibleFields = computed(() =>
  expanded.value ? props.fields : props.fields.slice(0, COLLAPSE_AT)
)

// 变更单个字段：始终抛出全新对象，不原地修改 prop
function setField(key: string, value: unknown) {
  emit('update:modelValue', { ...props.modelValue, [key]: value })
}

function onReset() {
  const cleared: Record<string, unknown> = {}
  for (const f of props.fields) cleared[f.key] = undefined
  emit('update:modelValue', cleared)
  emit('reset')
}
</script>

<template>
  <div class="cp-filter">
    <div v-for="f in visibleFields" :key="f.key" class="fld">
      <label>{{ f.label }}</label>

      <el-input
        v-if="f.type === 'text'"
        :model-value="(modelValue[f.key] as string | undefined)"
        :placeholder="f.placeholder"
        clearable
        @update:model-value="setField(f.key, $event)"
      />

      <el-select
        v-else-if="f.type === 'select'"
        :model-value="(modelValue[f.key] as any)"
        :placeholder="f.placeholder"
        clearable
        @update:model-value="setField(f.key, $event)"
      >
        <el-option
          v-for="o in f.options"
          :key="String(o.value)"
          :label="o.label"
          :value="(o.value as any)"
        />
      </el-select>

      <el-date-picker
        v-else-if="f.type === 'date'"
        type="date"
        :model-value="(modelValue[f.key] as any)"
        :placeholder="f.placeholder"
        :value-format="f.valueFormat"
        clearable
        @update:model-value="setField(f.key, $event)"
      />

      <el-date-picker
        v-else-if="f.type === 'daterange'"
        type="daterange"
        :model-value="(modelValue[f.key] as any)"
        :placeholder="f.placeholder"
        :start-placeholder="f.placeholder"
        :end-placeholder="f.placeholder"
        :value-format="f.valueFormat"
        range-separator="→"
        @update:model-value="setField(f.key, $event)"
      />

      <el-input-number
        v-else-if="f.type === 'number'"
        :model-value="(modelValue[f.key] as number | undefined)"
        :min="f.min"
        :max="f.max"
        :step="f.step"
        :placeholder="f.placeholder"
        @update:model-value="setField(f.key, $event)"
      />
    </div>

    <span class="spacer" />

    <div class="fbtns">
      <button
        v-if="showToggle"
        type="button"
        class="link-btn"
        @click="expanded = !expanded"
      >{{ expanded ? (labels?.collapse ?? '收起') : (labels?.expand ?? '展开更多') }} ▾</button>
      <el-button @click="onReset">{{ labels?.reset ?? '重置' }}</el-button>
      <el-button type="primary" @click="emit('search')">{{ labels?.search ?? '查询' }}</el-button>
    </div>
  </div>
</template>

<style scoped>
.cp-filter { display:flex; flex-wrap:wrap; gap:12px; align-items:flex-end;
  background:var(--cp-card); border-radius:var(--cp-r-md); box-shadow:var(--cp-shadow-1);
  padding:14px 18px; }
.fld { display:flex; flex-direction:column; gap:5px; }
.fld label { font-size:var(--cp-fs-2xs); font-weight:800; color:var(--cp-muted); letter-spacing:.5px; }
.cp-filter .spacer { flex:1; }
.fbtns { display:flex; gap:8px; align-items:center; }
.link-btn { font-size:var(--cp-fs-sm); font-weight:800; color:var(--cp-brand-deep);
  background:none; border:none; cursor:pointer; font-family:inherit; padding:8px 4px; }
</style>
