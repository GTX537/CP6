<!--
  CpEmpty —— 空状态占位（居中 muted 图标 + 文案 + 可选操作）。

  Props:
    - text?: string  提示文案；缺省为「暂无数据」。
  Slots:
    - action  文案下方的操作出口（如「新規作成」按钮）。

  使用示例：
    <CpEmpty text="該当するデータがありません">
      <template #action><button>新規作成</button></template>
    </CpEmpty>
-->
<script setup lang="ts">
withDefaults(defineProps<{ text?: string }>(), { text: '暂无数据' })
</script>
<template>
  <div class="cp-empty">
    <svg class="cp-empty__icon" viewBox="0 0 24 24" width="40" height="40"
      fill="none" stroke="currentColor" stroke-width="1.5"
      stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
      <path d="M3 7l2-3h14l2 3M3 7v11a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V7M3 7h18" />
      <path d="M9 11h6" />
    </svg>
    <p class="cp-empty__text">{{ text }}</p>
    <div class="cp-empty__action"><slot name="action" /></div>
  </div>
</template>
<style scoped>
.cp-empty { display:flex; flex-direction:column; align-items:center; justify-content:center;
  gap:12px; padding:48px 20px; text-align:center; color:var(--cp-muted); }
.cp-empty__icon { color:var(--cp-faint); }
.cp-empty__text { font-size:var(--cp-fs-base); font-weight:700; color:var(--cp-muted); }
.cp-empty__action { display:flex; gap:10px; }
</style>
