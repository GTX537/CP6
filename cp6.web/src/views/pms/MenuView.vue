<template>
  <div v-loading="loading" class="menu-workspace">
    <header class="workspace-heading">
      <div>
        <span class="heading-eyebrow">系统管理 / 菜单结构</span>
        <h1>{{ t('menu.title') }}</h1>
        <p>使用递归树维护任意层级菜单；拖动节点可调整顺序或改变父级。</p>
      </div>
      <div class="heading-actions">
        <span v-if="structureDirty" class="dirty-state"><i />树结构尚未保存</span>
        <el-button :icon="View" @click="previewVisible = true">预览导航</el-button>
        <el-button :icon="Refresh" :loading="loading" @click="refreshData">刷新</el-button>
        <el-button
          v-permission="'menu:edit'"
          type="primary"
          :icon="Check"
          :loading="savingStructure"
          :disabled="!structureDirty"
          @click="saveStructure"
        >
          保存树结构
        </el-button>
      </div>
    </header>

    <section class="menu-editor-frame">
      <aside class="tree-panel">
        <div class="tree-heading">
          <div><strong>MENU 菜单树</strong><small>支持任意层级 · 拖拽调整</small></div>
          <el-button v-permission="'menu:add'" type="primary" :icon="Plus" @click="openCreate(null)">新增顶级</el-button>
        </div>

        <div class="tree-tools">
          <el-input
            v-model="keyword"
            clearable
            :prefix-icon="Search"
            placeholder="搜索名称、菜单 ID、功能键或路由"
          />
          <el-button-group>
            <el-tooltip content="定位当前节点" placement="top">
              <el-button :icon="Position" title="定位当前节点" @click="focusSelection" />
            </el-tooltip>
            <el-tooltip content="全部展开" placement="top">
              <el-button :icon="Expand" title="全部展开" @click="setExpanded(true)" />
            </el-tooltip>
            <el-tooltip content="全部收起" placement="top">
              <el-button :icon="Fold" title="全部收起" @click="setExpanded(false)" />
            </el-tooltip>
          </el-button-group>
        </div>

        <div class="tree-scopes">
          <button type="button" :class="{ active: nodeFilter === 'all' }" @click="nodeFilter = 'all'">
            <span class="scope-dot all" />全部 <b>{{ nodeCount }}</b>
          </button>
          <button type="button" :class="{ active: nodeFilter === 'folder' }" @click="nodeFilter = 'folder'">
            <span class="scope-dot folder" />目录 <b>{{ folderCount }}</b>
          </button>
          <button type="button" :class="{ active: nodeFilter === 'page' }" @click="nodeFilter = 'page'">
            <span class="scope-dot page" />页面 <b>{{ pageCount }}</b>
          </button>
        </div>

        <div class="tree-scroll">
          <el-tree
            v-if="menuTree.length"
            ref="treeRef"
            class="menu-tree"
            :data="menuTree"
            :props="treeProps"
            node-key="menuId"
            highlight-current
            :draggable="canDrag"
            :expand-on-click-node="false"
            :default-expanded-keys="defaultExpandedKeys"
            :current-node-key="selectedId"
            :filter-node-method="filterNode"
            :allow-drop="allowDrop"
            @node-click="handleNodeClick"
            @node-drop="handleNodeDrop"
          >
            <template #default="{ data }">
              <div class="tree-node-row" :title="data.menuName">
                <span class="node-kind" :class="isFolder(data) ? 'folder' : 'page'">
                  <el-icon><FolderOpened v-if="isFolder(data)" /><Document v-else /></el-icon>
                </span>
                <span class="node-copy">
                  <strong>{{ data.menuName }}</strong>
                  <small>{{ nodeIdentity(data) }}</small>
                </span>
                <span v-if="data.children.length" class="child-count">{{ data.children.length }}</span>
                <i class="node-status" :class="{ off: !data.enable }" />
                <span class="node-actions">
                  <el-tooltip content="新增子节点" placement="top">
                    <el-button
                      v-permission="'menu:add'"
                      text
                      circle
                      :icon="Plus"
                      title="新增子节点"
                      @click.stop="openCreate(data)"
                    />
                  </el-tooltip>
                </span>
              </div>
            </template>
          </el-tree>
          <div v-else class="tree-empty">
            <el-icon><Folder /></el-icon>
            <strong>暂无菜单节点</strong>
            <el-button v-permission="'menu:add'" :icon="Plus" @click="openCreate(null)">创建顶级菜单</el-button>
          </div>
        </div>

        <footer class="tree-footer">
          <span v-if="canDrag"><el-icon><Rank /></el-icon>拖动节点可排序或改变父级</span>
          <span v-else-if="isFiltered"><el-icon><InfoFilled /></el-icon>清除搜索和筛选后可拖拽</span>
          <span v-else><el-icon><InfoFilled /></el-icon>当前账号仅可查看菜单结构</span>
          <strong>{{ nodeCount }} 个节点</strong>
        </footer>
      </aside>

      <section v-if="hasEditor" class="detail-panel">
        <header class="detail-head">
          <div class="breadcrumb">
            <span v-for="(item, index) in editorBreadcrumb" :key="`${item.menuId}-${index}`">
              {{ item.menuName }}<el-icon v-if="index < editorBreadcrumb.length - 1"><ArrowRight /></el-icon>
            </span>
          </div>
          <div class="title-row">
            <div class="title-identity">
              <span :class="editorKind === '目录' ? 'folder' : 'page'">
                <el-icon><FolderOpened v-if="editorKind === '目录'" /><Document v-else /></el-icon>
              </span>
              <div>
                <h2>{{ editorForm.menuName || (isCreating ? '新建菜单' : '未命名菜单') }}</h2>
                <p>菜单 ID {{ editorForm.menuId || '待填写' }} · 第 {{ editorBreadcrumb.length }} 层</p>
              </div>
            </div>
            <div class="title-actions">
              <el-tag :type="editorForm.enable ? 'success' : 'info'">{{ editorForm.enable ? t('menu.enabled') : t('menu.disabled') }}</el-tag>
              <el-button v-if="!isCreating" v-permission="'menu:add'" :icon="Plus" @click="selectedNode && openCreate(selectedNode)">新增子节点</el-button>
              <el-dropdown v-if="!isCreating" trigger="click">
                <el-button circle :icon="MoreFilled" title="更多操作" />
                <template #dropdown>
                  <el-dropdown-menu>
                    <el-dropdown-item :icon="Position" @click="focusSelection">在树中定位</el-dropdown-item>
                    <el-dropdown-item v-permission="'menu:delete'" divided :icon="Delete" class="danger-item" @click="deleteSelected">删除节点</el-dropdown-item>
                  </el-dropdown-menu>
                </template>
              </el-dropdown>
            </div>
          </div>
          <div class="context-strip">
            <span><el-icon><Connection /></el-icon>{{ editorBreadcrumb.length }} 层路径</span>
            <span><el-icon><FolderOpened /></el-icon>{{ descendantCount }} 个下级节点</span>
            <span><el-icon><Link /></el-icon>{{ editorForm.routePath || '目录节点，无执行路由' }}</span>
          </div>
        </header>

        <div class="detail-scroll">
          <el-form ref="formRef" :model="editorForm" :rules="formRules" label-position="top">
            <section class="form-section">
              <div class="section-title">
                <span><el-icon><EditPen /></el-icon></span>
                <div><strong>节点信息</strong><small>维护当前菜单节点的标识、路由和导航位置</small></div>
              </div>
              <div class="form-grid">
                <el-form-item :label="t('menu.menuName')" prop="menuName">
                  <el-input v-model="editorForm.menuName" maxlength="100" show-word-limit />
                </el-form-item>
                <el-form-item label="节点类型">
                  <el-segmented v-model="editorKind" :options="['目录', '页面']" />
                </el-form-item>
                <el-form-item :label="t('menu.menuId')" prop="menuId">
                  <el-input-number v-model="editorForm.menuId" :min="1" :disabled="!isCreating" controls-position="right" />
                </el-form-item>
                <el-form-item label="父级菜单">
                  <el-input :model-value="editorParentName" disabled>
                    <template #prefix><el-icon><Folder /></el-icon></template>
                  </el-input>
                </el-form-item>
                <el-form-item label="功能键">
                  <el-input v-model="editorForm.menuKey" maxlength="100" placeholder="例如 menu；用于权限资源键" />
                </el-form-item>
                <el-form-item :label="t('menu.icon')">
                  <el-select
                    v-model="editorForm.icon"
                    filterable
                    allow-create
                    default-first-option
                    clearable
                    placeholder="选择或输入 Element Plus 图标名"
                  >
                    <el-option v-for="icon in iconOptions" :key="icon" :label="icon" :value="icon" />
                  </el-select>
                </el-form-item>
                <el-form-item class="span-two" :label="t('menu.routePath')">
                  <el-input v-model="editorForm.routePath" maxlength="200" placeholder="/module/page；目录节点可留空">
                    <template #prefix><span class="field-prefix">URL</span></template>
                  </el-input>
                  <small>页面路由必须与前端已注册页面一致；目录节点可以留空。</small>
                </el-form-item>
                <el-form-item :label="t('menu.orderNo')">
                  <el-input-number v-model="editorForm.orderNo" :min="0" controls-position="right" />
                </el-form-item>
              </div>
            </section>

            <section class="form-section behavior-section">
              <div class="section-title amber">
                <span><el-icon><Setting /></el-icon></span>
                <div><strong>展示状态</strong><small>控制当前节点是否进入角色菜单和系统导航</small></div>
              </div>
              <div class="behavior-row">
                <span><strong>启用节点</strong><small>停用后，该节点不会出现在用户登录后的导航中。</small></span>
                <el-switch v-model="editorForm.enable" />
              </div>
              <div v-if="descendantCount" class="impact-note">
                <el-icon><InfoFilled /></el-icon>
                <span><strong>层级影响提示</strong><small>移动或删除当前节点时，会同时影响其下 {{ descendantCount }} 个节点。</small></span>
              </div>
            </section>

            <section v-if="!isCreating" class="form-section child-section">
              <div class="child-section-head">
                <div class="section-title">
                  <span><el-icon><Connection /></el-icon></span>
                  <div><strong>直属子节点</strong><small>当前节点下一级菜单，共 {{ selectedNode?.children.length || 0 }} 个</small></div>
                </div>
                <el-button v-permission="'menu:add'" :icon="Plus" @click="selectedNode && openCreate(selectedNode)">添加子节点</el-button>
              </div>
              <div v-if="selectedNode?.children.length" class="child-list">
                <div v-for="(child, index) in selectedNode.children" :key="child.menuId">
                  <span class="node-kind" :class="isFolder(child) ? 'folder' : 'page'">
                    <el-icon><FolderOpened v-if="isFolder(child)" /><Document v-else /></el-icon>
                  </span>
                  <span class="child-copy"><strong>{{ child.menuName }}</strong><small>{{ nodeIdentity(child) }}</small></span>
                  <el-tag size="small" effect="plain">{{ isFolder(child) ? '目录' : '页面' }}</el-tag>
                  <em>顺序 {{ index + 1 }}</em>
                  <el-button text :icon="ArrowRight" @click="focusNode(child)">进入</el-button>
                </div>
              </div>
              <div v-else class="child-empty">
                <el-icon><Folder /></el-icon>
                <strong>暂无子节点</strong>
                <p>这个节点仍然可以继续添加下一级菜单。</p>
                <el-button v-permission="'menu:add'" :icon="Plus" @click="selectedNode && openCreate(selectedNode)">创建第一个子节点</el-button>
              </div>
            </section>
          </el-form>
        </div>

        <footer class="detail-footer">
          <span :class="{ dirty: formDirty }">
            <el-icon><InfoFilled /></el-icon>{{ formDirty ? '当前节点有未保存内容' : '当前节点内容已同步' }}
          </span>
          <div>
            <el-button @click="resetEditor">放弃更改</el-button>
            <el-button
              v-if="isCreating"
              v-permission="'menu:add'"
              type="primary"
              :icon="Check"
              :loading="savingDetails"
              @click="saveDetails"
            >
              创建节点
            </el-button>
            <el-button
              v-else
              v-permission="'menu:edit'"
              type="primary"
              :icon="Check"
              :loading="savingDetails"
              :disabled="!formDirty"
              @click="saveDetails"
            >
              保存节点
            </el-button>
          </div>
        </footer>
      </section>

      <section v-else class="detail-empty">
        <el-icon><FolderOpened /></el-icon>
        <h2>选择一个菜单节点</h2>
        <p>从左侧树中选择节点后，可维护名称、路由、图标和状态。</p>
      </section>
    </section>

    <el-drawer v-model="previewVisible" title="导航预览" size="360px" class="menu-preview-drawer">
      <div class="preview-summary">
        <span><strong>{{ enabledNodeCount }}</strong><small>启用节点</small></span>
        <span><strong>{{ enabledRootCount }}</strong><small>顶级目录</small></span>
      </div>
      <el-tree :data="enabledTree" :props="treeProps" node-key="menuId" default-expand-all class="preview-tree">
        <template #default="{ data }">
          <span class="preview-node"><el-icon><FolderOpened v-if="isFolder(data)" /><Document v-else /></el-icon>{{ data.menuName }}</span>
        </template>
      </el-tree>
    </el-drawer>
  </div>
</template>

<script setup lang="ts">
import { computed, nextTick, onMounted, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import type { FormInstance, FormRules } from 'element-plus'
import {
  ArrowRight,
  Check,
  Connection,
  Delete,
  Document,
  EditPen,
  Expand,
  Fold,
  Folder,
  FolderOpened,
  InfoFilled,
  Link,
  MoreFilled,
  Plus,
  Position,
  Rank,
  Refresh,
  Search,
  Setting,
  View,
} from '@element-plus/icons-vue'
import { menuApi, type MenuItem, type MenuTreePosition } from '@/api/sys/menu'
import { authApi } from '@/api/sys/auth'
import { usePermissionStore } from '@/stores/permission'

interface MenuNode extends MenuItem {
  children: MenuNode[]
}

interface MenuFormModel {
  menuId: number
  menuName: string
  routePath: string
  menuKey: string
  icon: string
  parentId: number | null
  orderNo: number
  enable: boolean
}

type NodeFilter = 'all' | 'folder' | 'page'

const { t } = useI18n()
const permissionStore = usePermissionStore()
const treeRef = ref<any>()
const formRef = ref<FormInstance>()
const menuTree = ref<MenuNode[]>([])
const selectedId = ref<number | null>(null)
const keyword = ref('')
const nodeFilter = ref<NodeFilter>('all')
const loading = ref(false)
const savingStructure = ref(false)
const savingDetails = ref(false)
const structureDirty = ref(false)
const formDirty = ref(false)
const isCreating = ref(false)
const previewVisible = ref(false)
let syncingForm = false

const treeProps = { children: 'children', label: 'menuName' }
const iconOptions = [
  'Folder', 'Document', 'Menu', 'Setting', 'User', 'UserFilled', 'Lock', 'Key',
  'OfficeBuilding', 'Collection', 'Files', 'Tickets', 'Money', 'Box', 'DataLine',
  'ShoppingBag', 'ShoppingCart', 'Notebook', 'MagicStick', 'Odometer',
]

const editorForm = reactive<MenuFormModel>({
  menuId: 0,
  menuName: '',
  routePath: '',
  menuKey: '',
  icon: '',
  parentId: null,
  orderNo: 0,
  enable: true,
})

const formRules: FormRules<MenuFormModel> = {
  menuId: [{ required: true, message: t('menu.menuIdRequired'), trigger: 'change' }],
  menuName: [{ required: true, message: t('menu.menuNameRequired'), trigger: 'blur' }],
}

watch(editorForm, () => {
  if (!syncingForm) formDirty.value = true
}, { deep: true })

watch([keyword, nodeFilter], ([nextKeyword, nextFilter]) => {
  treeRef.value?.filter({ keyword: nextKeyword, type: nextFilter })
})

const canEdit = computed(() => !permissionStore.loaded || permissionStore.has('menu:edit'))
const isFiltered = computed(() => Boolean(keyword.value.trim()) || nodeFilter.value !== 'all')
const canDrag = computed(() => canEdit.value && !isFiltered.value)
const selectedInfo = computed(() => selectedId.value === null ? null : findNode(menuTree.value, selectedId.value))
const selectedNode = computed(() => selectedInfo.value?.node ?? null)
const hasEditor = computed(() => isCreating.value || Boolean(selectedNode.value))
const nodeCount = computed(() => countNodes(menuTree.value))
const folderCount = computed(() => countByKind(menuTree.value, 'folder'))
const pageCount = computed(() => nodeCount.value - folderCount.value)
const descendantCount = computed(() => isCreating.value ? 0 : countNodes(selectedNode.value?.children ?? []))
const defaultExpandedKeys = computed(() => menuTree.value.map(node => node.menuId))
const editorParentName = computed(() => {
  if (editorForm.parentId === null) return '顶级菜单'
  return findNode(menuTree.value, editorForm.parentId)?.node.menuName ?? `菜单 ${editorForm.parentId}`
})
const editorBreadcrumb = computed<MenuNode[]>(() => {
  if (isCreating.value) {
    const parentPath = editorForm.parentId === null ? [] : findNode(menuTree.value, editorForm.parentId)?.path ?? []
    return [...parentPath, { ...toMenuItem(editorForm), children: [] }]
  }
  return selectedInfo.value?.path ?? []
})
const editorKind = computed<'目录' | '页面'>({
  get: () => editorForm.routePath.trim() ? '页面' : '目录',
  set: (value) => {
    if (value === '目录') editorForm.routePath = ''
    else if (!editorForm.routePath.trim()) editorForm.routePath = '/new-page'
  },
})
const enabledTree = computed(() => filterEnabled(menuTree.value))
const enabledNodeCount = computed(() => countNodes(enabledTree.value))
const enabledRootCount = computed(() => enabledTree.value.length)

function toMenuItem(form: MenuFormModel): MenuItem {
  return {
    menuId: form.menuId,
    menuName: form.menuName,
    routePath: form.routePath.trim() || null,
    menuKey: form.menuKey.trim() || null,
    icon: form.icon.trim() || null,
    parentId: form.parentId,
    orderNo: form.orderNo,
    enable: form.enable,
  }
}

function buildTree(items: MenuItem[]): MenuNode[] {
  const nodes = new Map<number, MenuNode>()
  items.forEach(item => nodes.set(item.menuId, { ...item, children: [] }))
  const roots: MenuNode[] = []
  const sorted = [...nodes.values()].sort(compareNodes)

  sorted.forEach(node => {
    const parent = node.parentId === null ? null : nodes.get(node.parentId)
    if (parent && parent.menuId !== node.menuId) parent.children.push(node)
    else roots.push(node)
  })

  const sortChildren = (list: MenuNode[]) => {
    list.sort(compareNodes)
    list.forEach(node => sortChildren(node.children))
  }
  sortChildren(roots)
  return roots
}

function compareNodes(a: MenuNode, b: MenuNode) {
  return a.orderNo - b.orderNo || a.menuId - b.menuId
}

function findNode(
  nodes: MenuNode[],
  menuId: number,
  parent: MenuNode | null = null,
  path: MenuNode[] = [],
): { node: MenuNode; parent: MenuNode | null; path: MenuNode[] } | null {
  for (const node of nodes) {
    const nextPath = [...path, node]
    if (node.menuId === menuId) return { node, parent, path: nextPath }
    const found = findNode(node.children, menuId, node, nextPath)
    if (found) return found
  }
  return null
}

function walkTree(nodes: MenuNode[], visitor: (node: MenuNode) => void) {
  nodes.forEach(node => {
    visitor(node)
    walkTree(node.children, visitor)
  })
}

function countNodes(nodes: MenuNode[]): number {
  return nodes.reduce((total, node) => total + 1 + countNodes(node.children), 0)
}

function countByKind(nodes: MenuNode[], kind: 'folder' | 'page'): number {
  return nodes.reduce((total, node) => total + (isFolder(node) === (kind === 'folder') ? 1 : 0) + countByKind(node.children, kind), 0)
}

function isFolder(node: MenuNode) {
  return node.children.length > 0 || !node.routePath?.trim()
}

function nodeIdentity(node: MenuNode) {
  return node.menuKey || node.routePath || `ID ${node.menuId}`
}

function filterNode(value: { keyword: string; type: NodeFilter } | undefined, data: MenuNode) {
  if (!value) return true
  const search = value.keyword.trim().toLowerCase()
  const haystack = `${data.menuName} ${data.menuId} ${data.menuKey || ''} ${data.routePath || ''}`.toLowerCase()
  const matchesKeyword = !search || haystack.includes(search)
  const matchesType = value.type === 'all' || (value.type === 'folder' ? isFolder(data) : !isFolder(data))
  return matchesKeyword && matchesType
}

function filterEnabled(nodes: MenuNode[]): MenuNode[] {
  return nodes
    .filter(node => node.enable)
    .map(node => ({ ...node, children: filterEnabled(node.children) }))
}

function assignEditor(node: MenuItem) {
  syncingForm = true
  Object.assign(editorForm, {
    menuId: node.menuId,
    menuName: node.menuName,
    routePath: node.routePath || '',
    menuKey: node.menuKey || '',
    icon: node.icon || '',
    parentId: node.parentId,
    orderNo: node.orderNo,
    enable: node.enable,
  })
  formDirty.value = false
  nextTick(() => {
    syncingForm = false
    formRef.value?.clearValidate()
  })
}

function beginEdit(node: MenuNode) {
  isCreating.value = false
  selectedId.value = node.menuId
  assignEditor(node)
  nextTick(() => treeRef.value?.setCurrentKey(node.menuId))
}

async function confirmDiscardForm() {
  if (!formDirty.value) return true
  try {
    await ElMessageBox.confirm('当前节点有未保存内容，继续后这些内容会丢失。', '未保存更改', {
      type: 'warning',
      confirmButtonText: '继续',
      cancelButtonText: '返回编辑',
    })
    return true
  } catch {
    return false
  }
}

async function loadData(preferredId?: number | null) {
  loading.value = true
  try {
    const items = await menuApi.getAll()
    menuTree.value = buildTree(items)
    structureDirty.value = false
    const nextId = preferredId ?? selectedId.value
    const nextNode = nextId === null ? null : findNode(menuTree.value, nextId)?.node
    const firstNode = menuTree.value[0] ?? null
    if (nextNode || firstNode) beginEdit(nextNode ?? firstNode!)
    else {
      selectedId.value = null
      isCreating.value = false
      formDirty.value = false
    }
  } finally {
    loading.value = false
  }
}

async function refreshData() {
  if (structureDirty.value || formDirty.value) {
    try {
      await ElMessageBox.confirm('刷新会放弃尚未保存的树结构和节点内容。', '确认刷新', { type: 'warning' })
    } catch {
      return
    }
  }
  await loadData(selectedId.value)
}

async function handleNodeClick(data: MenuNode) {
  if (!isCreating.value && data.menuId === selectedId.value) return
  const previousId = selectedId.value
  if (!await confirmDiscardForm()) {
    nextTick(() => treeRef.value?.setCurrentKey(previousId))
    return
  }
  beginEdit(data)
}

async function openCreate(parent: MenuNode | null) {
  if (structureDirty.value) {
    ElMessage.warning('请先保存树结构，再新增菜单节点。')
    return
  }
  if (!await confirmDiscardForm()) return
  const ids: number[] = []
  walkTree(menuTree.value, node => ids.push(node.menuId))
  isCreating.value = true
  assignEditor({
    menuId: Math.max(0, ...ids) + 1,
    menuName: parent ? '新建子菜单' : '新建顶级菜单',
    routePath: parent ? '/new-page' : null,
    menuKey: null,
    icon: parent ? 'Document' : 'Folder',
    parentId: parent?.menuId ?? null,
    orderNo: parent ? parent.children.length : menuTree.value.length,
    enable: true,
  })
  formDirty.value = true
  nextTick(() => formRef.value?.clearValidate())
}

function resetEditor() {
  if (isCreating.value) {
    isCreating.value = false
    const node = selectedNode.value ?? menuTree.value[0]
    if (node) beginEdit(node)
    return
  }
  if (selectedNode.value) assignEditor(selectedNode.value)
}

async function saveDetails() {
  if (!formRef.value) return
  if (structureDirty.value) {
    ElMessage.warning('请先保存树结构，再保存节点内容。')
    return
  }
  await formRef.value.validate()
  editorForm.menuName = editorForm.menuName.trim()

  if (isCreating.value && findNode(menuTree.value, editorForm.menuId)) {
    ElMessage.error(`菜单 ID ${editorForm.menuId} 已存在`)
    return
  }

  savingDetails.value = true
  try {
    const payload = toMenuItem(editorForm)
    if (isCreating.value) {
      await menuApi.add(payload)
      ElMessage.success(t('table.addSuccess'))
    } else {
      await menuApi.update(payload)
      ElMessage.success(t('table.editSuccess'))
    }
    formDirty.value = false
    isCreating.value = false
    await loadData(payload.menuId)
    await refreshActiveNavigation()
  } finally {
    savingDetails.value = false
  }
}

function collectPositions(nodes: MenuNode[], parentId: number | null = null, output: MenuTreePosition[] = []) {
  nodes.forEach((node, index) => {
    node.parentId = parentId
    node.orderNo = index
    output.push({ menuId: node.menuId, parentId, orderNo: index })
    collectPositions(node.children, node.menuId, output)
  })
  return output
}

function handleNodeDrop() {
  const positions = collectPositions(menuTree.value)
  const selectedPosition = selectedId.value === null ? null : positions.find(item => item.menuId === selectedId.value)
  if (selectedPosition) {
    syncingForm = true
    editorForm.parentId = selectedPosition.parentId
    editorForm.orderNo = selectedPosition.orderNo
    nextTick(() => { syncingForm = false })
  }
  structureDirty.value = true
  ElMessage.success('树结构已调整，保存后生效')
}

function allowDrop() {
  return true
}

async function saveStructure() {
  if (!structureDirty.value) return
  savingStructure.value = true
  try {
    await menuApi.updateTree(collectPositions(menuTree.value))
    structureDirty.value = false
    await refreshActiveNavigation()
    ElMessage.success('菜单树结构已保存')
  } finally {
    savingStructure.value = false
  }
}

async function deleteSelected() {
  const node = selectedNode.value
  if (!node) return
  if (structureDirty.value) {
    ElMessage.warning('请先保存或刷新树结构，再删除节点。')
    return
  }
  if (!await confirmDiscardForm()) return

  const ids: number[] = []
  walkTree([node], item => ids.push(item.menuId))
  const childText = ids.length > 1 ? `及其 ${ids.length - 1} 个下级节点` : ''
  await ElMessageBox.confirm(`确定删除“${node.menuName}”${childText}吗？此操作不可恢复。`, t('table.tip'), {
    type: 'warning',
    confirmButtonText: '删除',
    confirmButtonClass: 'el-button--danger',
  })
  const parentId = selectedInfo.value?.parent?.menuId ?? null
  await menuApi.del(ids)
  ElMessage.success(t('table.deleteSuccess'))
  await loadData(parentId)
  await refreshActiveNavigation()
}

function setExpanded(expanded: boolean) {
  walkTree(menuTree.value, node => {
    const treeNode = treeRef.value?.getNode(node.menuId)
    if (treeNode) treeNode.expanded = expanded
  })
}

function focusSelection() {
  if (selectedId.value === null) return
  const info = findNode(menuTree.value, selectedId.value)
  if (!info) return
  setExpanded(false)
  nextTick(() => {
    info.path.slice(0, -1).forEach(item => {
      const node = treeRef.value?.getNode(item.menuId)
      if (node) node.expanded = true
    })
    treeRef.value?.setCurrentKey(selectedId.value)
    const scroller = document.querySelector<HTMLElement>('.menu-workspace .tree-scroll')
    const current = document.querySelector<HTMLElement>('.menu-workspace .menu-tree .is-current')
    if (scroller && current) {
      const scrollRect = scroller.getBoundingClientRect()
      const currentRect = current.getBoundingClientRect()
      scroller.scrollTo({
        top: scroller.scrollTop + currentRect.top - scrollRect.top - scrollRect.height / 2 + currentRect.height / 2,
        behavior: 'smooth',
      })
    }
  })
}

async function focusNode(node: MenuNode) {
  if (!await confirmDiscardForm()) return
  beginEdit(node)
  focusSelection()
}

async function refreshActiveNavigation() {
  try {
    const profile: any = await authApi.profile()
    localStorage.setItem('menus', JSON.stringify(profile.menus || []))
    window.dispatchEvent(new Event('cp6-menu-updated'))
  } catch {
    // 菜单保存已经成功；导航画像失败时由下次登录重新加载。
  }
}

onMounted(() => loadData())
</script>

<style scoped>
.menu-workspace {
  min-height: 100%;
  max-width: 1600px;
  margin: 0 auto;
  color: var(--cp-ink);
}

.workspace-heading {
  min-height: 92px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 20px;
  padding: 0 0 16px;
}
.heading-eyebrow {
  color: var(--cp-brand-deep);
  font-size: 11px;
  font-weight: 800;
}
.workspace-heading h1 {
  margin: 5px 0 0;
  font-size: 26px;
  letter-spacing: 0;
}
.workspace-heading p {
  margin: 5px 0 0;
  color: var(--cp-muted);
  font-size: 12px;
}
.heading-actions {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 9px;
  flex-wrap: wrap;
}
.dirty-state {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-right: 4px;
  color: #a76f18;
  font-size: 11px;
}
.dirty-state i {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: #e7a32e;
  box-shadow: 0 0 0 4px #fff0d2;
}

.menu-editor-frame {
  height: calc(100dvh - 190px);
  min-height: 650px;
  max-height: 880px;
  display: grid;
  grid-template-columns: minmax(360px, 38%) minmax(570px, 1fr);
  overflow: hidden;
  border: 1px solid var(--cp-line);
  border-radius: 8px;
  background: var(--cp-card);
  box-shadow: var(--cp-shadow-1);
}

.tree-panel,
.detail-panel {
  min-width: 0;
  min-height: 0;
  display: flex;
  flex-direction: column;
}
.tree-panel {
  overflow: hidden;
  border-right: 1px solid var(--cp-line);
  background: rgba(248, 251, 251, .92);
}
.tree-heading {
  min-height: 76px;
  padding: 15px 18px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 14px;
  border-bottom: 1px solid var(--cp-line);
}
.tree-heading strong,
.tree-heading small {
  display: block;
}
.tree-heading strong {
  font-size: 14px;
}
.tree-heading small {
  margin-top: 4px;
  color: var(--cp-muted);
  font-size: 10px;
}
.tree-tools {
  padding: 13px 15px;
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: 8px;
  border-bottom: 1px solid var(--cp-line);
  background: var(--cp-card);
}
.tree-scopes {
  min-height: 43px;
  padding: 6px 15px;
  display: flex;
  align-items: center;
  gap: 5px;
  overflow-x: auto;
  border-bottom: 1px solid var(--cp-line);
  background: var(--cp-card);
}
.tree-scopes button {
  height: 29px;
  padding: 0 9px;
  display: flex;
  align-items: center;
  gap: 5px;
  flex: 0 0 auto;
  border: 1px solid transparent;
  border-radius: 4px;
  background: transparent;
  color: var(--cp-text);
  font-size: 10px;
  cursor: pointer;
}
.tree-scopes button:hover {
  background: var(--cp-bg-hover);
}
.tree-scopes button.active {
  border-color: rgba(20, 184, 196, .28);
  background: var(--cp-brand-bg);
  color: var(--cp-brand-deep);
}
.tree-scopes button b {
  color: var(--cp-muted);
  font-size: 9px;
}
.scope-dot {
  width: 7px;
  height: 7px;
  border-radius: 2px;
}
.scope-dot.all { background: #789095; }
.scope-dot.folder { background: #e1a23c; }
.scope-dot.page { background: #1baeb5; }

.tree-scroll {
  flex: 1;
  min-height: 0;
  padding: 10px 9px 18px;
  overflow: auto;
  overscroll-behavior: contain;
  scrollbar-gutter: stable;
}
.menu-tree {
  background: transparent;
  color: var(--cp-text);
  --el-tree-node-hover-bg-color: var(--cp-bg-hover);
}
.menu-tree :deep(.el-tree-node__content) {
  min-height: 48px;
  height: auto;
  margin: 2px 0;
  padding-right: 5px;
  border: 1px solid transparent;
  border-radius: 5px;
}
.menu-tree :deep(.el-tree-node__content:hover) {
  border-color: var(--cp-line);
}
.menu-tree :deep(.el-tree-node.is-current > .el-tree-node__content) {
  border-color: rgba(20, 184, 196, .32);
  background: var(--cp-brand-bg);
  color: var(--cp-brand-deep);
}
.menu-tree :deep(.el-tree-node__expand-icon) {
  color: var(--cp-muted);
  font-size: 13px;
}
.menu-tree :deep(.el-tree-node__children) {
  position: relative;
}
.menu-tree :deep(.el-tree-node__children::before) {
  position: absolute;
  top: 0;
  bottom: 6px;
  left: 8px;
  border-left: 1px dashed #cad8da;
  content: '';
}
.tree-node-row {
  width: calc(100% - 3px);
  min-width: 0;
  display: flex;
  align-items: center;
  gap: 8px;
}
.node-kind {
  width: 30px;
  height: 30px;
  display: grid;
  place-items: center;
  flex: 0 0 30px;
  border-radius: 5px;
  font-size: 15px;
}
.node-kind.folder {
  color: #b87915;
  background: #fff0d5;
}
.node-kind.page {
  color: var(--cp-brand-deep);
  background: var(--cp-brand-bg);
}
.node-copy {
  min-width: 0;
  flex: 1;
}
.node-copy strong,
.node-copy small {
  display: block;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.node-copy strong {
  font-size: 12px;
}
.node-copy small {
  margin-top: 3px;
  color: var(--cp-muted);
  font: 9px Consolas, monospace;
}
.child-count {
  min-width: 20px;
  padding: 2px 5px;
  border-radius: 9px;
  background: #e4ecee;
  color: #6f8287;
  text-align: center;
  font-size: 8px;
}
.node-status {
  width: 7px;
  height: 7px;
  flex: 0 0 7px;
  border-radius: 50%;
  background: var(--cp-ok);
}
.node-status.off {
  background: #b9c4c6;
}
.node-actions {
  display: flex;
  opacity: 0;
  transition: opacity .15s;
}
.tree-node-row:hover .node-actions,
.menu-tree :deep(.is-current) > .el-tree-node__content .node-actions {
  opacity: 1;
}
.node-actions .el-button {
  width: 25px;
  height: 25px;
  margin: 0;
}
.tree-footer {
  min-height: 48px;
  padding: 10px 15px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  border-top: 1px solid var(--cp-line);
  background: var(--cp-card);
  color: var(--cp-muted);
  font-size: 9px;
}
.tree-footer span {
  display: flex;
  align-items: center;
  gap: 6px;
}
.tree-footer strong {
  flex: 0 0 auto;
  color: var(--cp-text);
}
.tree-empty {
  min-height: 260px;
  display: grid;
  place-items: center;
  align-content: center;
  gap: 10px;
  color: var(--cp-muted);
}
.tree-empty > .el-icon {
  font-size: 34px;
}

.detail-panel {
  overflow: hidden;
  background: var(--cp-card);
}
.detail-head {
  padding: 14px 24px 17px;
  border-bottom: 1px solid var(--cp-line);
}
.breadcrumb {
  min-height: 25px;
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 4px;
  color: var(--cp-muted);
  font-size: 10px;
}
.breadcrumb span {
  display: flex;
  align-items: center;
  gap: 4px;
}
.breadcrumb span:last-child {
  color: var(--cp-brand-deep);
  font-weight: 700;
}
.title-row {
  min-height: 51px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 18px;
}
.title-identity {
  min-width: 0;
  display: flex;
  align-items: center;
  gap: 11px;
}
.title-identity > span {
  width: 42px;
  height: 42px;
  display: grid;
  place-items: center;
  flex: 0 0 42px;
  border-radius: 6px;
  font-size: 20px;
}
.title-identity > span.folder {
  color: #b47716;
  background: #fff0d5;
}
.title-identity > span.page {
  color: var(--cp-brand-deep);
  background: var(--cp-brand-bg);
}
.title-identity h2 {
  margin: 0;
  overflow-wrap: anywhere;
  font-size: 18px;
}
.title-identity p {
  margin: 4px 0 0;
  color: var(--cp-muted);
  font: 9px Consolas, monospace;
}
.title-actions {
  display: flex;
  align-items: center;
  gap: 8px;
  flex: 0 0 auto;
}
.context-strip {
  min-height: 34px;
  margin-top: 12px;
  padding: 7px 10px;
  display: flex;
  align-items: center;
  gap: 18px;
  overflow: hidden;
  border: 1px solid var(--cp-line);
  border-radius: 4px;
  background: var(--cp-bg-hover);
  color: var(--cp-text);
  font-size: 10px;
}
.context-strip span {
  min-width: 0;
  display: flex;
  align-items: center;
  gap: 5px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.context-strip .el-icon {
  flex: 0 0 auto;
  color: var(--cp-brand-deep);
}
.detail-scroll {
  flex: 1;
  min-height: 0;
  overflow: auto;
  overscroll-behavior: contain;
  scrollbar-gutter: stable;
}
.form-section {
  padding: 21px 24px 23px;
  border-bottom: 1px solid var(--cp-line);
}
.section-title {
  display: flex;
  align-items: center;
  gap: 9px;
  margin-bottom: 17px;
}
.section-title > span {
  width: 32px;
  height: 32px;
  display: grid;
  place-items: center;
  flex: 0 0 32px;
  border-radius: 5px;
  color: var(--cp-brand-deep);
  background: var(--cp-brand-bg);
}
.section-title.amber > span {
  color: #b57612;
  background: #fff0d6;
}
.section-title strong,
.section-title small {
  display: block;
}
.section-title strong {
  font-size: 13px;
}
.section-title small {
  margin-top: 3px;
  color: var(--cp-muted);
  font-size: 10px;
}
.form-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 2px 18px;
}
.form-grid .span-two {
  grid-column: 1 / -1;
}
.form-grid :deep(.el-form-item__label) {
  color: var(--cp-text);
  font-size: 11px;
  font-weight: 700;
}
.form-grid :deep(.el-select),
.form-grid :deep(.el-segmented),
.form-grid :deep(.el-input-number) {
  width: 100%;
}
.form-grid small {
  display: block;
  width: 100%;
  margin-top: 5px;
  color: var(--cp-muted);
  font-size: 9px;
}
.field-prefix {
  color: var(--cp-muted);
  font: 800 8px Consolas, monospace;
}
.behavior-row {
  min-height: 64px;
  padding: 10px 13px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  border: 1px solid var(--cp-line);
  border-radius: 5px;
}
.behavior-row strong,
.behavior-row small {
  display: block;
}
.behavior-row strong {
  font-size: 11px;
}
.behavior-row small {
  margin-top: 4px;
  color: var(--cp-muted);
  font-size: 9px;
}
.impact-note {
  margin-top: 10px;
  padding: 10px 12px;
  display: flex;
  align-items: center;
  gap: 8px;
  border: 1px solid #ead8b5;
  border-radius: 4px;
  background: #fff9ee;
  color: #a26b12;
}
.impact-note strong,
.impact-note small {
  display: block;
}
.impact-note strong {
  font-size: 10px;
}
.impact-note small {
  margin-top: 3px;
  color: #8b795d;
  font-size: 9px;
}
.child-section-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 15px;
}
.child-section-head .section-title {
  margin-bottom: 14px;
}
.child-list {
  overflow: hidden;
  border: 1px solid var(--cp-line);
  border-radius: 5px;
}
.child-list > div {
  min-height: 56px;
  padding: 7px 11px;
  display: grid;
  grid-template-columns: 32px minmax(150px, 1fr) 55px 65px 52px;
  align-items: center;
  gap: 8px;
  border-bottom: 1px solid var(--cp-line);
}
.child-list > div:last-child {
  border-bottom: 0;
}
.child-copy {
  min-width: 0;
}
.child-copy strong,
.child-copy small {
  display: block;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.child-copy strong {
  font-size: 11px;
}
.child-copy small {
  margin-top: 3px;
  color: var(--cp-muted);
  font: 9px Consolas, monospace;
}
.child-list em {
  color: var(--cp-muted);
  font-size: 9px;
  font-style: normal;
}
.child-empty {
  min-height: 170px;
  display: grid;
  place-items: center;
  align-content: center;
  gap: 6px;
  border: 1px dashed #c7d7da;
  border-radius: 5px;
  color: var(--cp-muted);
  text-align: center;
}
.child-empty > .el-icon {
  font-size: 25px;
}
.child-empty strong {
  color: var(--cp-text);
  font-size: 11px;
}
.child-empty p {
  margin: 0 0 6px;
  font-size: 9px;
}
.detail-footer {
  min-height: 66px;
  padding: 11px 24px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 18px;
  border-top: 1px solid var(--cp-line);
  background: #fafcfc;
}
.detail-footer > span {
  display: flex;
  align-items: center;
  gap: 6px;
  color: var(--cp-ok);
  font-size: 10px;
}
.detail-footer > span.dirty {
  color: #a76f18;
}
.detail-footer > div {
  display: flex;
  gap: 8px;
}
.detail-empty {
  display: grid;
  place-items: center;
  align-content: center;
  color: var(--cp-muted);
  text-align: center;
}
.detail-empty > .el-icon {
  font-size: 42px;
}
.detail-empty h2 {
  margin: 14px 0 5px;
  color: var(--cp-text);
  font-size: 18px;
}
.detail-empty p {
  margin: 0;
  font-size: 11px;
}
.danger-item {
  color: var(--cp-danger);
}

.preview-summary {
  display: grid;
  grid-template-columns: 1fr 1fr;
  margin-bottom: 18px;
  border: 1px solid var(--cp-line);
  border-radius: 6px;
}
.preview-summary span {
  min-height: 70px;
  display: grid;
  place-items: center;
  align-content: center;
}
.preview-summary span + span {
  border-left: 1px solid var(--cp-line);
}
.preview-summary strong,
.preview-summary small {
  display: block;
}
.preview-summary strong {
  color: var(--cp-brand-deep);
  font-size: 20px;
}
.preview-summary small {
  margin-top: 3px;
  color: var(--cp-muted);
  font-size: 10px;
}
.preview-tree :deep(.el-tree-node__content) {
  min-height: 40px;
  border-radius: 5px;
}
.preview-node {
  min-width: 0;
  display: flex;
  align-items: center;
  gap: 8px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.preview-node .el-icon {
  flex: 0 0 auto;
  color: var(--cp-brand-deep);
}

@media (hover: none) {
  .node-actions { opacity: 1; }
}

@media (max-width: 1050px) {
  .menu-editor-frame {
    height: auto;
    max-height: none;
    grid-template-columns: 1fr;
  }
  .tree-panel {
    height: 520px;
    border-right: 0;
    border-bottom: 1px solid var(--cp-line);
  }
  .detail-panel {
    min-height: 700px;
  }
  .detail-scroll {
    overflow: visible;
  }
}

@media (max-width: 700px) {
  .workspace-heading {
    align-items: flex-start;
    flex-direction: column;
  }
  .heading-actions {
    width: 100%;
    justify-content: flex-start;
  }
  .heading-actions .dirty-state {
    width: 100%;
  }
  .menu-editor-frame {
    min-height: 0;
  }
  .tree-panel {
    height: 460px;
  }
  .tree-heading,
  .detail-head,
  .form-section,
  .detail-footer {
    padding-left: 14px;
    padding-right: 14px;
  }
  .tree-heading {
    align-items: flex-start;
  }
  .title-row,
  .detail-footer {
    align-items: flex-start;
    flex-direction: column;
  }
  .title-actions,
  .context-strip {
    width: 100%;
    flex-wrap: wrap;
  }
  .context-strip {
    gap: 8px 14px;
  }
  .form-grid {
    grid-template-columns: 1fr;
  }
  .form-grid .span-two {
    grid-column: auto;
  }
  .child-list {
    overflow-x: auto;
  }
  .child-list > div {
    min-width: 560px;
  }
}
</style>
