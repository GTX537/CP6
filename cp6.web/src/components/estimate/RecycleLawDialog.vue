<template>
  <el-dialog
    :model-value="modelValue"
    @update:model-value="(v: boolean) => emit('update:modelValue', v)"
    :title="title"
    width="720"
    :close-on-click-modal="false"
    draggable
  >
    <el-alert
      :title="alertMsg"
      type="info"
      :closable="false"
      style="margin-bottom: 12px"
    />

    <!-- 法 A：回収責任（回収事業者選択） -->
    <div v-if="type === 'A'">
      <el-form :model="formA" label-width="120px" size="small">
        <el-form-item :label="t('回収事業者')">
          <el-select v-model="formA.operatorCd" clearable style="width: 100%">
            <el-option value="OP01" :label="t('OP01 全国容器リサイクル協会')" />
            <el-option value="OP02" label="OP02 JCRC" />
            <el-option value="OP03" :label="t('OP03 自社回収')" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('識別表示')">
          <el-radio-group v-model="formA.idMarkType">
            <el-radio value="紙">{{ t('紙マーク') }}</el-radio>
            <el-radio value="プラ">{{ t('プラマーク') }}</el-radio>
            <el-radio value="両方">{{ t('両方') }}</el-radio>
          </el-radio-group>
        </el-form-item>
        <el-form-item :label="t('払込単価')">
          <el-input-number v-model="formA.paymentUnitPrice" :min="0" :precision="3" :controls="false" style="width: 100%" />
        </el-form-item>
        <el-form-item :label="t('備考')">
          <el-input v-model="formA.note" type="textarea" :rows="2" />
        </el-form-item>
      </el-form>
    </div>

    <!-- 法 B：素材品目選択 -->
    <div v-if="type === 'B'">
      <el-table :data="formB.items" border stripe size="small" style="width: 100%" max-height="320">
        <el-table-column type="selection" width="40" />
        <el-table-column prop="code" :label="t('コード')" width="100" />
        <el-table-column prop="name" :label="t('素材名')" min-width="160" />
        <el-table-column prop="rate" :label="t('係数')" width="100" align="right">
          <template #default="{ row }">{{ formatQty(row.rate, 3) }}</template>
        </el-table-column>
        <el-table-column :label="t('数量')" width="140">
          <template #default="{ row }">
            <el-input-number v-model="row.qty" :min="0" :precision="2" :controls="false" size="small" style="width: 100%" />
          </template>
        </el-table-column>
      </el-table>
    </div>

    <!-- 法 C：段成率 × 原紙単価 -->
    <div v-if="type === 'C'">
      <el-form :model="formC" label-width="120px" size="small">
        <el-form-item :label="t('原紙コード')">
          <el-select v-model="formC.paperCd" clearable style="width: 100%" @change="onPaperChange">
            <el-option
              v-for="p in paperCodes"
              :key="p.code"
              :value="p.code"
              :label="t('{code} {name}（単価 {price}）', { code: p.code, name: p.name, price: p.num1 ?? 0 })"
            />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('段成率')">
          <el-input-number v-model="formC.yieldRate" :min="0.01" :precision="3" :step="0.01" :controls="false" style="width: 100%" />
        </el-form-item>
        <el-form-item :label="t('原紙単価')">
          <el-input-number v-model="formC.paperUnitPrice" :min="0" :precision="3" :controls="false" style="width: 100%" />
        </el-form-item>
        <el-form-item :label="t('シート面積 (m²)')">
          <el-input-number v-model="formC.sheetSqm" :min="0" :precision="4" :controls="false" style="width: 100%" />
        </el-form-item>
        <el-form-item :label="t('計算原価')">
          <el-input :model-value="fmtMoney(computedCost)" readonly>
            <template #append>{{ t('円/枚') }}</template>
          </el-input>
        </el-form-item>
      </el-form>
    </div>

    <template #footer>
      <el-button @click="onCancel">{{ t('キャンセル') }}</el-button>
      <el-button type="primary" @click="onConfirm">{{ t('OK（反映）') }}</el-button>
    </template>
  </el-dialog>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { masterApi } from '@/api/erp/master'
import { formatQty, formatNumber } from '@/utils/format'
import type { MasterGenericCode } from '@/types/erp/estimateCalc'

const { t } = useI18n()

interface Props {
  modelValue: boolean
  type: 'A' | 'B' | 'C'
}
const props = defineProps<Props>()
const emit = defineEmits<{
  (e: 'update:modelValue', v: boolean): void
  (e: 'confirm', payload: any): void
}>()

const title = computed(() => {
  switch (props.type) {
    case 'A': return t('リサイクル法 A - 回収責任')
    case 'B': return t('リサイクル法 B - 素材区分')
    case 'C': return t('リサイクル法 C - 段成率原価')
    default: return t('リサイクル法')
  }
})
const alertMsg = computed(() => {
  switch (props.type) {
    case 'A': return t('識別表示・払込責任の指定。法律遵守を確認してください。')
    case 'B': return t('容器包装リサイクル法対象素材の選択と数量入力。')
    case 'C': return t('段ボール原紙の段成率と単価による原価計算。')
    default: return ''
  }
})

// ============ 法 A ============
const formA = ref({
  operatorCd: '',
  idMarkType: '紙',
  paymentUnitPrice: 0,
  note: '',
})

// ============ 法 B ============
const formB = ref({
  items: [
    { code: 'M01', name: '紙', rate: 1.0, qty: 0 },
    { code: 'M02', name: 'PE', rate: 0.85, qty: 0 },
    { code: 'M03', name: 'PP', rate: 0.75, qty: 0 },
    { code: 'M04', name: 'PET', rate: 0.6, qty: 0 },
    { code: 'M05', name: 'アルミ', rate: 0.5, qty: 0 },
  ] as Array<{ code: string; name: string; rate: number; qty: number }>,
})

// ============ 法 C ============
const formC = ref({
  paperCd: '',
  yieldRate: 1.0,
  paperUnitPrice: 0,
  sheetSqm: 0,
})
const paperCodes = ref<MasterGenericCode[]>([])

const computedCost = computed(() => {
  const c = formC.value
  if (!c.paperUnitPrice || !c.sheetSqm) return 0
  return (c.paperUnitPrice * c.sheetSqm) / (c.yieldRate || 1)
})

function fmtMoney(v: number) {
  return formatNumber(v, 'decimal')
}

function onPaperChange() {
  const m = paperCodes.value.find((p) => p.code === formC.value.paperCd)
  if (m?.num1) formC.value.paperUnitPrice = Number(m.num1)
}

function onCancel() {
  emit('update:modelValue', false)
}

/** 向父组件回传数据 */
function onConfirm() {
  let payload: any = {}
  if (props.type === 'A') {
    payload = {
      ...formA.value,
      items: [{ name: t('回収:{operator}', { operator: formA.value.operatorCd }), note: formA.value.note }],
    }
  } else if (props.type === 'B') {
    const picked = formB.value.items.filter((x) => x.qty > 0)
    payload = {
      items: picked.map((x) => ({ name: `${x.code} ${x.name}`, note: `qty=${x.qty} rate=${x.rate}` })),
      totalQty: picked.reduce((s, x) => s + x.qty * x.rate, 0),
    }
  } else {
    payload = {
      ...formC.value,
      computedCost: computedCost.value,
      items: [{ name: t('{paper} 段成率{rate}', { paper: formC.value.paperCd, rate: formC.value.yieldRate }), note: t('原価={cost}', { cost: fmtMoney(computedCost.value) }) }],
    }
  }
  emit('confirm', payload)
  emit('update:modelValue', false)
}

// 打开时重置状态（仅当再次打开对应 type）
watch(
  () => props.modelValue,
  (v) => {
    if (v && props.type === 'C' && paperCodes.value.length === 0) {
      masterApi.getGenericCodes('Paper').then((r) => (paperCodes.value = r.data ?? []))
    }
  }
)

onMounted(() => {
  if (props.type === 'C') {
    masterApi.getGenericCodes('Paper').then((r) => (paperCodes.value = r.data ?? []))
  }
})
</script>
