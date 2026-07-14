<!--
  连接器新建/编辑对话框（D-T2）。凭证输入即写不回显：编辑态且 hasAuth 时，authJson 输入框
  placeholder=「已配置（不回显）」，留空提交=保留原密文；填新值=重加密覆盖。
  TimeoutSec 须严格小于租约（后端 E-WF-028，此处仅前置提示，最终以后端校验为准）。
-->
<template>
  <el-dialog :model-value="modelValue" :title="editing ? t('common.edit') : t('oa.connector.new')"
             width="560px" @close="onClose">
    <el-form label-width="120px">
      <el-form-item :label="t('oa.connector.form.name')">
        <el-input v-model="form.name" :disabled="!!editing" :placeholder="t('oa.connector.form.nameHint')" />
      </el-form-item>
      <el-form-item :label="t('oa.connector.form.displayName')">
        <el-input v-model="form.displayName" />
      </el-form-item>
      <el-form-item :label="t('oa.connector.form.baseUrl')">
        <el-input v-model="form.baseUrl" placeholder="https://erp.example.com" />
      </el-form-item>
      <el-form-item :label="t('oa.connector.form.auth')">
        <el-input v-model="form.authJson" type="textarea" :rows="2"
                  :placeholder="authPlaceholder" />
        <div class="auth-hint">{{ t('oa.connector.form.authHint') }}</div>
      </el-form-item>
      <el-form-item :label="t('oa.connector.form.timeout')">
        <el-input-number v-model="form.timeoutSec" :min="1" :max="600" />
      </el-form-item>
      <el-form-item :label="t('oa.connector.col.enabled')">
        <el-switch v-model="form.enabled" />
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="onClose">{{ t('common.cancel') }}</el-button>
      <el-button type="primary" :loading="saving" @click="onSave">{{ t('common.save') }}</el-button>
    </template>
  </el-dialog>
</template>

<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { wfConnectorApi, type WfConnectorItem } from '@/api/oa/wfConnector'

const props = defineProps<{ modelValue: boolean; editing: WfConnectorItem | null }>()
const emit = defineEmits<{ 'update:modelValue': [boolean]; saved: [] }>()
const { t } = useI18n()

const form = reactive({ name: '', displayName: '', baseUrl: '', authJson: '', timeoutSec: 30, enabled: true })
const saving = ref(false)

// 编辑态且已配置凭证 → placeholder 提示「已配置（不回显）」，留空=保留；否则普通输入提示。
const authPlaceholder = computed(() =>
  props.editing?.hasAuth ? t('oa.connector.form.authConfigured') : t('oa.connector.form.authPlaceholder'))

watch(() => props.modelValue, open => { if (open) hydrate() })

function hydrate() {
  const e = props.editing
  if (!e) {
    Object.assign(form, { name: '', displayName: '', baseUrl: '', authJson: '', timeoutSec: 30, enabled: true })
    return
  }
  // 元数据回填；authJson 永不回填（掩码读契约）→ 留空占位
  Object.assign(form, {
    name: e.name, displayName: e.displayName, baseUrl: e.baseUrl,
    authJson: '', timeoutSec: e.timeoutSec, enabled: e.enabled,
  })
}

function onClose() { emit('update:modelValue', false) }

async function onSave() {
  if (!form.name.trim() || !form.baseUrl.trim()) {
    ElMessage.warning(t('oa.connector.form.required'))
    return
  }
  const body = {
    name: form.name.trim(),
    displayName: form.displayName.trim() || form.name.trim(),
    baseUrl: form.baseUrl.trim(),
    // 留空=保留原密文（后端 UpdateAsync 空即保留）；新建时留空=无认证连接器
    authJson: form.authJson.trim() ? form.authJson.trim() : null,
    timeoutSec: form.timeoutSec,
    enabled: form.enabled,
  }
  saving.value = true
  try {
    if (props.editing) await wfConnectorApi.update(props.editing.id, body)
    else await wfConnectorApi.create(body)
    emit('saved')
    onClose()
  } catch {
    // http 拦截器已 toast（含 E-WF-028），无需重复提示
  }
  finally { saving.value = false }
}
</script>

<style scoped>
.auth-hint { font-size: 12px; opacity: 0.75; margin-top: 4px; }
</style>
