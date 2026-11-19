<template>
  <form class="draft-form" @submit.prevent="$emit('save')">
    <div class="fields">
      <label>科目编码<input v-model.trim="draft.code" maxlength="30" required :readonly="mode !== 'create'" /></label>
      <label>科目名称<input v-model.trim="draft.content.name" maxlength="100" required /></label>
      <label>会计大类
        <select v-model.number="draft.content.type" required>
          <option disabled :value="0">请选择</option>
          <option v-for="(name, index) in types" :key="name" :value="index + 1">{{ name }}</option>
        </select>
      </label>
      <label>正常方向
        <select v-model.number="draft.content.normalSide" required>
          <option disabled :value="0">请选择</option>
          <option :value="1">借</option><option :value="2">贷</option>
        </select>
      </label>
      <label>有效起日<input v-model="draft.content.from" type="date" :min="minDate" required /></label>
      <label>有效止日（不含）<input v-model="draft.content.toExclusive" type="date" :min="draft.content.from" /></label>
      <label>上级科目
        <select v-model="draft.content.parentRootId">
          <option :value="null">无上级</option>
          <option v-if="draft.content.parentRootId && !parents.some(parent => parent.id === draft.content.parentRootId)"
            :value="draft.content.parentRootId">保留原上级，待校验</option>
          <option v-for="parent in parents" :key="parent.id" :value="parent.id">{{ parent.code }} · {{ parent.name }}</option>
        </select>
      </label>
      <label>过账类别
        <select v-model="draft.content.postingClass" required @change="changeClass">
          <option value="" disabled>请选择</option>
          <option value="GROUP">分组科目</option><option value="POSTABLE">记账科目</option>
        </select>
      </label>
      <label>科目角色
        <select v-model="draft.content.roleKey" :disabled="draft.content.postingClass === 'GROUP'">
          <option :value="null">无</option>
          <option v-for="role in roles" :key="role">{{ role }}</option>
        </select>
      </label>
      <label>币种规则
        <select :value="draft.content.currency?.mode ?? ''" required @change="changeCurrency">
          <option value="" disabled>请选择</option>
          <option value="BASE_ONLY">账套本位币（{{ baseCurrency }}）</option>
          <option value="SINGLE">单一币种</option><option value="MULTI">多币种</option>
        </select>
      </label>
      <label v-if="draft.content.currency?.mode === 'SINGLE'">单一币种
        <select :value="draft.content.currency.codes[0] ?? ''" required @change="singleCurrency">
          <option value="" disabled>请选择</option>
          <option v-for="currency in currencies" :key="currency">{{ currency }}</option>
        </select>
      </label>
      <fieldset v-if="draft.content.currency?.mode === 'MULTI'" class="currencies">
        <legend>多币种（至少两种）</legend>
        <label v-for="currency in currencies" :key="currency">
          <input v-model="draft.content.currency.codes" type="checkbox" :value="currency" />{{ currency }}
        </label>
      </fieldset>
      <label class="check"><input v-model="draft.content.isControl" type="checkbox"
        :disabled="draft.content.postingClass === 'GROUP'" @change="changeControl" />控制科目</label>
      <label v-if="draft.content.isControl">子账类型
        <select v-model="draft.content.subLedgerType" required>
          <option :value="null" disabled>请选择</option>
          <option v-for="kind in ['AP', 'AR', 'INV', 'COST']" :key="kind">{{ kind }}</option>
        </select>
      </label>
      <label class="check"><input v-model="draft.content.requirePartner" type="checkbox"
        :disabled="draft.content.postingClass === 'GROUP'" />记账时要求往来单位资格</label>
      <label v-if="firstRelease" class="check"><input v-model="draft.activate" type="checkbox" />申请首次发布时启用</label>
    </div>
    <label>操作原因<input v-model.trim="draft.reason" maxlength="256" required /></label>
    <div class="actions">
      <el-button :disabled="busy" @click="$emit('close')">取消</el-button>
      <el-button type="primary" native-type="submit" :loading="busy">保存草稿</el-button>
    </div>
  </form>
</template>

<script setup lang="ts">
import type { GlContent } from '@/api/fin/glAccountWorkflow'

defineProps<{
  mode: 'create' | 'revise' | 'successor'
  minDate: string
  firstRelease: boolean
  baseCurrency: string
  roles: string[]
  currencies: string[]
  parents: { id: string; code: string; name: string }[]
  busy: boolean
}>()
defineEmits<{ save: []; close: [] }>()
const draft = defineModel<{ code: string; content: GlContent; activate: boolean; reason: string }>({ required: true })
const types = ['资产', '负债', '权益', '收入', '费用']

function changeControl() {
  if (!draft.value.content.isControl) draft.value.content.subLedgerType = null
}
function changeClass() {
  if (draft.value.content.postingClass !== 'GROUP') return
  draft.value.content.isControl = false
  draft.value.content.requirePartner = false
  draft.value.content.roleKey = null
  changeControl()
}
function changeCurrency(event: Event) {
  const mode = (event.target as HTMLSelectElement).value
  if (mode === 'BASE_ONLY' || mode === 'SINGLE' || mode === 'MULTI') draft.value.content.currency = { mode, codes: [] }
}
function singleCurrency(event: Event) {
  if (draft.value.content.currency?.mode === 'SINGLE')
    draft.value.content.currency.codes = [(event.target as HTMLSelectElement).value]
}
</script>

<style scoped>
.draft-form, .fields { display: grid; gap: 14px; }
.fields { grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); }
label { display: grid; gap: 5px; }
input:not([type='checkbox']), select { min-width: 0; padding: 7px 9px; font: inherit; border: 1px solid var(--el-border-color); border-radius: 4px; }
.check, .currencies label { display: flex; gap: 8px; align-items: center; }
.currencies { border: 1px solid var(--el-border-color); min-width: 0; }
.actions { display: flex; justify-content: flex-end; }
</style>
