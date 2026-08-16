<script setup lang="ts">
import { computed, ref } from 'vue'
import {
  designCadMappingProfileApi,
  type SpaceCadMappingProfileDetail,
  type SpaceCadMappingRule,
  type SpaceCadMappingSourceKind,
} from '@/api/space/designCadMappingProfiles'
import type {
  SpaceCadGeometryRule,
  SpaceCadSemanticTarget,
} from '@/api/space/designCadParse'

const props = defineProps<{
  initialProfileId?: string
  initialProfileVersion?: number
}>()

const emit = defineEmits<{
  saved: [profile: SpaceCadMappingProfileDetail]
}>()

const open = ref(false)
const busy = ref(false)
const error = ref('')
const success = ref('')
const profiles = ref<SpaceCadMappingProfileDetail[]>([])
const selectedKey = ref('')
const selected = ref<SpaceCadMappingProfileDetail | null>(null)
const draftName = ref('')
const draftEnabled = ref(true)
const rules = ref<SpaceCadMappingRule[]>([])

const semanticTargets: Array<{ value: SpaceCadSemanticTarget; label: string }> = [
  { value: 'Wall', label: '墙' },
  { value: 'Column', label: '柱' },
  { value: 'Door', label: '门' },
  { value: 'Dock', label: '月台' },
  { value: 'Zone', label: '库区' },
  { value: 'Aisle', label: '巷道' },
  { value: 'Rack', label: '货架' },
  { value: 'Equipment', label: '静态设备' },
  { value: 'VerticalCirculation', label: '垂直交通' },
  { value: 'Annotation', label: '标注' },
  { value: 'Guide', label: '辅助线' },
  { value: 'RestrictedArea', label: '限制区' },
]

const canSave = computed(() =>
  Boolean(selected.value && draftName.value.trim() && rules.value.length) &&
  !busy.value,
)

async function toggle(): Promise<void> {
  open.value = !open.value
  if (open.value)
    await loadProfiles()
}

async function loadProfiles(preferredId?: string, preferredVersion?: number): Promise<void> {
  busy.value = true
  error.value = ''
  try {
    profiles.value = await designCadMappingProfileApi.listProfiles()
    const preferredProfileId = preferredId ?? props.initialProfileId
    const preferredProfileVersion = preferredVersion ?? props.initialProfileVersion
    const preferred = profiles.value.find((profile) =>
      profile.id === preferredProfileId &&
      (preferredProfileVersion === undefined ||
        profile.version === preferredProfileVersion),
    ) ?? profiles.value[0]
    if (!preferred) return
    selectedKey.value = key(preferred)
    await loadSelected()
  } catch (cause) {
    error.value = message(cause, '无法加载 CAD Mapping Profile')
  } finally {
    busy.value = false
  }
}

async function loadSelected(): Promise<void> {
  const [profileId, rawVersion] = selectedKey.value.split(':')
  if (!profileId || !rawVersion) return
  busy.value = true
  error.value = ''
  success.value = ''
  try {
    const profile = await designCadMappingProfileApi.getProfile(
      profileId,
      Number(rawVersion),
    )
    selected.value = profile
    draftName.value = profile.scope === 'System'
      ? `${profile.name} 副本`
      : profile.name
    draftEnabled.value = profile.isEnabled
    rules.value = profile.rules.map((rule) => ({ ...rule }))
  } catch (cause) {
    error.value = message(cause, '无法读取 CAD Mapping Profile')
  } finally {
    busy.value = false
  }
}

function addRule(): void {
  const used = new Set(rules.value.map((rule) => rule.ruleId))
  let index = rules.value.length + 1
  while (used.has(`tenant-rule-${index}`)) index += 1
  rules.value.push({
    ruleId: `tenant-rule-${index}`,
    priority: 100,
    sourceKind: 'Layer',
    matchKind: 'Exact',
    pattern: 'NEW_LAYER',
    target: 'Rack',
    geometryRule: 'DirectGeometry',
    confidenceWeight: .9,
    isRequired: false,
  })
}

function removeRule(index: number): void {
  rules.value.splice(index, 1)
}

function changeSourceKind(rule: SpaceCadMappingRule): void {
  rule.attributeName = null
  rule.attributeMatchKind = null
  rule.attributePattern = null
  rule.geometryRule = rule.sourceKind === 'Block'
    ? 'BlockFootprint'
    : 'DirectGeometry'
}

function geometryOptions(sourceKind: SpaceCadMappingSourceKind): SpaceCadGeometryRule[] {
  return sourceKind === 'Block'
    ? ['BlockFootprint', 'InsertionPoint']
    : ['DirectGeometry', 'Centerline', 'ClosedBoundary']
}

async function save(): Promise<void> {
  const current = selected.value
  if (!current || !canSave.value) return
  busy.value = true
  error.value = ''
  success.value = ''
  try {
    const isCopy = current.scope === 'System'
    const result = await designCadMappingProfileApi.save({
      profileId: isCopy ? null : current.id,
      name: draftName.value.trim(),
      isEnabled: draftEnabled.value,
      rules: rules.value.map((rule) => ({ ...rule })),
      expectedRowVersion: isCopy ? null : current.rowVersion,
      copyFromProfileId: isCopy ? current.id : null,
      copyFromVersion: isCopy ? current.version : null,
    }, crypto.randomUUID())
    const successMessage = result.created
      ? `已创建租户 Profile v${result.profile.version}`
      : `已保存不可变新版本 v${result.profile.version}`
    emit('saved', result.profile)
    await loadProfiles(result.profile.id, result.profile.version)
    success.value = successMessage
  } catch (cause) {
    error.value = message(cause, '保存 CAD Mapping Profile 失败')
  } finally {
    busy.value = false
  }
}

function key(profile: SpaceCadMappingProfileDetail): string {
  return `${profile.id}:${profile.version}`
}

function message(cause: unknown, fallback: string): string {
  if (cause instanceof Error && cause.message) return cause.message
  return fallback
}
</script>

<template>
  <section class="profile-manager" aria-label="CAD Mapping Profile 管理">
    <button
      type="button"
      class="secondary manager-toggle"
      :aria-expanded="open"
      @click="toggle"
    >
      {{ open ? '收起租户 Profile 管理' : '管理租户 Profile' }}
    </button>

    <div v-if="open" class="manager-body">
      <div class="manager-heading">
        <div>
          <h3>租户私有 Mapping Profile</h3>
          <p>系统方案只读；复制后保存为租户方案。已保存版本不可覆盖，后续修改会追加新版本。</p>
        </div>
        <label>
          管理方案
          <select v-model="selectedKey" aria-label="管理 CAD Mapping Profile" :disabled="busy" @change="loadSelected">
            <option v-for="profile in profiles" :key="key(profile)" :value="key(profile)">
              {{ profile.name }} · {{ profile.scope === 'System' ? '系统只读' : '租户私有' }} · v{{ profile.version }}{{ profile.isEnabled ? '' : ' · 已停用' }}
            </option>
          </select>
        </label>
      </div>

      <div v-if="selected" class="profile-fields">
        <label>
          方案名称
          <input v-model="draftName" maxlength="200" aria-label="CAD Mapping Profile 名称" />
        </label>
        <label class="enabled-field">
          <input v-model="draftEnabled" type="checkbox" />
          在 CAD 起始向导中启用
        </label>
        <code>{{ selected.definitionSha256.slice(0, 16) }}…</code>
      </div>

      <div v-if="selected" class="rules-heading">
        <div>
          <h4>规则（{{ rules.length }}）</h4>
          <p>每条规则均由服务端重新验证、规范排序并计算 SHA-256。</p>
        </div>
        <button type="button" class="secondary" :disabled="busy || rules.length >= 500" @click="addRule">
          添加规则
        </button>
      </div>

      <div v-if="selected" class="rule-list">
        <article v-for="(rule, index) in rules" :key="`${rule.ruleId}:${index}`" class="rule-card">
          <div class="rule-grid">
            <label>规则 ID<input v-model.trim="rule.ruleId" maxlength="128" :aria-label="`规则 ${index + 1} ID`" /></label>
            <label>优先级<input v-model.number="rule.priority" type="number" min="0" max="10000" :aria-label="`规则 ${index + 1} 优先级`" /></label>
            <label>来源
              <select v-model="rule.sourceKind" :aria-label="`规则 ${index + 1} 来源`" @change="changeSourceKind(rule)">
                <option value="Layer">图层</option><option value="Block">块</option>
              </select>
            </label>
            <label>匹配
              <select v-model="rule.matchKind" :aria-label="`规则 ${index + 1} 匹配方式`">
                <option value="Exact">精确</option><option value="Glob">Glob</option><option value="Regex">正则</option>
              </select>
            </label>
            <label class="wide">模式<input v-model="rule.pattern" maxlength="512" :aria-label="`规则 ${index + 1} 模式`" /></label>
            <label>目标
              <select v-model="rule.target" :aria-label="`规则 ${index + 1} 目标`">
                <option v-for="target in semanticTargets" :key="target.value" :value="target.value">{{ target.label }}</option>
              </select>
            </label>
            <label>几何
              <select v-model="rule.geometryRule" :aria-label="`规则 ${index + 1} 几何规则`">
                <option v-for="geometry in geometryOptions(rule.sourceKind)" :key="geometry" :value="geometry">{{ geometry }}</option>
              </select>
            </label>
            <label>置信度<input v-model.number="rule.confidenceWeight" type="number" min="0" max="1" step="0.01" :aria-label="`规则 ${index + 1} 置信度`" /></label>
            <label class="required-field"><input v-model="rule.isRequired" type="checkbox" />必须来源</label>
          </div>
          <details class="rule-advanced">
            <summary>高级条件与默认尺寸</summary>
            <div class="rule-grid">
              <label>目标子类型<input v-model="rule.targetSubtype" maxlength="128" /></label>
              <label>默认高度 mm<input v-model.number="rule.defaultHeightMillimeters" type="number" min="0" /></label>
              <label>默认厚度 mm<input v-model.number="rule.defaultThicknessMillimeters" type="number" min="0" /></label>
              <template v-if="rule.sourceKind === 'Block'">
                <label>属性名<input v-model="rule.attributeName" maxlength="128" /></label>
                <label>属性匹配
                  <select v-model="rule.attributeMatchKind">
                    <option :value="null">无</option><option value="Exact">精确</option><option value="Glob">Glob</option><option value="Regex">正则</option>
                  </select>
                </label>
                <label>属性模式<input v-model="rule.attributePattern" maxlength="512" /></label>
              </template>
            </div>
          </details>
          <button type="button" class="danger" :disabled="busy || rules.length <= 1" @click="removeRule(index)">删除规则</button>
        </article>
      </div>

      <p v-if="error" class="manager-error" role="alert">{{ error }}</p>
      <p v-if="success" class="manager-success" role="status">{{ success }}</p>
      <div v-if="selected" class="manager-actions">
        <span>{{ selected.scope === 'System' ? '将创建租户副本 v1' : `将追加 v${selected.version + 1}` }}</span>
        <button type="button" class="primary" :disabled="!canSave" @click="save">
          {{ busy ? '保存中…' : selected.scope === 'System' ? '复制并保存' : '保存新版本' }}
        </button>
      </div>
    </div>
  </section>
</template>

<style scoped>
.profile-manager { margin-top:12px; border:1px solid #2a3950; border-radius:8px; background:#0d1626; }
.manager-toggle { width:100%; justify-content:flex-start; }
.manager-body { display:grid; gap:16px; padding:16px; border-top:1px solid #2a3950; }
.manager-heading,.rules-heading,.manager-actions { display:flex; align-items:end; justify-content:space-between; gap:18px; }
.manager-heading p,.rules-heading p { color:#aebbd0; font-size:14px; }
.manager-heading label { min-width:360px; }
.profile-fields { display:grid; grid-template-columns:minmax(260px,1fr) auto auto; gap:14px; align-items:end; }
.enabled-field,.required-field { display:flex; min-height:44px; align-items:center; gap:8px; }
.enabled-field input,.required-field input { width:44px; height:44px; }
.rule-list { display:grid; gap:12px; max-height:620px; overflow:auto; }
.rule-card { display:grid; gap:10px; padding:12px; border:1px solid #2a3950; border-radius:7px; background:#111a2b; }
.rule-grid { display:grid; grid-template-columns:repeat(4,minmax(0,1fr)); gap:10px; }
.rule-grid .wide { grid-column:span 2; }
.rule-advanced summary { min-height:44px; cursor:pointer; color:#8cebf0; }
.danger { justify-self:end; color:#ffb0b8; border-color:#78404a; }
.manager-error { padding:10px; color:#ffc1c7; background:#321922; }
.manager-success { padding:10px; color:#9bf1c8; background:#123126; }
.manager-actions { padding-top:12px; border-top:1px solid #2a3950; color:#aebbd0; }
@media (max-width:900px) {
  .manager-heading,.rules-heading,.manager-actions { align-items:stretch; flex-direction:column; }
  .manager-heading label { min-width:0; width:100%; }
  .profile-fields,.rule-grid { grid-template-columns:1fr; }
  .rule-grid .wide { grid-column:auto; }
}
</style>
