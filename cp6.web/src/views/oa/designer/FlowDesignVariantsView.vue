<script setup lang="ts">
import { computed, nextTick, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import {
  Aim,
  ArrowDown,
  ArrowLeft,
  ArrowRight,
  Check,
  CircleCheck,
  Close,
  Connection,
  Delete,
  DocumentChecked,
  DocumentCopy,
  Expand,
  FolderOpened,
  Grid,
  List,
  MagicStick,
  MoreFilled,
  Operation,
  Plus,
  Position,
  Promotion,
  RefreshLeft,
  Search,
  Setting,
  Share,
  Stamp,
  Timer,
  Tools,
  User,
  VideoPlay,
  View,
  Warning,
  ZoomIn,
  ZoomOut,
} from '@element-plus/icons-vue'

import FlowDesignInspector from './FlowDesignInspector.vue'
import FlowDesignEdgeInspector from './FlowDesignEdgeInspector.vue'
import FlowDesignPreviewCanvas from './FlowDesignPreviewCanvas.vue'
import {
  FLOW_PREVIEW_DEFINITIONS,
  FLOW_PREVIEW_NODES,
  findPreviewNode,
  type FlowPreviewEditableEdge,
  type FlowPreviewNode,
  type FlowPreviewNodeKind,
  type FlowPreviewSelection,
} from './flowDesignConcept'

type VariantKey = 'professional' | 'focus' | 'control'
type CanvasExpose = {
  addNode: (kind: FlowPreviewNodeKind) => void
  autoLayout: () => void
  deleteSelection: () => void
  fit: () => void
  redo: () => void
  undo: () => void
  updateEdge: (id: string, patch: Partial<FlowPreviewEditableEdge>) => void
  zoomIn: () => void
  zoomOut: () => void
}

const router = useRouter()
const variants: Array<{ key: VariantKey; index: string; label: string }> = [
  { key: 'professional', index: 'A', label: '专业建模台' },
  { key: 'focus', index: 'B', label: '画布专注' },
  { key: 'control', index: 'C', label: '流程控制台' },
]

const activeVariant = ref<VariantKey>('professional')
const selectedFlowId = ref('purchase')
const selectedNodeId = ref('manager')
const selectedNode = ref<FlowPreviewNode>(findPreviewNode('manager'))
const selectedEdge = ref<FlowPreviewEditableEdge | null>(null)
const selectionKind = ref<'node' | 'edge' | 'none'>('node')
const flowKeyword = ref('')
const showGrid = ref(true)
const zoomPercent = ref(76)
const canvasRef = ref<CanvasExpose | null>(null)
const canvasStats = ref({ nodes: FLOW_PREVIEW_NODES.length, edges: 12 })
const historyState = ref({ canUndo: false, canRedo: false })
const focusLibraryOpen = ref(false)
const focusInspectorOpen = ref(true)
const controlLeftTab = ref<'flows' | 'outline'>('outline')
const issuePanelOpen = ref(true)

const selectedEdgeId = computed(() => selectionKind.value === 'edge' ? selectedEdge.value?.id ?? '' : '')
const selectedFlow = computed(() => FLOW_PREVIEW_DEFINITIONS.find(flow => flow.id === selectedFlowId.value) ?? FLOW_PREVIEW_DEFINITIONS[0]!)
const filteredFlows = computed(() => {
  const keyword = flowKeyword.value.trim().toLowerCase()
  if (!keyword) return FLOW_PREVIEW_DEFINITIONS
  return FLOW_PREVIEW_DEFINITIONS.filter(flow =>
    `${flow.code} ${flow.name} ${flow.category}`.toLowerCase().includes(keyword),
  )
})

const palette: Array<{
  kind: FlowPreviewNodeKind
  label: string
  icon: typeof VideoPlay
  tone: string
}> = [
  { kind: 'start', label: '开始', icon: VideoPlay, tone: 'green' },
  { kind: 'approval', label: '审批', icon: User, tone: 'blue' },
  { kind: 'gateway', label: '分支', icon: Share, tone: 'amber' },
  { kind: 'join', label: '汇聚', icon: Connection, tone: 'amber' },
  { kind: 'service', label: '服务', icon: Tools, tone: 'teal' },
  { kind: 'timer', label: '定时', icon: Timer, tone: 'violet' },
  { kind: 'subflow', label: '子流程', icon: DocumentCopy, tone: 'indigo' },
  { kind: 'end', label: '结束', icon: CircleCheck, tone: 'gray' },
]

const outlineNodes = computed(() => FLOW_PREVIEW_NODES.filter(node => node.id !== 'reject'))
const issues = [
  { level: 'error', node: '10', title: '金额条件分流', detail: '缺少默认流出路径' },
  { level: 'warning', node: '23', title: '采购合规会签', detail: '建议配置代理处理人' },
  { level: 'info', node: '31', title: '写入 ERP 请购单', detail: '接口重试策略为 3 次' },
]

function switchVariant(key: VariantKey) {
  activeVariant.value = key
  void nextTick(() => setTimeout(() => canvasRef.value?.fit(), 80))
}

function selectNode(id: string) {
  selectedNodeId.value = id
  selectedNode.value = findPreviewNode(id)
  selectedEdge.value = null
  selectionKind.value = 'node'
  if (activeVariant.value === 'focus') focusInspectorOpen.value = true
}

function handleCanvasSelect(selection: FlowPreviewSelection) {
  selectionKind.value = selection.kind
  if (selection.kind === 'node') {
    selectedNodeId.value = selection.node.id
    selectedNode.value = selection.node
    selectedEdge.value = null
  } else if (selection.kind === 'edge') {
    selectedEdge.value = selection.edge
  } else {
    selectedNodeId.value = ''
    selectedEdge.value = null
  }
  if (activeVariant.value === 'focus') focusInspectorOpen.value = true
}

function updateSelectedEdge(patch: Partial<FlowPreviewEditableEdge>) {
  if (!selectedEdge.value) return
  canvasRef.value?.updateEdge(selectedEdge.value.id, patch)
}

function updateCanvasStats(stats: { nodes: number; edges: number }) {
  canvasStats.value = stats
}

function updateHistoryState(state: { canUndo: boolean; canRedo: boolean }) {
  historyState.value = state
}

function undoCanvas() {
  canvasRef.value?.undo()
}

function redoCanvas() {
  canvasRef.value?.redo()
}

function autoLayoutCanvas() {
  canvasRef.value?.autoLayout()
}

function deleteCanvasSelection() {
  canvasRef.value?.deleteSelection()
}

function addPaletteNode(kind: FlowPreviewNodeKind) {
  canvasRef.value?.addNode(kind)
}

function onPaletteDragStart(event: DragEvent, kind: FlowPreviewNodeKind) {
  event.dataTransfer?.setData('application/cp6-flow-node', kind)
  if (event.dataTransfer) event.dataTransfer.effectAllowed = 'copy'
}

function chooseFlow(id: string) {
  selectedFlowId.value = id
  focusLibraryOpen.value = false
}

function zoomInCanvas() {
  canvasRef.value?.zoomIn()
  zoomPercent.value = Math.min(160, zoomPercent.value + 10)
}

function zoomOutCanvas() {
  canvasRef.value?.zoomOut()
  zoomPercent.value = Math.max(30, zoomPercent.value - 10)
}

function fitCanvas() {
  canvasRef.value?.fit()
  zoomPercent.value = 76
}

function previewAction(message: string) {
  ElMessage.success(message)
}
</script>

<template>
  <div class="flow-design-page">
    <header class="design-topbar">
      <div class="design-brand">
        <span>CP</span>
        <div><strong>流程设计器 · UI 方案</strong><small>演示数据，不写入系统</small></div>
      </div>

      <nav class="variant-nav" aria-label="流程设计器方案">
        <button
          v-for="variant in variants"
          :key="variant.key"
          type="button"
          :class="{ active: activeVariant === variant.key }"
          @click="switchVariant(variant.key)"
        >
          <b>{{ variant.index }}</b><span>{{ variant.label }}</span>
        </button>
      </nav>

      <div class="top-actions">
        <span class="preview-state"><i />概念预览</span>
        <el-button :icon="ArrowLeft" @click="router.push('/oa/designer')">返回流程设计器</el-button>
      </div>
    </header>

    <main class="design-stage">
      <section v-if="activeVariant === 'professional'" class="variant-shell professional-variant">
        <div class="scheme-bar">
          <div>
            <span>方案 A · 推荐</span>
            <h1>专业流程建模台</h1>
            <p>设备采购审批 · {{ selectedFlow.code }} · {{ selectedFlow.version }}</p>
          </div>
          <div class="scheme-actions">
            <span class="save-state"><i />已保存 14:32</span>
            <el-button :icon="View">预览</el-button>
            <el-button :icon="Check" @click="previewAction('流程校验完成：1 项待处理')">校验</el-button>
            <el-button type="primary" :icon="Promotion" @click="previewAction('方案预览不会写入系统')">保存草稿</el-button>
          </div>
        </div>

        <div class="professional-workspace">
          <aside class="flow-library">
            <div class="panel-heading">
              <div><strong>流程库</strong><small>18 个流程 · 3 个草稿</small></div>
              <button type="button" title="新建流程"><el-icon><Plus /></el-icon></button>
            </div>
            <div class="library-search">
              <el-input v-model="flowKeyword" clearable :prefix-icon="Search" placeholder="搜索名称或编号" />
            </div>
            <div class="library-tabs"><button class="active">全部 <b>18</b></button><button>最近</button><button>草稿 <b>3</b></button></div>
            <div class="flow-list">
              <button
                v-for="flow in filteredFlows"
                :key="flow.id"
                type="button"
                :class="{ active: selectedFlowId === flow.id }"
                @click="chooseFlow(flow.id)"
              >
                <span class="flow-status" :class="flow.status" />
                <span><strong>{{ flow.name }}</strong><small>{{ flow.code }} · {{ flow.category }}</small></span>
                <em>{{ flow.version }}</em>
              </button>
            </div>
            <div class="library-footer"><el-icon><FolderOpened /></el-icon><span>归档流程</span><b>24</b></div>
          </aside>

          <aside class="node-palette professional-palette">
            <strong class="rail-label">节点</strong>
            <button
              v-for="item in palette"
              :key="item.kind"
              type="button"
              draggable="true"
              :class="`tone-${item.tone}`"
              :title="`添加${item.label}节点`"
              @click="addPaletteNode(item.kind)"
              @dragstart="onPaletteDragStart($event, item.kind)"
            >
              <el-icon><component :is="item.icon" /></el-icon>
              <small>{{ item.label }}</small>
            </button>
          </aside>

          <section class="canvas-workarea">
            <div class="canvas-commandbar">
              <div class="command-group">
                <button type="button" title="撤销" :disabled="!historyState.canUndo" @click="undoCanvas"><el-icon><RefreshLeft /></el-icon></button>
                <button type="button" title="重做" :disabled="!historyState.canRedo" @click="redoCanvas"><el-icon class="flip"><RefreshLeft /></el-icon></button>
                <i />
                <button type="button" title="自动布局" @click="autoLayoutCanvas"><el-icon><MagicStick /></el-icon></button>
                <button type="button" :class="{ active: showGrid }" title="显示或隐藏网格" @click="showGrid = !showGrid"><el-icon><Grid /></el-icon></button>
              </div>
              <div class="canvas-path"><span>设备采购审批</span><el-icon><ArrowRight /></el-icon><strong>主流程</strong></div>
              <div class="command-group">
                <button type="button" title="缩小" @click="zoomOutCanvas"><el-icon><ZoomOut /></el-icon></button>
                <span class="zoom-value">{{ zoomPercent }}%</span>
                <button type="button" title="放大" @click="zoomInCanvas"><el-icon><ZoomIn /></el-icon></button>
                <button type="button" title="适配画布" @click="fitCanvas"><el-icon><Aim /></el-icon></button>
                <i />
                <button type="button" class="danger" title="删除选中项" @click="deleteCanvasSelection"><el-icon><Delete /></el-icon></button>
              </div>
            </div>
            <div class="canvas-host">
              <FlowDesignPreviewCanvas
                ref="canvasRef"
                :selected-node-id="selectedNodeId"
                :selected-edge-id="selectedEdgeId"
                :show-grid="showGrid"
                show-mini-map
                tone="classic"
                @history="updateHistoryState"
                @select="handleCanvasSelect"
                @stats="updateCanvasStats"
              />
              <div class="canvas-badge"><span><i />设计中</span><b>{{ canvasStats.nodes }} 节点</b><b>{{ canvasStats.edges }} 连线</b></div>
            </div>
          </section>

          <aside class="property-inspector">
            <FlowDesignEdgeInspector v-if="selectionKind === 'edge' && selectedEdge" :edge="selectedEdge" mode="professional" @update="updateSelectedEdge" />
            <FlowDesignInspector v-else-if="selectionKind === 'node'" :node="selectedNode" mode="professional" />
            <div v-else class="empty-inspector"><el-icon><Operation /></el-icon><strong>请选择节点或路径</strong><small>属性将在此处显示</small></div>
          </aside>
        </div>
      </section>

      <section v-else-if="activeVariant === 'focus'" class="variant-shell focus-variant">
        <div class="scheme-bar focus-scheme-bar">
          <div>
            <span>方案 B · 复杂流程</span>
            <h1>画布专注模式</h1>
            <p>{{ selectedFlow.name }} · {{ selectedFlow.code }}</p>
          </div>
          <div class="scheme-actions">
            <el-button :icon="Check" @click="previewAction('已完成流程检查')">检查</el-button>
            <el-button type="primary" :icon="Promotion" @click="previewAction('已保存当前草稿')">保存</el-button>
          </div>
        </div>

        <div class="focus-workspace" :class="{ 'inspector-closed': !focusInspectorOpen }">
          <aside class="focus-toolrail">
            <button type="button" :class="{ active: focusLibraryOpen }" title="打开流程库" @click="focusLibraryOpen = !focusLibraryOpen"><el-icon><FolderOpened /></el-icon></button>
            <i />
            <button
              v-for="item in palette"
              :key="item.kind"
              type="button"
              draggable="true"
              :class="`tone-${item.tone}`"
              :title="`添加${item.label}节点`"
              @click="addPaletteNode(item.kind)"
              @dragstart="onPaletteDragStart($event, item.kind)"
            ><el-icon><component :is="item.icon" /></el-icon></button>
            <span />
            <button type="button" title="设计器设置"><el-icon><Setting /></el-icon></button>
          </aside>

          <section class="focus-canvas">
            <div class="focus-titlebar">
              <button type="button" title="切换流程" @click="focusLibraryOpen = !focusLibraryOpen"><el-icon><ArrowDown /></el-icon></button>
              <span><strong>{{ selectedFlow.name }}</strong><small>{{ selectedFlow.code }} · {{ selectedFlow.version }}</small></span>
              <em><i />草稿已保存</em>
              <div>
                <button type="button" title="撤销" :disabled="!historyState.canUndo" @click="undoCanvas"><el-icon><RefreshLeft /></el-icon></button>
                <button type="button" title="自动布局" @click="autoLayoutCanvas"><el-icon><MagicStick /></el-icon></button>
                <button type="button" :class="{ active: showGrid }" title="显示或隐藏网格" @click="showGrid = !showGrid"><el-icon><Grid /></el-icon></button>
                <button type="button" title="更多操作"><el-icon><MoreFilled /></el-icon></button>
              </div>
            </div>

            <div class="focus-canvas-host">
              <FlowDesignPreviewCanvas
                ref="canvasRef"
                :selected-node-id="selectedNodeId"
                :selected-edge-id="selectedEdgeId"
                :show-grid="showGrid"
                show-mini-map
                tone="focus"
                @history="updateHistoryState"
                @select="handleCanvasSelect"
                @stats="updateCanvasStats"
              />
              <div class="focus-zoom">
                <button type="button" title="缩小" @click="zoomOutCanvas"><el-icon><ZoomOut /></el-icon></button>
                <span>{{ zoomPercent }}%</span>
                <button type="button" title="放大" @click="zoomInCanvas"><el-icon><ZoomIn /></el-icon></button>
                <i />
                <button type="button" title="适配画布" @click="fitCanvas"><el-icon><Aim /></el-icon></button>
              </div>
              <button v-if="!focusInspectorOpen" type="button" class="open-inspector" title="打开属性面板" @click="focusInspectorOpen = true"><el-icon><Operation /></el-icon><span>属性</span></button>
            </div>

            <aside v-if="focusLibraryOpen" class="focus-library-drawer">
              <div class="drawer-heading"><div><strong>切换流程</strong><small>最近使用</small></div><button type="button" title="关闭流程库" @click="focusLibraryOpen = false"><el-icon><Close /></el-icon></button></div>
              <el-input v-model="flowKeyword" clearable :prefix-icon="Search" placeholder="搜索流程" />
              <div class="drawer-flow-list">
                <button v-for="flow in filteredFlows" :key="flow.id" type="button" :class="{ active: selectedFlowId === flow.id }" @click="chooseFlow(flow.id)">
                  <span class="flow-status" :class="flow.status" />
                  <span><strong>{{ flow.name }}</strong><small>{{ flow.code }}</small></span>
                  <em>{{ flow.version }}</em>
                </button>
              </div>
              <el-button type="primary" :icon="Plus">新建流程</el-button>
            </aside>
          </section>

          <aside v-if="focusInspectorOpen" class="focus-inspector">
            <button type="button" class="drawer-close" title="收起属性面板" @click="focusInspectorOpen = false"><el-icon><Close /></el-icon></button>
            <FlowDesignEdgeInspector v-if="selectionKind === 'edge' && selectedEdge" :edge="selectedEdge" mode="focus" @update="updateSelectedEdge" />
            <FlowDesignInspector v-else-if="selectionKind === 'node'" :node="selectedNode" mode="focus" />
            <div v-else class="empty-inspector"><el-icon><Operation /></el-icon><strong>请选择节点或路径</strong><small>属性将在此处显示</small></div>
          </aside>
        </div>
      </section>

      <section v-else class="variant-shell control-variant">
        <div class="scheme-bar control-scheme-bar">
          <div>
            <span>方案 C · 大型流程治理</span>
            <h1>流程控制台</h1>
            <p>{{ selectedFlow.name }} · 发布版本 {{ selectedFlow.version }}</p>
          </div>
          <div class="scheme-actions">
            <span class="version-chip"><el-icon><DocumentChecked /></el-icon>版本 {{ selectedFlow.version }}</span>
            <el-button :icon="View">运行预览</el-button>
            <el-button type="primary" :icon="Promotion" @click="previewAction('发布检查：仍有 1 个错误需要处理')">检查并发布</el-button>
          </div>
        </div>

        <div class="control-workspace">
          <aside class="control-sidebar">
            <div class="control-sidebar-head"><div><strong>流程导航</strong><small>结构与版本</small></div><button type="button" title="更多导航操作"><el-icon><MoreFilled /></el-icon></button></div>
            <div class="control-tabs">
              <button type="button" :class="{ active: controlLeftTab === 'flows' }" @click="controlLeftTab = 'flows'"><el-icon><FolderOpened /></el-icon>流程</button>
              <button type="button" :class="{ active: controlLeftTab === 'outline' }" @click="controlLeftTab = 'outline'"><el-icon><List /></el-icon>节点大纲</button>
            </div>
            <div v-if="controlLeftTab === 'flows'" class="control-flow-list">
              <div class="library-search"><el-input v-model="flowKeyword" clearable :prefix-icon="Search" placeholder="搜索流程" /></div>
              <button v-for="flow in filteredFlows" :key="flow.id" type="button" :class="{ active: selectedFlowId === flow.id }" @click="chooseFlow(flow.id)">
                <span class="flow-status" :class="flow.status" />
                <span><strong>{{ flow.name }}</strong><small>{{ flow.code }} · {{ flow.version }}</small></span>
              </button>
            </div>
            <div v-else class="outline-list">
              <div class="outline-root"><el-icon><ArrowDown /></el-icon><strong>设备采购审批</strong><em>10</em></div>
              <button v-for="node in outlineNodes" :key="node.id" type="button" :class="[{ active: selectionKind === 'node' && selectedNodeId === node.id }, `kind-${node.kind}`]" @click="selectNode(node.id)">
                <i />
                <b>{{ node.code }}</b>
                <span><strong>{{ node.title }}</strong><small>{{ node.assignee }}</small></span>
                <el-icon v-if="node.status === 'warning'" class="outline-warning"><Warning /></el-icon>
                <el-icon v-else class="outline-ok"><CircleCheck /></el-icon>
              </button>
            </div>
            <div class="outline-footer"><span><i />9 已配置</span><span><i />1 待处理</span></div>
          </aside>

          <section class="control-center">
            <div class="control-toolbar">
              <div class="command-group">
                <button type="button" title="撤销" :disabled="!historyState.canUndo" @click="undoCanvas"><el-icon><RefreshLeft /></el-icon></button>
                <button type="button" title="重做" :disabled="!historyState.canRedo" @click="redoCanvas"><el-icon class="flip"><RefreshLeft /></el-icon></button>
                <i />
                <button type="button" title="自动布局" @click="autoLayoutCanvas"><el-icon><MagicStick /></el-icon></button>
                <button type="button" title="定位选中节点"><el-icon><Position /></el-icon></button>
              </div>
              <div class="control-metrics"><span><b>{{ canvasStats.nodes }}</b> 节点</span><span><b>{{ canvasStats.edges }}</b> 连线</span><span class="has-error"><b>1</b> 错误</span></div>
              <div class="command-group">
                <button type="button" title="缩小" @click="zoomOutCanvas"><el-icon><ZoomOut /></el-icon></button>
                <span class="zoom-value">{{ zoomPercent }}%</span>
                <button type="button" title="放大" @click="zoomInCanvas"><el-icon><ZoomIn /></el-icon></button>
                <button type="button" title="适配画布" @click="fitCanvas"><el-icon><Aim /></el-icon></button>
              </div>
            </div>

            <div class="control-canvas-host">
              <FlowDesignPreviewCanvas
                ref="canvasRef"
                :selected-node-id="selectedNodeId"
                :selected-edge-id="selectedEdgeId"
                :show-grid="showGrid"
                tone="control"
                @history="updateHistoryState"
                @select="handleCanvasSelect"
                @stats="updateCanvasStats"
              />
            </div>

            <div v-if="issuePanelOpen" class="issue-console">
              <div class="issue-head">
                <div><button class="active">问题 <b>3</b></button><button>校验历史</button></div>
                <button type="button" title="收起问题面板" @click="issuePanelOpen = false"><el-icon><ArrowDown /></el-icon></button>
              </div>
              <div class="issue-list">
                <button v-for="issue in issues" :key="issue.node" type="button" :class="issue.level" @click="selectNode(issue.node === '10' ? 'split' : issue.node === '23' ? 'compliance' : 'service')">
                  <el-icon><Warning v-if="issue.level !== 'info'" /><CircleCheck v-else /></el-icon>
                  <b>STATE {{ issue.node }}</b><span><strong>{{ issue.title }}</strong><small>{{ issue.detail }}</small></span><el-icon><ArrowRight /></el-icon>
                </button>
              </div>
            </div>
            <button v-else type="button" class="issue-collapsed" @click="issuePanelOpen = true"><el-icon><Warning /></el-icon>问题 <b>3</b><el-icon><Expand /></el-icon></button>
          </section>

          <aside class="control-inspector">
            <FlowDesignEdgeInspector v-if="selectionKind === 'edge' && selectedEdge" :edge="selectedEdge" mode="control" @update="updateSelectedEdge" />
            <FlowDesignInspector v-else-if="selectionKind === 'node'" :node="selectedNode" mode="control" />
            <div v-else class="empty-inspector"><el-icon><Operation /></el-icon><strong>请选择节点或路径</strong><small>属性将在此处显示</small></div>
          </aside>
        </div>
      </section>
    </main>
  </div>
</template>

<style scoped>
.flow-design-page {
  --ink: #2d4850;
  --muted: #7b8f94;
  --line: #dce5e7;
  --soft: #f3f7f8;
  --teal: #139aa0;
  width: 100%;
  height: 100vh;
  min-width: 0;
  overflow: hidden;
  background: #eef4f5;
  color: var(--ink);
  font-family: Nunito, "Microsoft YaHei", sans-serif;
}

button { font-family: inherit; letter-spacing: 0; }

.design-topbar {
  height: 70px;
  padding: 0 20px;
  display: grid;
  grid-template-columns: minmax(270px, 1fr) auto minmax(270px, 1fr);
  align-items: center;
  gap: 18px;
  border-bottom: 1px solid #d9e3e5;
  background: #fff;
}

.design-brand { display: flex; align-items: center; gap: 11px; }
.design-brand > span {
  width: 42px;
  height: 42px;
  display: grid;
  place-items: center;
  border-radius: 8px;
  background: #1db8be;
  color: #fff;
  font-size: 17px;
  font-weight: 900;
}
.design-brand strong,
.design-brand small { display: block; }
.design-brand strong { font-size: 15px; }
.design-brand small { margin-top: 3px; color: #819499; font-size: 9px; }

.variant-nav {
  height: 46px;
  padding: 5px;
  display: flex;
  gap: 3px;
  border-radius: 7px;
  background: #edf2f3;
}
.variant-nav button {
  min-width: 138px;
  padding: 0 14px;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  border: 0;
  border-radius: 5px;
  background: transparent;
  color: #647a80;
  font-size: 11px;
  font-weight: 800;
  cursor: pointer;
}
.variant-nav button b {
  width: 23px;
  height: 23px;
  display: grid;
  place-items: center;
  border-radius: 5px;
  background: #dfe8ea;
  color: #5f757b;
  font-size: 9px;
}
.variant-nav button.active { background: #fff; color: #0b7f85; box-shadow: 0 2px 8px rgb(40 70 78 / 9%); }
.variant-nav button.active b { background: #1aa9af; color: #fff; }

.top-actions { display: flex; align-items: center; justify-content: flex-end; gap: 12px; }
.preview-state { display: flex; align-items: center; gap: 6px; color: #73878c; font-size: 9px; font-weight: 700; }
.preview-state i { width: 7px; height: 7px; border-radius: 50%; background: #2cb47f; box-shadow: 0 0 0 4px #e4f6ee; }

.design-stage { height: calc(100vh - 70px); min-height: 0; }
.variant-shell { height: 100%; min-width: 0; min-height: 0; display: flex; flex-direction: column; }

.scheme-bar {
  min-height: 70px;
  padding: 10px 20px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 24px;
  border-bottom: 1px solid #dbe4e6;
  background: #f7fafa;
}
.scheme-bar > div:first-child > span { color: #118b91; font-size: 9px; font-weight: 800; }
.scheme-bar h1 { margin: 2px 0 0; font-size: 19px; line-height: 1.2; }
.scheme-bar p { margin: 3px 0 0; color: #7c8f94; font-size: 9px; }
.scheme-actions { display: flex; align-items: center; gap: 8px; }
.save-state { margin-right: 5px; display: flex; align-items: center; gap: 6px; color: #6c8580; font-size: 9px; }
.save-state i { width: 6px; height: 6px; border-radius: 50%; background: #2ba779; }

.professional-workspace {
  flex: 1;
  min-height: 0;
  display: grid;
  grid-template-columns: 244px 66px minmax(480px, 1fr) 320px;
  overflow: hidden;
  background: #fff;
}

.flow-library,
.control-sidebar {
  min-width: 0;
  min-height: 0;
  display: flex;
  flex-direction: column;
  border-right: 1px solid var(--line);
  background: #f7fafa;
}
.panel-heading,
.control-sidebar-head {
  min-height: 66px;
  padding: 12px 14px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  border-bottom: 1px solid #e0e7e9;
}
.panel-heading strong,
.panel-heading small,
.control-sidebar-head strong,
.control-sidebar-head small { display: block; }
.panel-heading strong,
.control-sidebar-head strong { font-size: 13px; }
.panel-heading small,
.control-sidebar-head small { margin-top: 3px; color: #829499; font-size: 9px; }
.panel-heading button,
.control-sidebar-head button,
.drawer-heading button {
  width: 30px;
  height: 30px;
  display: grid;
  place-items: center;
  border: 1px solid #d9e3e5;
  border-radius: 5px;
  background: #fff;
  color: #537078;
  cursor: pointer;
}

.library-search { padding: 11px 12px 8px; }
.library-tabs { min-height: 36px; padding: 0 12px; display: flex; gap: 16px; border-bottom: 1px solid #e1e8ea; }
.library-tabs button {
  position: relative;
  padding: 0;
  border: 0;
  background: transparent;
  color: #778b90;
  font-size: 9px;
  font-weight: 700;
  cursor: pointer;
}
.library-tabs button.active { color: #0d898e; }
.library-tabs button.active::after { content: ''; position: absolute; right: 0; bottom: -1px; left: 0; height: 2px; background: #159da3; }
.library-tabs b { margin-left: 2px; padding: 1px 4px; border-radius: 6px; background: #e1eaec; font-size: 7px; }

.flow-list,
.drawer-flow-list,
.control-flow-list { flex: 1; min-height: 0; overflow-y: auto; }
.flow-list { padding: 8px; }
.flow-list > button,
.drawer-flow-list > button,
.control-flow-list > button {
  width: 100%;
  min-height: 57px;
  padding: 8px 9px;
  display: grid;
  grid-template-columns: 8px minmax(0, 1fr) auto;
  align-items: center;
  gap: 9px;
  border: 1px solid transparent;
  border-radius: 5px;
  background: transparent;
  color: #4a6269;
  text-align: left;
  cursor: pointer;
}
.flow-list > button:hover,
.drawer-flow-list > button:hover,
.control-flow-list > button:hover { background: #eef4f5; }
.flow-list > button.active,
.drawer-flow-list > button.active,
.control-flow-list > button.active { border-color: #abdadd; background: #e1f3f3; color: #0b7e84; }
.flow-status { width: 7px; height: 7px; border-radius: 50%; background: #2ca778; }
.flow-status.draft { background: #d5932b; }
.flow-status.warning { background: #d65c55; }
.flow-list strong,
.flow-list small,
.drawer-flow-list strong,
.drawer-flow-list small,
.control-flow-list strong,
.control-flow-list small { display: block; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.flow-list strong,
.drawer-flow-list strong,
.control-flow-list strong { font-size: 10px; }
.flow-list small,
.drawer-flow-list small,
.control-flow-list small { margin-top: 3px; color: #84959a; font-size: 8px; }
.flow-list em,
.drawer-flow-list em { color: #74888d; font-size: 8px; font-style: normal; font-weight: 800; }
.library-footer { min-height: 48px; padding: 0 15px; display: flex; align-items: center; gap: 8px; border-top: 1px solid #dfe7e9; color: #73878c; font-size: 9px; }
.library-footer span { flex: 1; }

.node-palette,
.focus-toolrail {
  min-height: 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  border-right: 1px solid var(--line);
  background: #fff;
}
.professional-palette { padding: 10px 7px; gap: 5px; overflow-y: auto; }
.rail-label { margin: 1px 0 6px; color: #84969a; font-size: 8px; }
.node-palette button {
  width: 50px;
  min-height: 48px;
  padding: 5px 3px;
  display: grid;
  place-items: center;
  gap: 2px;
  border: 1px solid transparent;
  border-radius: 5px;
  background: transparent;
  color: #60757b;
  cursor: grab;
}
.node-palette button:hover { border-color: #d7e2e4; background: #f3f7f8; }
.node-palette button .el-icon { font-size: 17px; }
.node-palette button small { font-size: 8px; }
.tone-green { color: #278960 !important; }
.tone-blue { color: #4679c4 !important; }
.tone-amber { color: #bb7919 !important; }
.tone-teal { color: #118c92 !important; }
.tone-violet { color: #735cb7 !important; }
.tone-indigo { color: #526fae !important; }
.tone-gray { color: #708389 !important; }

.canvas-workarea,
.control-center,
.focus-canvas { min-width: 0; min-height: 0; position: relative; display: flex; flex-direction: column; overflow: hidden; }
.canvas-commandbar,
.control-toolbar {
  min-height: 48px;
  padding: 0 10px;
  display: grid;
  grid-template-columns: 1fr auto 1fr;
  align-items: center;
  gap: 14px;
  border-bottom: 1px solid var(--line);
  background: #fff;
}
.command-group { display: flex; align-items: center; gap: 4px; }
.command-group:last-child { justify-content: flex-end; }
.command-group > i { width: 1px; height: 22px; margin: 0 4px; background: #dce4e6; }
.command-group button,
.focus-titlebar button,
.focus-zoom button {
  width: 30px;
  height: 30px;
  display: grid;
  place-items: center;
  border: 1px solid transparent;
  border-radius: 4px;
  background: transparent;
  color: #60767c;
  cursor: pointer;
}
.command-group button:hover,
.focus-titlebar button:hover,
.focus-zoom button:hover,
.command-group button.active,
.focus-titlebar button.active { border-color: #d5e1e3; background: #eef4f5; color: #0d888e; }
.command-group button:disabled { opacity: .35; cursor: not-allowed; }
.command-group button.danger { color: #c95955; }
.flip { transform: scaleX(-1); }
.canvas-path { display: flex; align-items: center; gap: 5px; color: #788b90; font-size: 9px; white-space: nowrap; }
.canvas-path strong { color: #496169; }
.zoom-value { min-width: 38px; color: #667c82; text-align: center; font-size: 9px; font-weight: 700; }
.canvas-host,
.focus-canvas-host,
.control-canvas-host { position: relative; flex: 1; min-width: 0; min-height: 0; }
.canvas-badge {
  position: absolute;
  bottom: 14px;
  left: 14px;
  height: 30px;
  padding: 0 10px;
  display: flex;
  align-items: center;
  gap: 12px;
  border: 1px solid #d4e0e2;
  border-radius: 5px;
  background: rgb(255 255 255 / 94%);
  color: #73878c;
  font-size: 8px;
  box-shadow: 0 4px 13px rgb(35 66 74 / 8%);
}
.canvas-badge span { display: flex; align-items: center; gap: 5px; color: #2a8062; }
.canvas-badge span i { width: 6px; height: 6px; border-radius: 50%; background: #2cab7b; }
.canvas-badge b { font-weight: 700; }
.property-inspector,
.control-inspector { min-width: 0; min-height: 0; border-left: 1px solid var(--line); overflow: hidden; }

.empty-inspector {
  width: 100%;
  height: 100%;
  display: grid;
  place-items: center;
  align-content: center;
  gap: 7px;
  background: #fff;
  color: #84969b;
}
.empty-inspector .el-icon { font-size: 24px; }
.empty-inspector strong { color: #526970; font-size: 11px; }
.empty-inspector small { font-size: 9px; }

.focus-scheme-bar { background: #f4f7f8; }
.focus-workspace {
  flex: 1;
  min-height: 0;
  display: grid;
  grid-template-columns: 58px minmax(0, 1fr) 306px;
  overflow: hidden;
  background: #fff;
  transition: grid-template-columns .18s;
}
.focus-workspace.inspector-closed { grid-template-columns: 58px minmax(0, 1fr); }
.focus-toolrail { padding: 9px 6px; gap: 5px; background: #243a42; border-right-color: #1c3037; }
.focus-toolrail > i { width: 28px; height: 1px; margin: 3px 0; background: #40545b; }
.focus-toolrail > span { flex: 1; }
.focus-toolrail button {
  width: 42px;
  height: 42px;
  display: grid;
  place-items: center;
  border: 1px solid transparent;
  border-radius: 5px;
  background: transparent;
  color: #b6c5c8 !important;
  font-size: 16px;
  cursor: pointer;
}
.focus-toolrail button:hover,
.focus-toolrail button.active { border-color: #4f666d; background: #334b53; color: #fff !important; }
.focus-titlebar {
  min-height: 52px;
  padding: 0 12px;
  display: flex;
  align-items: center;
  gap: 9px;
  border-bottom: 1px solid #dae4e6;
  background: #fff;
  z-index: 3;
}
.focus-titlebar > span { min-width: 0; }
.focus-titlebar strong,
.focus-titlebar small { display: block; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.focus-titlebar strong { font-size: 11px; }
.focus-titlebar small { margin-top: 2px; color: #829499; font-size: 8px; }
.focus-titlebar em { margin-left: 8px; display: flex; align-items: center; gap: 5px; color: #688179; font-size: 8px; font-style: normal; }
.focus-titlebar em i { width: 6px; height: 6px; border-radius: 50%; background: #2cab79; }
.focus-titlebar > div { margin-left: auto; display: flex; align-items: center; gap: 3px; }
.focus-canvas-host { overflow: hidden; }
.focus-zoom {
  position: absolute;
  bottom: 14px;
  left: 14px;
  z-index: 4;
  height: 38px;
  padding: 3px 5px;
  display: flex;
  align-items: center;
  gap: 3px;
  border: 1px solid #cedbdd;
  border-radius: 6px;
  background: rgb(255 255 255 / 96%);
  box-shadow: 0 6px 18px rgb(34 63 71 / 11%);
}
.focus-zoom span { min-width: 39px; color: #61777d; text-align: center; font-size: 9px; font-weight: 700; }
.focus-zoom i { width: 1px; height: 21px; background: #dce4e6; }
.focus-inspector { position: relative; min-width: 0; min-height: 0; border-left: 1px solid var(--line); overflow: hidden; }
.drawer-close { position: absolute; top: 8px; right: 48px; z-index: 4; width: 28px; height: 28px; display: grid; place-items: center; border: 0; border-radius: 4px; background: transparent; color: #73878c; cursor: pointer; }
.open-inspector {
  position: absolute;
  top: 14px;
  right: 14px;
  z-index: 4;
  height: 34px;
  padding: 0 10px;
  display: flex;
  align-items: center;
  gap: 6px;
  border: 1px solid #cbdadd;
  border-radius: 5px;
  background: #fff;
  color: #4e6870;
  font-size: 9px;
  font-weight: 700;
  cursor: pointer;
}
.focus-library-drawer {
  position: absolute;
  z-index: 6;
  top: 52px;
  bottom: 0;
  left: 0;
  width: 270px;
  padding: 12px;
  display: flex;
  flex-direction: column;
  gap: 10px;
  border-right: 1px solid #ccdadd;
  background: rgb(255 255 255 / 98%);
  box-shadow: 10px 0 28px rgb(34 62 70 / 12%);
}
.drawer-heading { display: flex; align-items: center; justify-content: space-between; gap: 10px; }
.drawer-heading strong,
.drawer-heading small { display: block; }
.drawer-heading strong { font-size: 13px; }
.drawer-heading small { margin-top: 2px; color: #829499; font-size: 8px; }
.drawer-flow-list { margin: 0 -4px; padding: 2px 4px; }

.control-scheme-bar { background: #f5f7fa; }
.control-scheme-bar > div:first-child > span { color: #526fa7; }
.version-chip { height: 31px; padding: 0 10px; display: flex; align-items: center; gap: 6px; border: 1px solid #d6dfea; border-radius: 5px; background: #fff; color: #5e75a1; font-size: 9px; font-weight: 700; }
.control-workspace {
  flex: 1;
  min-height: 0;
  display: grid;
  grid-template-columns: 270px minmax(500px, 1fr) 326px;
  overflow: hidden;
  background: #fff;
}
.control-sidebar { background: #f6f8fa; }
.control-tabs { min-height: 42px; padding: 5px 9px; display: grid; grid-template-columns: 1fr 1fr; gap: 4px; border-bottom: 1px solid #dfe6e9; }
.control-tabs button { display: flex; align-items: center; justify-content: center; gap: 6px; border: 1px solid transparent; border-radius: 4px; background: transparent; color: #71858b; font-size: 9px; font-weight: 700; cursor: pointer; }
.control-tabs button.active { border-color: #ccd8e7; background: #fff; color: #506fa8; }
.control-flow-list { padding: 5px 8px 10px; }
.control-flow-list .library-search { padding-right: 2px; padding-left: 2px; }
.outline-list { flex: 1; min-height: 0; padding: 7px 8px 12px; overflow-y: auto; }
.outline-root { min-height: 35px; padding: 0 7px; display: flex; align-items: center; gap: 6px; color: #536a72; font-size: 10px; }
.outline-root strong { flex: 1; }
.outline-root em { min-width: 20px; padding: 2px 5px; border-radius: 8px; background: #e1e8eb; color: #71838a; text-align: center; font-size: 7px; font-style: normal; }
.outline-list > button {
  position: relative;
  width: 100%;
  min-height: 50px;
  padding: 6px 7px 6px 19px;
  display: grid;
  grid-template-columns: 4px 25px minmax(0, 1fr) 16px;
  align-items: center;
  gap: 7px;
  border: 1px solid transparent;
  border-radius: 5px;
  background: transparent;
  color: #526970;
  text-align: left;
  cursor: pointer;
}
.outline-list > button::before { content: ''; position: absolute; top: -7px; bottom: 0; left: 11px; border-left: 1px solid #cbd7da; }
.outline-list > button > i { width: 4px; height: 24px; z-index: 1; border-radius: 2px; background: #4f7fc3; }
.outline-list > button > b { color: #768990; font-size: 8px; }
.outline-list > button span { min-width: 0; }
.outline-list > button strong,
.outline-list > button small { display: block; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.outline-list > button strong { font-size: 9px; }
.outline-list > button small { margin-top: 3px; color: #839499; font-size: 8px; }
.outline-list > button:hover { background: #edf2f5; }
.outline-list > button.active { border-color: #c0d2e8; background: #e7eef8; color: #456aa3; }
.outline-list > button.kind-start > i,
.outline-list > button.kind-end > i { background: #2a9c71; }
.outline-list > button.kind-gateway > i,
.outline-list > button.kind-join > i { background: #d08b27; }
.outline-list > button.kind-service > i { background: #16979c; }
.outline-warning { color: #d28a21; }
.outline-ok { color: #29926a; }
.outline-footer { min-height: 44px; padding: 0 14px; display: flex; align-items: center; gap: 16px; border-top: 1px solid #dce4e7; color: #72858b; font-size: 8px; }
.outline-footer span { display: flex; align-items: center; gap: 5px; }
.outline-footer i { width: 6px; height: 6px; border-radius: 50%; background: #2ba174; }
.outline-footer span:last-child i { background: #d58b23; }

.control-toolbar { min-height: 46px; background: #fbfcfd; }
.control-metrics { display: flex; align-items: center; gap: 12px; color: #7a8c92; font-size: 8px; white-space: nowrap; }
.control-metrics b { color: #536a72; }
.control-metrics .has-error,
.control-metrics .has-error b { color: #bf534f; }
.control-canvas-host { flex: 1; }
.issue-console { flex: 0 0 154px; min-height: 0; border-top: 1px solid #d8e1e4; background: #fff; }
.issue-head { min-height: 39px; padding: 0 10px 0 14px; display: flex; align-items: center; justify-content: space-between; border-bottom: 1px solid #e2e8ea; }
.issue-head > div { align-self: stretch; display: flex; gap: 16px; }
.issue-head button { border: 0; background: transparent; color: #73878c; font-size: 9px; font-weight: 700; cursor: pointer; }
.issue-head > div button { position: relative; padding: 0; }
.issue-head > div button.active { color: #4d6fa9; }
.issue-head > div button.active::after { content: ''; position: absolute; right: 0; bottom: -1px; left: 0; height: 2px; background: #5578b2; }
.issue-head b { margin-left: 3px; padding: 1px 5px; border-radius: 7px; background: #f7e6e4; color: #bc514d; font-size: 7px; }
.issue-list { height: 114px; overflow-y: auto; }
.issue-list button { width: 100%; min-height: 37px; padding: 5px 13px; display: grid; grid-template-columns: 18px 64px minmax(0, 1fr) 16px; align-items: center; gap: 8px; border: 0; border-bottom: 1px solid #edf0f1; background: #fff; color: #556b72; text-align: left; cursor: pointer; }
.issue-list button:hover { background: #f6f8fa; }
.issue-list button.error > .el-icon { color: #ce5853; }
.issue-list button.warning > .el-icon { color: #cf8922; }
.issue-list button.info > .el-icon { color: #4f75b0; }
.issue-list button > b { color: #768990; font-size: 7px; }
.issue-list strong,
.issue-list small { display: block; }
.issue-list strong { font-size: 9px; }
.issue-list small { margin-top: 2px; color: #84959a; font-size: 8px; }
.issue-collapsed { min-height: 36px; padding: 0 13px; display: flex; align-items: center; gap: 7px; border: 0; border-top: 1px solid #dce4e7; background: #fff; color: #677c82; font-size: 9px; cursor: pointer; }
.issue-collapsed b { color: #bf534f; }
.issue-collapsed .el-icon:last-child { margin-left: auto; }

@media (max-width: 1380px) {
  .professional-workspace { grid-template-columns: 215px 60px minmax(450px, 1fr) 292px; }
  .control-workspace { grid-template-columns: 245px minmax(470px, 1fr) 300px; }
  .variant-nav button { min-width: 120px; }
  .professional-palette { padding-right: 4px; padding-left: 4px; }
  .node-palette button { width: 48px; }
}

@media (max-width: 1120px) {
  .design-topbar { grid-template-columns: auto 1fr auto; }
  .design-brand small,
  .preview-state { display: none; }
  .variant-nav { justify-self: center; }
  .variant-nav button { min-width: 94px; padding: 0 8px; }
  .professional-workspace { grid-template-columns: 60px minmax(430px, 1fr) 285px; }
  .professional-workspace > .flow-library { display: none; }
  .control-workspace { grid-template-columns: 230px minmax(430px, 1fr); }
  .control-inspector { display: none; }
}

@media (max-width: 820px) {
  .design-topbar { height: auto; min-height: 64px; padding: 8px 11px; grid-template-columns: 1fr auto; }
  .variant-nav { grid-column: 1 / -1; grid-row: 2; width: 100%; order: 3; }
  .variant-nav button { flex: 1; min-width: 0; }
  .top-actions .el-button span { display: none; }
  .design-stage { height: calc(100vh - 118px); }
  .scheme-bar { min-height: 64px; padding: 8px 12px; }
  .scheme-bar h1 { font-size: 16px; }
  .scheme-actions .save-state,
  .scheme-actions .version-chip,
  .scheme-actions .el-button:not(:last-child) { display: none; }
  .professional-workspace { grid-template-columns: 54px minmax(0, 1fr); }
  .property-inspector { display: none; }
  .focus-workspace,
  .focus-workspace.inspector-closed { grid-template-columns: 52px minmax(0, 1fr); }
  .focus-inspector { display: none; }
  .focus-toolrail { padding-right: 4px; padding-left: 4px; }
  .focus-toolrail button { width: 40px; }
  .control-workspace { grid-template-columns: minmax(0, 1fr); }
  .control-sidebar { display: none; }
  .canvas-commandbar { grid-template-columns: 1fr auto; }
  .canvas-path { display: none; }
}
</style>
