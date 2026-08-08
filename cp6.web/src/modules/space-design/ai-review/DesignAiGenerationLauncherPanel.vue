<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { isAxiosError } from 'axios'
import { ElMessage, ElMessageBox } from 'element-plus'
import { aiProposalReviewApi } from '@/api/space/aiProposalReview'
import type {
  ISpaceSourceDto,
  ISpaceVersionDto,
  ISpaceRackGenerationProfileDto,
} from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

const props = defineProps<{
  versionId: string
  currentContentRevision: number
}>()
const emit = defineEmits<{
  close: []
  created: [runId: string]
}>()

const loading = ref(false)
const submitting = ref(false)
const version = ref<ISpaceVersionDto | null>(null)
const sources = ref<ISpaceSourceDto[]>([])
const rackProfiles = ref<ISpaceRackGenerationProfileDto[]>([])
const selectedSourceId = ref('')
const selectedRackProfileVersionId = ref('')
let idempotencyKey = ''

const eligibleSources = computed(() => sources.value.filter((source) =>
  Boolean(
    source.id
    && ['Dwg', 'Dxf'].includes(source.sourceType ?? '')
    && ['PreviewReady', 'Imported'].includes(source.state ?? '')
    && source.mappingProfileId
    && Number.isInteger(source.mappingProfileVersion),
  ),
))
const selectedSource = computed(() => eligibleSources.value.find(
  source => source.id === selectedSourceId.value,
))
const selectedRackProfile = computed(() => rackProfiles.value.find(
  profile => profile.latestVersion?.id === selectedRackProfileVersionId.value,
))
const versionIsCurrent = computed(() => Boolean(
  version.value?.status === 'Draft'
  && version.value.rowVersion
  && version.value.contentRevision === props.currentContentRevision,
))
const canCreate = computed(() => Boolean(
  versionIsCurrent.value && selectedSource.value && !submitting.value,
))

onMounted(() => void load())

async function load(): Promise<void> {
  loading.value = true
  try {
    const [nextVersion, page, profilePage] = await Promise.all([
      aiProposalReviewApi.getVersion(props.versionId),
      aiProposalReviewApi.getSources(props.versionId),
      aiProposalReviewApi.getRackGenerationProfiles(),
    ])
    version.value = nextVersion
    sources.value = page.items ?? []
    rackProfiles.value = (profilePage.items ?? []).filter(profile =>
      profile.status === 'Active'
      && profile.latestVersion?.status === 'Ready'
      && Boolean(profile.latestVersion.id),
    )
    if (!rackProfiles.value.some(profile =>
      profile.latestVersion?.id === selectedRackProfileVersionId.value)) {
      selectedRackProfileVersionId.value = ''
    }
    if (!eligibleSources.value.some(source => source.id === selectedSourceId.value)) {
      selectedSourceId.value = eligibleSources.value.length === 1
        ? eligibleSources.value[0]?.id ?? ''
        : ''
    }
  } finally {
    loading.value = false
  }
}

async function createRun(): Promise<void> {
  const currentVersion = version.value
  const source = selectedSource.value
  if (!canCreate.value || !currentVersion?.rowVersion || !source?.id) return

  await ElMessageBox.confirm(
    `确认基于 CAD 来源“${source.displayName ?? source.id}”启动规则生成？${selectedRackProfile.value ? `将固定货架方案“${selectedRackProfile.value.name}”。` : '未选择货架方案时，Rack 提案会保留阻断问题。'}不会调用外部 AI Provider，也不会自动写入 Draft。`,
    '启动规则生成',
    {
      type: 'warning',
      confirmButtonText: '排队生成',
      cancelButtonText: '返回',
    },
  )

  submitting.value = true
  idempotencyKey ||= crypto.randomUUID()
  try {
    const accepted = await aiProposalReviewApi.createGenerationRun(
      props.versionId,
      {
        sourceId: source.id,
        mappingProfileVersionId: source.mappingProfileId ?? null,
        rackGenerationProfileVersionId:
          selectedRackProfileVersionId.value || null,
        mode: 'RuleOnly',
        expectedContentRevision: props.currentContentRevision,
      },
      currentVersion.rowVersion,
      idempotencyKey,
    )
    ElMessage.success(
      accepted.idempotentReplay || accepted.reused
        ? '已恢复同一规则生成任务'
        : '规则生成任务已排队',
    )
    emit('created', accepted.runId)
  } catch (error) {
    if (isAxiosError(error) && error.response && [409, 422].includes(error.response.status)) {
      idempotencyKey = ''
      await load()
    }
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <aside class="generation-launcher" data-test="ai-generation-launcher" v-loading="loading">
    <header>
      <div>
        <h2>规则生成</h2>
        <p>从已确认的 DWG/DXF 预览创建可审查提案</p>
      </div>
      <el-button text aria-label="关闭规则生成" @click="emit('close')">关闭</el-button>
    </header>

    <el-alert
      v-if="!versionIsCurrent"
      type="warning"
      :closable="false"
      title="当前 Draft 已变化或版本不可编辑，请刷新场景后再生成。"
    />
    <el-alert
      v-else-if="eligibleSources.length === 0"
      type="warning"
      :closable="false"
      title="没有可生成的 CAD 来源。请先完成 DWG/DXF 解析、映射和预览确认。"
    />
    <el-alert
      v-else
      type="info"
      :closable="false"
      title="当前只开放 RuleOnly：使用确定性规则，不调用外部 AI Provider。"
    />

    <el-form label-position="top">
      <el-form-item label="已确认的 CAD 来源">
        <el-select
          v-model="selectedSourceId"
          data-test="ai-generation-source"
          placeholder="选择 DWG/DXF 预览"
          :disabled="!versionIsCurrent"
        >
          <el-option
            v-for="source in eligibleSources"
            :key="source.id"
            :label="`${source.displayName ?? source.id} · ${source.sourceType} · 映射 v${source.mappingProfileVersion}`"
            :value="source.id"
          />
        </el-select>
      </el-form-item>
      <el-form-item label="货架生成方案（可选）">
        <el-select
          v-model="selectedRackProfileVersionId"
          clearable
          data-test="rack-generation-profile"
          placeholder="不选择：Rack 保持 Blocking"
          :disabled="!versionIsCurrent"
        >
          <el-option
            v-for="profile in rackProfiles"
            :key="profile.latestVersion?.id"
            :label="`${profile.name} · ${profile.scope} · v${profile.latestVersion?.versionNo}`"
            :value="profile.latestVersion?.id"
          />
        </el-select>
      </el-form-item>
    </el-form>

    <dl v-if="selectedSource" class="source-evidence">
      <div><dt>来源状态</dt><dd>{{ selectedSource.state }}</dd></div>
      <div><dt>坐标单位</dt><dd>{{ selectedSource.unit ?? '由后端校验' }}</dd></div>
      <div><dt>内容指纹</dt><dd>{{ selectedSource.sha256?.slice(0, 16) }}…</dd></div>
    </dl>

    <dl v-if="selectedRackProfile?.latestVersion" class="source-evidence">
      <div><dt>货架尺寸</dt><dd>{{ selectedRackProfile.latestVersion.rackWidthMillimeters }} × {{ selectedRackProfile.latestVersion.rackDepthMillimeters }} × {{ selectedRackProfile.latestVersion.rackHeightMillimeters }} mm</dd></div>
      <div><dt>层数 / 库位数</dt><dd>{{ selectedRackProfile.latestVersion.levels?.length ?? 0 }} / {{ selectedRackProfile.latestVersion.locationCount }}</dd></div>
      <div><dt>方案指纹</dt><dd>{{ selectedRackProfile.latestVersion.contentHash?.slice(0, 16) }}…</dd></div>
    </dl>

    <footer>
      <el-button @click="load">刷新来源</el-button>
      <el-button
        v-permission="'space:model:generate-ai'"
        data-test="create-rule-only-run"
        type="primary"
        :loading="submitting"
        :disabled="!canCreate"
        @click="createRun"
      >
        启动规则生成
      </el-button>
    </footer>
  </aside>
</template>

<style scoped>
.generation-launcher {
  width: min(420px, 42vw);
  min-width: 340px;
  padding: 18px;
  overflow: auto;
  background: #fff;
  border-left: 1px solid #dcdfe6;
}

header,
footer,
.source-evidence div {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

header h2 {
  margin: 0;
}

header p {
  margin: 4px 0 0;
  color: #606266;
}

.el-alert,
.el-form,
.source-evidence {
  margin-top: 18px;
}

.source-evidence {
  padding: 12px;
  background: #f5f7fa;
  border-radius: 6px;
}

.source-evidence div + div {
  margin-top: 8px;
}

.source-evidence dt {
  color: #606266;
}

.source-evidence dd {
  margin: 0;
  text-align: right;
  overflow-wrap: anywhere;
}

footer {
  margin-top: 22px;
  justify-content: flex-end;
}
</style>
