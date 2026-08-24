<script setup lang="ts">
defineProps<{
  imported: boolean
  reviewed: boolean
  coded: boolean
  publishReady: boolean
}>()
</script>

<template>
  <details
    class="studio-checklist"
    data-test="space-studio-checklist"
    aria-label="首次建模四步任务清单"
    open
  >
    <summary>首次建模任务 · 4 步</summary>
    <ol>
      <li :class="{ done: imported }" :aria-label="`导入来源 · ${imported ? '已完成' : '待完成'}`">
        <span aria-hidden="true">{{ imported ? '✓' : '○' }}</span> 导入来源
      </li>
      <li :class="{ done: reviewed }" :aria-label="`复核识别 · ${reviewed ? '已完成' : '待完成'}`">
        <span aria-hidden="true">{{ reviewed ? '✓' : '○' }}</span> 复核识别
      </li>
      <li :class="{ done: coded }" :aria-label="`补齐编码 · ${coded ? '已完成' : '待完成'}`">
        <span aria-hidden="true">{{ coded ? '✓' : '○' }}</span> 补齐编码
      </li>
      <li :class="{ done: publishReady }" :aria-label="`校验发布 · ${publishReady ? '已完成' : '待完成'}`">
        <span aria-hidden="true">{{ publishReady ? '✓' : '○' }}</span> 校验发布
      </li>
    </ol>
  </details>
</template>

<style scoped>
.studio-checklist { position:absolute; top:12px; left:12px; z-index:6; width:230px; padding:10px 12px; border:1px solid var(--space-studio-border); border-radius:7px; color:var(--space-studio-text); background:rgba(15,23,42,.94); box-shadow:0 10px 30px rgba(0,0,0,.25); }
summary { display:flex; align-items:center; min-height:44px; font-size:14px; font-weight:700; cursor:pointer; }
summary:focus-visible { outline:3px solid var(--space-studio-focus); outline-offset:2px; }
ol { margin:10px 0 0; padding-left:24px; font-size:16px; line-height:1.9; }
li span { display:inline-block; width:18px; text-decoration:none; }
li.done { color:var(--space-studio-success); text-decoration:line-through; }
</style>
