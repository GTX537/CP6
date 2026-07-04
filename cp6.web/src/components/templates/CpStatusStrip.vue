<!--
  CpStatusStrip —— 状态速览条（点击即筛选，设计系统 §9.2）。
  横向排列的 pill 卡，每张展示「圆点 + 文字 + 计数」；点击某卡即把该卡 key 回写为当前筛选值。

  Props:
    - items: { key:string; label:string; count:number; tone?:string }[]
        速览项；tone（ok|warn|danger|info|muted）着色圆点与计数，缺省为中性 ink。
    - modelValue: string   当前选中的 key；与之相等的卡获得 `on` 高亮态。
  Emits:
    - update:modelValue (key: string)   点击某卡时抛出该卡 key（供 v-model 双向绑定）。

  使用示例：
    <CpStatusStrip
      v-model="status"
      :items="[
        { key:'all',  label:'全部',  count:28 },
        { key:'wait', label:'未出库', count:9, tone:'warn' },
        { key:'done', label:'已出库', count:12, tone:'ok' }
      ]" />
-->
<script setup lang="ts">
interface StatusItem { key: string; label: string; count: number; tone?: string }

defineProps<{ items: StatusItem[]; modelValue: string }>()
defineEmits<{ (e: 'update:modelValue', key: string): void }>()

const TONE_VAR: Record<string, string> = {
  ok: 'var(--cp-ok)', warn: 'var(--cp-warn)', danger: 'var(--cp-danger)',
  info: 'var(--cp-info)', muted: 'var(--cp-muted)'
}
// tone 缺省或未命中 → 中性 ink；命中 → 对应语义色
const toneColor = (tone?: string) => (tone && TONE_VAR[tone]) || 'var(--cp-ink)'
</script>

<template>
  <div class="cp-status-strip">
    <button
      v-for="it in items"
      :key="it.key"
      type="button"
      class="ss"
      :class="{ on: it.key === modelValue }"
      @click="$emit('update:modelValue', it.key)"
    >
      <span class="dot" :style="{ background: toneColor(it.tone) }" />
      {{ it.label }}
      <b class="num" :style="{ color: toneColor(it.tone) }">{{ it.count }}</b>
    </button>
  </div>
</template>

<style scoped>
.cp-status-strip { display:flex; flex-wrap:wrap; gap:10px; }
.ss { display:flex; align-items:center; gap:10px; background:var(--cp-card);
  border:1px solid var(--cp-line); border-radius:var(--cp-r-md); padding:10px 16px;
  box-shadow:var(--cp-shadow-1); cursor:pointer; transition:var(--cp-t-fast);
  font-family:inherit; font-weight:700; font-size:var(--cp-fs-sm); color:var(--cp-muted); }
.ss b { font-size:19px; font-weight:800; }
.ss:hover { border-color:var(--cp-brand); }
.ss.on { border-color:var(--cp-brand); background:var(--cp-brand-bg); color:var(--cp-brand-deep); }
.ss .dot { width:8px; height:8px; border-radius:50%; }
</style>
