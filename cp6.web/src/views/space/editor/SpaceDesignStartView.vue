<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { isAxiosError } from 'axios'
import { designProjectApi } from '@/api/space/designProject'
import type {
  ISpaceModelDto,
  ISpaceSceneFloorDto,
  ISpaceVersionDto,
  ISpaceWarehouseTemplateDto,
  ISpaceWarehouseTemplateInstantiationPreviewDto,
} from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

const route = useRoute()
const router = useRouter()
const siteId = computed(() => String(route.params.siteId ?? ''))
const model = ref<ISpaceModelDto | null>(null)
const draftVersion = ref<ISpaceVersionDto | null>(null)
const floors = ref<ISpaceSceneFloorDto[]>([])
const warehouseTemplates = ref<ISpaceWarehouseTemplateDto[]>([])
const templatePreview = ref<ISpaceWarehouseTemplateInstantiationPreviewDto | null>(null)
const loading = ref(true)
const savingDraft = ref(false)
const savingFloor = ref(false)
const previewingTemplateId = ref('')
const errorText = ref('')
const templateErrorText = ref('')
const addingFloor = ref(false)
const viewportWidth = ref(window.innerWidth)
const draftName = ref('')
const floorForm = reactive({
  floorCode: '',
  name: '',
  level: '',
  elevation: '',
  height: '',
})

const isNarrow = computed(() => viewportWidth.value < 1280)
const showFloorForm = computed(
  () => Boolean(draftVersion.value) && (floors.value.length === 0 || addingFloor.value),
)

function updateViewport() {
  viewportWidth.value = window.innerWidth
}

function errorMessage(error: unknown) {
  if (isAxiosError(error)) {
    const data = error.response?.data as { detail?: string; title?: string } | undefined
    return data?.detail || data?.title || error.message
  }
  return error instanceof Error ? error.message : 'Space Studio 请求失败。'
}

async function loadDraft(versionId: string) {
  const [version, versionFloors] = await Promise.all([
    designProjectApi.getVersion(versionId),
    designProjectApi.getFloors(versionId),
  ])
  draftVersion.value = version
  floors.value = versionFloors
}

async function loadProject() {
  loading.value = true
  errorText.value = ''
  templateErrorText.value = ''
  try {
    model.value = await designProjectApi.getModel(siteId.value)
    try {
      warehouseTemplates.value = await designProjectApi.getWarehouseTemplates()
    } catch (error) {
      warehouseTemplates.value = []
      templateErrorText.value = errorMessage(error)
    }
    if (model.value.activeDraftVersionId) {
      await loadDraft(model.value.activeDraftVersionId)
    } else {
      draftVersion.value = null
      floors.value = []
    }
  } catch (error) {
    errorText.value = errorMessage(error)
  } finally {
    loading.value = false
  }
}

async function previewWarehouseTemplate(template: ISpaceWarehouseTemplateDto) {
  const templateId = template.id
  const templateVersionId = template.latestVersion?.id
  if (!templateId || !templateVersionId) {
    errorText.value = '模板版本标识不完整，请重新加载。'
    return
  }

  previewingTemplateId.value = templateId
  errorText.value = ''
  try {
    templatePreview.value = await designProjectApi.previewWarehouseTemplate(
      templateId,
      templateVersionId,
    )
  } catch (error) {
    errorText.value = errorMessage(error)
  } finally {
    previewingTemplateId.value = ''
  }
}

async function createBlankDraft() {
  const name = draftName.value.trim()
  if (!name) {
    errorText.value = '请输入草稿名称。'
    return
  }
  if (isNarrow.value)
    return

  savingDraft.value = true
  errorText.value = ''
  try {
    const created = await designProjectApi.createBlankVersion(siteId.value, name)
    if (!created.id)
      throw new Error('服务端未返回新 Draft 标识。')
    await loadDraft(created.id)
  } catch (error) {
    errorText.value = errorMessage(error)
  } finally {
    savingDraft.value = false
  }
}

function parseExplicitInteger(value: string, label: string) {
  if (!value.trim())
    throw new Error(`请输入${label}。`)
  const parsed = Number(value)
  if (!Number.isInteger(parsed))
    throw new Error(`${label}必须是整数毫米值。`)
  return parsed
}

async function createFloor() {
  if (!draftVersion.value || isNarrow.value)
    return
  const activeVersionId = draftVersion.value.id
  if (!activeVersionId) {
    errorText.value = '当前 Draft 缺少版本标识，请重新加载。'
    return
  }
  const floorCode = floorForm.floorCode.trim()
  const name = floorForm.name.trim()
  if (!floorCode || !name) {
    errorText.value = '请输入楼层编码和名称。'
    return
  }

  let level: number
  let elevation: number
  let height: number
  try {
    level = parseExplicitInteger(floorForm.level, '层级')
    elevation = parseExplicitInteger(floorForm.elevation, '标高')
    height = parseExplicitInteger(floorForm.height, '层高')
    if (height < 0)
      throw new Error('层高不能小于零。')
  } catch (error) {
    errorText.value = errorMessage(error)
    return
  }

  savingFloor.value = true
  errorText.value = ''
  try {
    const response = await designProjectApi.createFloor(
      activeVersionId,
      {
        floorCode,
        name,
        level,
        elevation,
        height,
        expectedContentRevision: draftVersion.value.contentRevision ?? 0,
      },
    )
    draftVersion.value = {
      ...draftVersion.value,
      contentRevision: response.versionContentRevision,
    }
    openFloor(response.floor)
  } catch (error) {
    errorText.value = errorMessage(error)
  } finally {
    savingFloor.value = false
  }
}

function openFloor(floor: ISpaceSceneFloorDto) {
  const activeVersionId = draftVersion.value?.id
  const floorLogicalId = floor.revision?.logicalId
  if (!activeVersionId || !floorLogicalId) {
    errorText.value = '楼层标识不完整，请重新加载。'
    return
  }
  router.push({
    name: 'space-design-underlay',
    params: {
      versionId: activeVersionId,
      floorLogicalId,
    },
  })
}

function floorLogicalIdOf(floor: ISpaceSceneFloorDto) {
  return floor.revision?.logicalId ?? ''
}

function goBack() {
  router.push('/space')
}

onMounted(() => {
  window.addEventListener('resize', updateViewport)
  void loadProject()
})

onBeforeUnmount(() => {
  window.removeEventListener('resize', updateViewport)
})
</script>

<template>
  <main class="space-design-start">
    <header class="start-header">
      <div>
        <p class="eyebrow">CP6 SPACE STUDIO</p>
        <h1>建立仓库设计草稿</h1>
        <p class="site-line">Site · <span>{{ siteId }}</span></p>
      </div>
      <button class="secondary-button" type="button" @click="goBack">返回 Space</button>
    </header>

    <section v-if="isNarrow" class="notice" role="status" data-testid="narrow-notice">
      当前宽度低于 1280px，已切换为只读。请在更宽的窗口中创建草稿或楼层。
    </section>

    <section v-if="errorText" class="error-banner" role="alert">
      <span>{{ errorText }}</span>
      <button type="button" @click="loadProject">重新加载</button>
    </section>

    <section v-if="loading" class="state-panel" aria-live="polite">
      正在加载 Design V1 项目…
    </section>

    <template v-else-if="model">
      <section class="progress-strip" aria-label="建模步骤">
        <span class="active">1 建立草稿</span>
        <span :class="{ active: draftVersion }">2 初始化楼层</span>
        <span :class="{ active: floors.length }">3 进入工作台</span>
      </section>

      <section v-if="!draftVersion" class="start-panel">
        <div class="panel-copy">
          <p class="step-label">STEP 1</p>
          <h2>从空白建立 Draft</h2>
          <p>不会复制 Published 内容，也不会自动猜测楼层。创建后再显式填写首个楼层。</p>
        </div>
        <form class="form-grid one-column" @submit.prevent="createBlankDraft">
          <label>
            草稿名称
            <input
              v-model="draftName"
              data-testid="draft-name"
              maxlength="200"
              autocomplete="off"
              placeholder="例如：华东仓改造草稿"
            >
          </label>
          <button
            class="primary-button"
            data-testid="create-draft"
            type="submit"
            :disabled="savingDraft || isNarrow"
          >{{ savingDraft ? '创建中…' : '创建空白 Draft' }}</button>
        </form>
      </section>

      <section v-if="!draftVersion" class="floor-section template-section">
        <div class="section-heading">
          <div>
            <p class="step-label">PLATFORM TEMPLATE CATALOG</p>
            <h2>平台整仓模板</h2>
            <p class="template-help">模板版本和内容哈希不可变；当前仅提供布局预览，不会写入 Draft。</p>
          </div>
        </div>
        <div v-if="warehouseTemplates.length" class="template-list">
          <article
            v-for="item in warehouseTemplates"
            :key="item.id"
            class="template-card"
          >
            <div>
              <span class="floor-level">{{ item.scope }}</span>
              <h3>{{ item.name }}</h3>
              <p>{{ item.description }}</p>
            </div>
            <dl>
              <div><dt>楼层</dt><dd>{{ item.latestVersion?.counts?.floors }}</dd></div>
              <div><dt>货架</dt><dd>{{ item.latestVersion?.counts?.racks }}</dd></div>
              <div><dt>库位</dt><dd>{{ item.latestVersion?.counts?.locations }}</dd></div>
              <div><dt>版本</dt><dd>v{{ item.latestVersion?.versionNo }}</dd></div>
            </dl>
            <button
              class="secondary-button"
              type="button"
              :data-template-id="item.id"
              :disabled="previewingTemplateId === item.id"
              @click="previewWarehouseTemplate(item)"
            >{{ previewingTemplateId === item.id ? '生成中…' : '查看实例化预览' }}</button>
          </article>
        </div>
        <p v-else-if="templateErrorText" class="template-error" role="alert">
          模板目录暂不可用：{{ templateErrorText }}。空白 Draft 创建仍可继续。
        </p>
        <p v-else class="template-help">当前没有可用整仓模板。</p>
        <div v-if="templatePreview" class="template-preview" role="status">
          <strong>预览已密封，未写入 Draft</strong>
          <span>
            {{ templatePreview.counts?.floors }} 层 ·
            {{ templatePreview.counts?.zones }} 区 ·
            {{ templatePreview.counts?.aisles }} 巷道 ·
            {{ templatePreview.counts?.racks }} 货架 ·
            {{ templatePreview.counts?.locations }} 库位
          </span>
          <code>{{ templatePreview.proposalHash }}</code>
          <small>先创建 Draft 与目标楼层，再在 Space Studio「构件」中按楼层原子写入。</small>
        </div>
      </section>

      <template v-else>
        <section class="version-summary">
          <div>
            <p class="step-label">ACTIVE DRAFT</p>
            <h2>{{ draftVersion.name }}</h2>
          </div>
          <dl>
            <div><dt>版本</dt><dd>{{ draftVersion.versionNo }}</dd></div>
            <div><dt>状态</dt><dd>{{ draftVersion.status }}</dd></div>
            <div><dt>Content Revision</dt><dd>{{ draftVersion.contentRevision }}</dd></div>
            <div><dt>来源</dt><dd>{{ draftVersion.basedOnVersionId ? 'Published' : 'Blank' }}</dd></div>
          </dl>
        </section>

        <section v-if="floors.length" class="floor-section">
          <div class="section-heading">
            <div>
              <p class="step-label">STEP 3</p>
              <h2>选择楼层进入 Space Studio</h2>
            </div>
            <button
              class="secondary-button"
              type="button"
              :disabled="isNarrow"
              @click="addingFloor = !addingFloor"
            >{{ addingFloor ? '取消新增' : '新增楼层' }}</button>
          </div>
          <div class="floor-list">
            <button
              v-for="item in floors"
              :key="floorLogicalIdOf(item)"
              class="floor-card"
              type="button"
              :data-floor-id="floorLogicalIdOf(item)"
              @click="openFloor(item)"
            >
              <span class="floor-level">L{{ item.level }}</span>
              <strong>{{ item.floorCode }} · {{ item.name }}</strong>
              <small>标高 {{ item.elevation }} mm · 层高 {{ item.height }} mm</small>
              <span class="open-label">打开工作台 →</span>
            </button>
          </div>
        </section>

        <section v-if="showFloorForm" class="start-panel floor-form-panel">
          <div class="panel-copy">
            <p class="step-label">STEP 2</p>
            <h2>{{ floors.length ? '新增设计楼层' : '初始化首个设计楼层' }}</h2>
            <p>以下字段全部由你确认；系统不会从旧运行态楼层或 CAD 文件静默推断。</p>
          </div>
          <form class="form-grid" @submit.prevent="createFloor">
            <label>
              楼层编码
              <input v-model="floorForm.floorCode" data-testid="floor-code" maxlength="100" placeholder="例如 F1">
            </label>
            <label>
              楼层名称
              <input v-model="floorForm.name" data-testid="floor-name" maxlength="200" placeholder="例如 一层仓库">
            </label>
            <label>
              层级
              <input v-model="floorForm.level" data-testid="floor-level" inputmode="numeric" placeholder="整数，例如 1">
            </label>
            <label>
              标高（mm）
              <input v-model="floorForm.elevation" data-testid="floor-elevation" inputmode="numeric" placeholder="整数，例如 0">
            </label>
            <label>
              层高（mm）
              <input v-model="floorForm.height" data-testid="floor-height" inputmode="numeric" placeholder="整数，例如 6000">
            </label>
            <button
              class="primary-button"
              data-testid="create-floor"
              type="submit"
              :disabled="savingFloor || isNarrow"
            >{{ savingFloor ? '创建中…' : '创建并进入工作台' }}</button>
          </form>
        </section>
      </template>
    </template>
  </main>
</template>

<style scoped>
.space-design-start {
  --space-studio-bg: #07141b;
  --space-studio-panel: #0d2029;
  --space-studio-panel-soft: #102832;
  --space-studio-line: #24424c;
  --space-studio-text: #f1f7f8;
  --space-studio-muted: #a7bdc3;
  --space-studio-accent: #26d7d3;
  --space-studio-accent-ink: #032c31;
  min-height: 100vh;
  box-sizing: border-box;
  padding: 32px clamp(24px, 5vw, 80px) 64px;
  color: var(--space-studio-text);
  background:
    radial-gradient(circle at 78% 4%, rgb(38 215 211 / 12%), transparent 34%),
    var(--space-studio-bg);
  font-family: Inter, "Noto Sans SC", system-ui, sans-serif;
}

.start-header,
.section-heading,
.version-summary {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 24px;
}

.start-header {
  min-height: 88px;
  padding-bottom: 24px;
  border-bottom: 1px solid var(--space-studio-line);
}

h1,
h2,
p { margin: 0; }
h1 { font-size: clamp(28px, 3vw, 42px); line-height: 1.15; }
h2 { font-size: 22px; line-height: 1.35; }
.eyebrow,
.step-label {
  color: var(--space-studio-accent);
  font-size: 13px;
  font-weight: 800;
  letter-spacing: .14em;
}
.site-line,
.panel-copy p:last-child { margin-top: 10px; color: var(--space-studio-muted); font-size: 16px; }
.site-line span { font-family: ui-monospace, SFMono-Regular, Consolas, monospace; }

.progress-strip {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  margin: 28px 0 18px;
  border: 1px solid var(--space-studio-line);
  background: rgb(6 24 31 / 72%);
}
.progress-strip span {
  min-height: 44px;
  display: grid;
  place-items: center;
  color: var(--space-studio-muted);
  font-size: 14px;
  border-right: 1px solid var(--space-studio-line);
}
.progress-strip span:last-child { border-right: 0; }
.progress-strip .active { color: var(--space-studio-text); background: rgb(38 215 211 / 10%); }

.start-panel,
.version-summary,
.floor-section,
.state-panel {
  margin-top: 18px;
  padding: 28px;
  border: 1px solid var(--space-studio-line);
  background: linear-gradient(145deg, var(--space-studio-panel), var(--space-studio-panel-soft));
}
.start-panel { display: grid; grid-template-columns: minmax(240px, .85fr) minmax(420px, 1.4fr); gap: 44px; }
.floor-form-panel { margin-top: 18px; }
.form-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 18px; }
.form-grid.one-column { grid-template-columns: 1fr; }
label { display: grid; gap: 8px; color: var(--space-studio-muted); font-size: 14px; font-weight: 700; }
input {
  min-height: 44px;
  box-sizing: border-box;
  border: 1px solid #365762;
  background: #071920;
  color: var(--space-studio-text);
  padding: 0 13px;
  font: inherit;
  font-size: 16px;
  outline: none;
}
input:focus-visible,
button:focus-visible { outline: 3px solid #7df7f3; outline-offset: 2px; }

button { min-height: 44px; cursor: pointer; font: inherit; font-weight: 800; }
button:disabled { cursor: not-allowed; opacity: .45; }
.primary-button {
  align-self: end;
  border: 0;
  background: var(--space-studio-accent);
  color: var(--space-studio-accent-ink);
  padding: 0 20px;
}
.secondary-button {
  border: 1px solid #3d6670;
  background: transparent;
  color: var(--space-studio-text);
  padding: 0 18px;
}

.version-summary dl { display: grid; grid-template-columns: repeat(4, minmax(100px, 1fr)); gap: 20px; margin: 0; }
.version-summary dl div { min-width: 110px; }
.version-summary dt { color: var(--space-studio-muted); font-size: 13px; }
.version-summary dd { margin: 5px 0 0; font-family: ui-monospace, SFMono-Regular, Consolas, monospace; }
.floor-list { display: grid; grid-template-columns: repeat(auto-fill, minmax(260px, 1fr)); gap: 14px; margin-top: 22px; }
.template-list { display: grid; grid-template-columns: repeat(auto-fill, minmax(320px, 1fr)); gap: 14px; margin-top: 22px; }
.template-card {
  display: grid;
  gap: 18px;
  border: 1px solid #31535d;
  background: #091a22;
  padding: 20px;
}
.template-card h3 { margin: 8px 0 0; font-size: 20px; }
.template-card p,
.template-help { margin-top: 8px; color: var(--space-studio-muted); font-size: 16px; }
.template-error { margin-top: 18px; color: #ffd0d0; font-size: 16px; }
.template-card dl { display: grid; grid-template-columns: repeat(4, 1fr); gap: 10px; margin: 0; }
.template-card dt { color: var(--space-studio-muted); font-size: 13px; }
.template-card dd { margin: 4px 0 0; font-weight: 800; }
.template-preview {
  display: grid;
  gap: 8px;
  margin-top: 18px;
  border-left: 4px solid var(--space-studio-accent);
  background: rgb(38 215 211 / 8%);
  padding: 16px;
  font-size: 16px;
}
.template-preview code { overflow-wrap: anywhere; color: var(--space-studio-accent); }
.template-preview small { color: var(--space-studio-muted); font-size: 14px; }
.floor-card {
  min-height: 150px;
  display: grid;
  gap: 8px;
  text-align: left;
  border: 1px solid #31535d;
  background: #091a22;
  color: var(--space-studio-text);
  padding: 18px;
}
.floor-card:hover { border-color: var(--space-studio-accent); transform: translateY(-1px); }
.floor-level,
.open-label { color: var(--space-studio-accent); font-size: 13px; }
.floor-card small { color: var(--space-studio-muted); font-size: 14px; }
.open-label { align-self: end; font-weight: 800; }

.notice,
.error-banner {
  min-height: 44px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  margin-top: 18px;
  padding: 10px 14px;
  box-sizing: border-box;
  font-size: 16px;
}
.notice { border: 1px solid #ad7b28; background: #3b2a0e; color: #ffe2a3; }
.error-banner { border: 1px solid #c75454; background: #351616; color: #ffd0d0; }
.error-banner button { border: 1px solid currentColor; background: transparent; color: inherit; padding: 0 14px; }

@media (max-width: 900px) {
  .start-panel,
  .version-summary { display: grid; grid-template-columns: 1fr; }
  .version-summary dl { grid-template-columns: repeat(2, 1fr); }
}
</style>
