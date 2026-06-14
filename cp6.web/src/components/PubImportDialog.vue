<template>
  <el-dialog v-model="visible" :title="title || t('数据导入')" width="640px" @open="reset">
    <el-steps :active="step" finish-status="success" simple style="margin-bottom: 16px">
      <el-step :title="t('下载模板')" />
      <el-step :title="t('上传文件')" />
      <el-step :title="t('校验结果')" />
    </el-steps>

    <!-- 步骤1：下载模板 -->
    <div v-if="step === 0" style="text-align: center; padding: 20px">
      <p style="color: #909399; margin-bottom: 16px">{{ t('先下载模板，按列填写（必填列标 *），再上传。') }}</p>
      <el-button type="primary" @click="onDownloadTemplate">{{ t('下载导入模板') }}</el-button>
      <div style="margin-top: 16px">
        <el-button @click="step = 1">{{ t('下一步：上传') }}</el-button>
      </div>
    </div>

    <!-- 步骤2：上传 -->
    <div v-else-if="step === 1" style="padding: 10px">
      <el-upload
        drag
        :auto-upload="false"
        :limit="1"
        :on-change="onFileChange"
        :on-exceed="onExceed"
        accept=".xlsx,.xls"
      >
        <el-icon class="el-icon--upload"><UploadFilled /></el-icon>
        <div class="el-upload__text">{{ t('拖拽 Excel 到此或') }}<em>{{ t('点击选择') }}</em></div>
      </el-upload>
      <div style="margin-top: 16px; text-align: right">
        <el-button @click="step = 0">{{ t('上一步') }}</el-button>
        <el-button type="primary" :disabled="!file" :loading="importing" @click="doImport">{{ t('开始导入') }}</el-button>
      </div>
    </div>

    <!-- 步骤3：结果 -->
    <div v-else style="padding: 10px">
      <el-result
        :icon="result && result.failed > 0 ? 'warning' : 'success'"
        :title="t('成功 {ok} 行，失败 {fail} 行', { ok: result?.success ?? 0, fail: result?.failed ?? 0 })"
      />
      <el-table v-if="result?.errors?.length" :data="result.errors" border size="small" max-height="240">
        <el-table-column prop="row" :label="t('行号')" width="80" />
        <el-table-column prop="message" :label="t('错误原因')" show-overflow-tooltip />
      </el-table>
      <div style="margin-top: 16px; text-align: right">
        <el-button v-if="result?.errorFileUrl" type="warning" @click="downloadErrorFile">{{ t('下载错误文件') }}</el-button>
        <el-button @click="step = 1">{{ t('重新上传') }}</el-button>
        <el-button type="primary" @click="finish">{{ t('完成') }}</el-button>
      </div>
    </div>
  </el-dialog>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { UploadFilled } from '@element-plus/icons-vue'
import http from '@/api/http'
import { usePubExcel } from '@/composables/usePubExcel'

const { t } = useI18n()

interface ImportResultView {
  success: number
  failed: number
  errors: { row: number; message: string }[]
  errorFileUrl?: string
}

const props = defineProps<{
  title?: string
  templateUrl: string   // GET 模板下载
  importUrl: string     // POST multipart 导入
}>()

const visible = defineModel<boolean>('visible', { default: false })
const emit = defineEmits<{ done: [] }>()

const { downloadTemplate } = usePubExcel()
const step = ref(0)
const file = ref<File | null>(null)
const importing = ref(false)
const result = ref<ImportResultView | null>(null)

function reset() {
  step.value = 0
  file.value = null
  result.value = null
}

function onDownloadTemplate() {
  return downloadTemplate(props.templateUrl)
}

function onFileChange(f: any) {
  file.value = f.raw
}
function onExceed(files: any) {
  file.value = files[0]
}

async function doImport() {
  if (!file.value) return
  importing.value = true
  try {
    const fd = new FormData()
    fd.append('file', file.value)
    const res: any = await http.post(props.importUrl, fd)
    result.value = (res.data ?? res) as ImportResultView
    step.value = 2
  } finally {
    importing.value = false
  }
}

async function downloadErrorFile() {
  if (!result.value?.errorFileUrl) return
  const blob: any = await http.get(result.value.errorFileUrl, { responseType: 'blob' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = t('导入错误.xlsx')
  a.click()
  URL.revokeObjectURL(url)
}

function finish() {
  ElMessage.success(t('导入完成'))
  visible.value = false
  emit('done')
}
</script>
