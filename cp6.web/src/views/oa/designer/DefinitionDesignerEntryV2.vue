<script setup lang="ts">
import { defineAsyncComponent, ref } from 'vue'
import LegacyDefinitionsViewV2 from './LegacyDefinitionsViewV2.vue'
const props = defineProps<{ kind: 'FLOW' | 'FORM' }>()
const FlowAuthoringDesignerV2 = defineAsyncComponent(() => import('./FlowAuthoringDesignerV2.vue'))
const FormAuthoringDesignerV2 = defineAsyncComponent(() => import('./FormAuthoringDesignerV2.vue'))
const mode = ref<'authoring' | 'home' | 'legacy'>('authoring')
</script>
<template>
  <FlowAuthoringDesignerV2 v-if="mode === 'authoring' && props.kind === 'FLOW'" @close="mode = 'home'" />
  <FormAuthoringDesignerV2 v-else-if="mode === 'authoring'" @close="mode = 'home'" />
  <LegacyDefinitionsViewV2 v-else-if="mode === 'legacy'" :kind="kind" @close="mode = 'home'" />
  <section v-else class="definition-entry">
    <h2>{{ kind === 'FLOW' ? '流程设计器' : '表单设计器' }}</h2>
    <button @click="mode = 'authoring'">打开草稿工作区</button>
    <button @click="mode = 'legacy'">查看历史来源</button>
    <p>历史定义只读；新建、修改和发布请进入草稿工作区。</p>
  </section>
</template>
<style scoped>
.definition-entry { padding: 24px; } button { margin: 12px 12px 0 0; padding: 10px 16px; }
</style>
