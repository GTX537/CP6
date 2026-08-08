<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import type { ISpaceDesignSceneDto } from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import {
  DesignScenePreview3D,
  type DesignPreviewPreset,
} from './DesignScenePreview3D'

const props = defineProps<{
  scene: ISpaceDesignSceneDto | null
}>()

const hostRef = ref<HTMLDivElement>()
const canvasRef = ref<HTMLCanvasElement>()
const state = ref<'idle' | 'building' | 'pass' | 'fail' | 'error'>('idle')
const objectCount2d = ref(0)
const objectCount3d = ref(0)
const evidenceHash = ref('')
const errorText = ref('')
let controller: DesignScenePreview3D | null = null
let resizeObserver: ResizeObserver | null = null
let renderVersion = 0

const statusLabel = computed(() => {
  switch (state.value) {
    case 'building':
      return '正在生成同源预览'
    case 'pass':
      return '2D/3D 清单一致'
    case 'fail':
      return '2D/3D 清单不一致'
    case 'error':
      return '3D 预览生成失败'
    default:
      return '等待场景'
  }
})

const statusType = computed(() => {
  if (state.value === 'pass') return 'success'
  if (state.value === 'fail' || state.value === 'error') return 'danger'
  return 'info'
})
const versionLabel = computed(
  () => props.scene?.versionStatus || 'DesignRevision',
)

onMounted(async () => {
  await nextTick()
  if (!canvasRef.value || !hostRef.value) return
  controller = new DesignScenePreview3D(canvasRef.value)
  resizeObserver = new ResizeObserver((entries) => {
    const size = entries[0]?.contentRect
    if (size) controller?.resize(size.width, size.height)
  })
  resizeObserver.observe(hostRef.value)
  if (props.scene) await renderScene(props.scene)
})

watch(
  () => props.scene,
  async (scene) => {
    if (scene && controller) await renderScene(scene)
  },
)

onBeforeUnmount(() => {
  renderVersion++
  resizeObserver?.disconnect()
  controller?.dispose()
  controller = null
})

async function renderScene(scene: ISpaceDesignSceneDto): Promise<void> {
  const version = ++renderVersion
  state.value = 'building'
  errorText.value = ''
  try {
    const evidence = await controller!.setScene(scene)
    if (version !== renderVersion) return
    objectCount2d.value = evidence.editor.objectCount
    objectCount3d.value = evidence.viewer.objectCount
    evidenceHash.value = evidence.consistent ? evidence.editorHash : ''
    errorText.value = evidence.differences.join('；')
    state.value = evidence.consistent ? 'pass' : 'fail'
  } catch (error) {
    if (version !== renderVersion) return
    state.value = 'error'
    evidenceHash.value = ''
    errorText.value = error instanceof Error ? error.message : String(error)
  }
}

function setPreset(preset: DesignPreviewPreset): void {
  controller?.setPreset(preset)
}
</script>

<template>
  <section class="design-preview" aria-label="Design Revision 3D 同源预览">
    <header class="preview-toolbar">
      <div class="preview-status">
        <strong>3D 同源预览</strong>
        <el-tag size="small" :type="statusType">{{ statusLabel }}</el-tag>
        <span v-if="state === 'pass'" class="counts">
          2D {{ objectCount2d }} / 3D {{ objectCount3d }}
        </span>
        <span
          v-if="evidenceHash"
          class="hash"
          :title="`SHA-256 ${evidenceHash}`"
        >
          SHA-256 {{ evidenceHash.slice(0, 12) }}…
        </span>
      </div>
      <el-button-group size="small">
        <el-button @click="setPreset('top')">俯视</el-button>
        <el-button @click="setPreset('iso')">轴测</el-button>
        <el-button @click="setPreset('front')">正视</el-button>
      </el-button-group>
    </header>
    <div ref="hostRef" class="preview-host">
      <canvas ref="canvasRef" class="preview-canvas" />
      <div v-if="state === 'building'" class="preview-overlay">
        正在从当前 Design Revision 构建…
      </div>
      <div v-else-if="state === 'fail' || state === 'error'" class="preview-overlay error">
        {{ errorText || statusLabel }}
      </div>
      <div class="draft-note">
        {{ versionLabel }} 只读预览 · 不含生产库存/任务
      </div>
    </div>
  </section>
</template>

<style scoped>
.design-preview {
  display: flex;
  min-width: 0;
  min-height: 0;
  flex-direction: column;
  background: #0f172a;
}

.preview-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  min-height: 42px;
  padding: 6px 10px;
  color: #e2e8f0;
  background: #111827;
  border-bottom: 1px solid #334155;
  gap: 12px;
}

.preview-status {
  display: flex;
  min-width: 0;
  align-items: center;
  gap: 8px;
  font-size: 12px;
}

.counts,
.hash {
  color: #94a3b8;
  white-space: nowrap;
}

.hash {
  overflow: hidden;
  max-width: 180px;
  text-overflow: ellipsis;
}

.preview-host {
  position: relative;
  min-height: 0;
  flex: 1;
}

.preview-canvas {
  display: block;
  width: 100%;
  height: 100%;
}

.preview-overlay {
  position: absolute;
  inset: 0;
  display: grid;
  place-items: center;
  padding: 24px;
  color: #cbd5e1;
  background: rgba(15, 23, 42, 0.78);
  text-align: center;
}

.preview-overlay.error {
  color: #fecaca;
}

.draft-note {
  position: absolute;
  right: 10px;
  bottom: 8px;
  padding: 3px 7px;
  color: #cbd5e1;
  background: rgba(15, 23, 42, 0.72);
  border-radius: 4px;
  font-size: 11px;
  pointer-events: none;
}

@media (max-width: 900px) {
  .preview-toolbar {
    align-items: flex-start;
    flex-direction: column;
  }

  .preview-status {
    flex-wrap: wrap;
  }
}
</style>
