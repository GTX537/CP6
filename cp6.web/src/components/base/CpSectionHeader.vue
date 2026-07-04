<!--
  CpSectionHeader —— 卡片/区块标题栏（设计系统 §11；样式参照 mockup .card-head / .sec-head）。

  Props:
    - title: string  区块标题（14.5px / 800 / ink）。
  Slots:
    - default  标题左侧插槽（如图标），置于 title 之前。
    - extra    标题右侧插槽（如「查看全部 →」链接、操作按钮）。

  使用示例：
    <CpSectionHeader title="最近受注">
      <template #extra><a href="#">查看全部 →</a></template>
    </CpSectionHeader>
-->
<script setup lang="ts">
defineProps<{ title: string }>()
</script>
<template>
  <div class="cp-sec-head">
    <h2 class="cp-sec-head__title"><slot />{{ title }}</h2>
    <div class="cp-sec-head__extra"><slot name="extra" /></div>
  </div>
</template>
<style scoped>
.cp-sec-head { display:flex; align-items:center; justify-content:space-between;
  padding:16px 20px; border-bottom:1px solid var(--cp-line-soft); }
.cp-sec-head__title { font-size:14.5px; font-weight:800; color:var(--cp-ink);
  display:flex; align-items:center; gap:9px; }
.cp-sec-head__extra { display:flex; align-items:center; gap:9px;
  font-size:12px; font-weight:800; color:var(--cp-brand-deep); }
</style>
