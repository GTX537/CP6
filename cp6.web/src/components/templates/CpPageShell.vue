<!--
  CpPageShell —— 业务页标准壳（设计系统 §9.2）。
  标题 + 可选计数 pill + 右上操作区，下方为纵向 16px 间距的内容区。

  Props:
    - title: string   页面标题（21px / 800 / ink）。
    - count?: number  可选计数；提供时（含 0）在标题右侧渲染计数 pill。
  Slots:
    - actions  标题栏右上的按钮组出口。
    - default  内容区出口（纵向 flex，子项间距 16px 由父级 gap 提供）。

  使用示例：
    <CpPageShell title="出庫指示一覧" :count="28">
      <template #actions><button>新規</button></template>
      …内容…
    </CpPageShell>
-->
<script setup lang="ts">
defineProps<{ title: string; count?: number }>()
</script>
<template>
  <div class="cp-page">
    <div class="cp-page-head">
      <h1>{{ title }}<span v-if="count !== undefined" class="cnt num">{{ count }}</span></h1>
      <div class="cp-page-actions"><slot name="actions" /></div>
    </div>
    <slot />
  </div>
</template>
<style scoped>
.cp-page { display:flex; flex-direction:column; gap:16px; max-width:1420px; margin:0 auto; }
.cp-page-head { display:flex; align-items:center; justify-content:space-between; gap:14px; }
.cp-page-head h1 { font-size:var(--cp-fs-2xl); font-weight:800; color:var(--cp-ink);
  display:flex; align-items:center; gap:11px; }
.cp-page-head .cnt { font-size:12px; font-weight:800; color:var(--cp-brand-deep);
  background:var(--cp-brand-bg); border-radius:999px; padding:3px 11px; }
.cp-page-actions { display:flex; gap:10px; }
</style>
