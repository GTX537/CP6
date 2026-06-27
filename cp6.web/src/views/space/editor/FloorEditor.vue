<script setup lang="ts">
import { ref, onMounted, onBeforeUnmount } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { sceneApi } from '@/api/space/scene'
import { useSpaceEditorStore } from '@/stores/spaceEditor'
import { SceneStage } from '@/space-editor/SceneStage'

const { t } = useI18n()
const route = useRoute()
const store = useSpaceEditorStore()

const canvasRef = ref<HTMLDivElement>()
let stageRef: SceneStage | null = null

onMounted(async () => {
  const floorId = route.params['floorId'] as string
  const res = await sceneApi.get(floorId)
  store.load(res.data)
  if (canvasRef.value) {
    stageRef = new SceneStage(canvasRef.value)
    stageRef.render(res.data)
  }
})

onBeforeUnmount(() => {
  stageRef?.destroy()
})
</script>

<template>
  <div class="floor-editor">
    <div class="toolbar">
      <span class="title">{{ t('空间编辑器') }}</span>
      <!-- Phase H: 工具按钮（选择/拖拽/旋转/打点/对齐/撤销重做）-->
    </div>
    <div ref="canvasRef" class="canvas-container" />
    <aside class="side-panel">
      <!-- Phase F: 模板库 -->
      <!-- Phase E-4: 属性面板占位 -->
    </aside>
  </div>
</template>

<style scoped>
.floor-editor {
  display: flex;
  flex-direction: column;
  height: 100vh;
  background: #f5f5f5;
}
.toolbar {
  display: flex;
  align-items: center;
  height: 48px;
  padding: 0 16px;
  background: #fff;
  border-bottom: 1px solid #e0e0e0;
  gap: 8px;
}
.title {
  font-weight: 600;
  font-size: 15px;
}
.canvas-container {
  flex: 1;
  overflow: hidden;
  background: #eaeaea;
}
.side-panel {
  position: fixed;
  right: 0;
  top: 48px;
  bottom: 0;
  width: 260px;
  background: #fff;
  border-left: 1px solid #e0e0e0;
  overflow-y: auto;
}
</style>
