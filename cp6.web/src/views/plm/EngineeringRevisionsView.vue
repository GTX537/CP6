<template>
  <div class="revision-page" :aria-busy="busy">
    <header><div><h1>工程修订与工作快照</h1><p>逐次保存设计草稿及修改原因，保留每个组成位置和历史版本。</p></div><button :disabled="busy" @click="reload">刷新</button></header>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <section v-if="pending" aria-label="原操作恢复"><p>上次保存结果尚未确认。</p><button :disabled="busy" @click="recover(false)">查询原操作</button><button :disabled="busy" @click="recover(true)">结束未提交操作</button></section>
    <section v-if="scope && canRead" aria-label="冲突清单回读">
      <form class="inline" @submit.prevent="loadConflict">
        <label>冲突原操作键<input v-model.trim="conflictKey" aria-label="冲突原操作键" maxlength="36" required :disabled="busy" /></label>
        <button :disabled="busy || !conflictKey">读取冲突清单</button>
      </form>
      <div v-if="conflict" aria-label="已保存冲突清单">
        <h2>已保存冲突清单</h2>
        <p>原操作 {{ conflict.operationId }} · {{ conflict.action === 'REBASE' ? '重基' : '工作草稿修改' }} · {{ conflict.recordedAt }}</p>
        <p>原操作理由：{{ conflict.reason }}</p>
        <details>
          <summary>准确版本与来源</summary>
          <dl class="refs">
            <dt>提交时依据的修订</dt><dd>{{ refKey(conflict.expected.revision) }}</dd>
            <dt>提交时依据的候选</dt><dd>{{ refKey(conflict.expected.candidate) }}</dd>
            <dt>拒绝时的修订</dt><dd>{{ refKey(conflict.current.revision) }}</dd>
            <dt>拒绝时的候选</dt><dd>{{ refKey(conflict.current.candidate) }}</dd>
            <dt>请求的 ETag</dt><dd>{{ conflict.requestedEtag }}</dd>
            <dt>请求版本的原 ETag</dt><dd>{{ conflict.expected.etag }}</dd>
            <dt>拒绝时的 ETag</dt><dd>{{ conflict.current.etag }}</dd>
            <template v-if="conflict.sources">
              <dt>重基来源</dt><dd>{{ refKey(conflict.sources.base) }}</dd>
              <dt>传入来源</dt><dd>{{ refKey(conflict.sources.incoming) }}</dd>
            </template>
          </dl>
        </details>
        <p v-if="!conflict.items.length">工作版本或 ETag 已变化，相关字段值一致。</p>
        <div v-else class="table-scroll"><table>
          <thead><tr><th>发生位置与字段</th><th>提交所依据的值</th><th>已保存值</th><th>尝试保存的值</th><th>理由</th></tr></thead>
          <tbody><tr v-for="item in conflict.items" :key="item.path">
            <td><code>{{ item.path }}</code><p>{{ item.conflict ? '双方改动冲突' : '字段差异' }}</p></td>
            <td><pre>{{ displayValue(item.expectedValue) }}</pre><code v-if="item.expectedSource">{{ refKey(item.expectedSource) }}</code></td>
            <td><pre>{{ displayValue(item.currentValue) }}</pre><code v-if="item.currentSource">{{ refKey(item.currentSource) }}</code></td>
            <td><pre>{{ displayValue(item.attemptedValue) }}</pre><code v-if="item.attemptedSource">{{ refKey(item.attemptedSource) }}</code></td>
            <td>{{ item.reason }}</td>
          </tr></tbody>
        </table></div>
      </div>
    </section>
    <section>
      <header><h2>修订目录</h2><button v-if="permissions.has('plm-engineering-revision:add')" :disabled="locked || !scope" @click="openNew">新建修订</button></header>
      <form class="inline" @submit.prevent="loadList(1)"><label>修订编号<input v-model="search" aria-label="修订编号搜索" maxlength="256" :disabled="busy" /></label><button :disabled="busy || !canRead">搜索</button></form>
      <div class="table-scroll"><table><thead><tr><th>修订编号</th><th>工作版本</th><th>设计目标</th><th>操作</th></tr></thead><tbody>
        <tr v-for="row in rows" :key="row.reference.id"><td>{{ row.revisionCode }}</td><td>{{ row.reference.version }}</td><td>{{ row.draft.goal }}</td><td><button :disabled="busy" @click="open(row)">打开</button></td></tr>
      </tbody></table></div><p v-if="!rows.length">当前范围内没有工程修订。</p>
      <nav class="inline" aria-label="修订分页"><button :disabled="busy || page === 1 || !canRead" @click="loadList(page - 1)">上一页</button><span>第 {{ page }} 页</span><button :disabled="busy || rows.length < 20 || !canRead" @click="loadList(page + 1)">下一页</button></nav>
    </section>
    <section v-if="creating || current">
      <h2>{{ creating ? '创建工程修订草稿' : current?.revisionCode }}</h2>
      <p v-if="current">工作版本 {{ current.reference.version }} · {{ current.state === 'FROZEN' ? '已冻结，请在工程基线中开始后继设计' : current.state === 'IN_REVIEW' ? '在审 · 当前工作版已锁定' : '草稿 · 尚未冻结' }}</p>
      <p v-if="current?.origin.parentBaselineRef">创建时父基线 {{ current.origin.parentBaselineRef.id }} · v{{ current.origin.parentBaselineRef.version }}</p>
      <p v-if="current?.origin.changeRef">变更原引用 {{ current.origin.changeRef.id }} · v{{ current.origin.changeRef.version }} · {{ current.origin.changeRef.digest }}</p>
      <p>此处保存工作草稿。配置规则、完整组成来源和适用性核准齐备后，才能进入后续评审。</p>
      <ul v-if="current?.missing.length" aria-label="草稿缺项"><li v-for="item in current.missing" :key="item">{{ missingText(item) }}</li></ul>
      <form @submit.prevent="saveGoal"><fieldset :disabled="locked || (!creating && !canEdit)"><div class="grid">
        <label>设计目标<textarea v-model="goal" aria-label="设计目标" maxlength="8000" required /></label>
        <label v-if="creating && !useLegacy">无父基线原因<textarea v-model="noParentReason" aria-label="无父基线原因" maxlength="8000" required /></label>
        <label v-if="creating && creation">所属部门<select v-model="department" aria-label="修订所属部门" :required="!creation.allowsUnbound"><option value="">{{ creation.allowsUnbound ? '不绑定部门' : '请选择授权部门' }}</option><option v-for="d in creation.departments" :key="d.id" :value="d.id">{{ d.code }} / {{ d.name }}</option></select></label>
        <label>修改原因<textarea v-model="reason" aria-label="修改原因" maxlength="8000" required /></label>
      </div><button>{{ creating ? '创建草稿' : '保存设计目标' }}</button></fieldset></form>
      <p>工程产品：{{ current?.draft.productRef ? `${current.draft.productRef.id} · 来源版本 ${current.draft.productRef.version}` : '尚未解析，可继续保存草稿' }}。绑定后保留同一产品身份。</p>
      <form class="inline" @submit.prevent="findProducts"><label>工程产品搜索<input v-model="productSearch" aria-label="工程产品搜索" :disabled="busy" /></label><button :disabled="busy || !permissions.has('plm-engineering-product:query')">查找工程产品</button></form>
      <div class="inline"><label>本修订的工程产品<select v-model="rootProduct" aria-label="本修订的工程产品" :disabled="locked"><option value="">请选择准确来源；新草稿也可暂不选择</option><option v-for="row in products.filter(p => !current?.draft.productRef || p.reference.id === current.draft.productRef.id)" :key="refKey(row.reference)" :value="refKey(row.reference)">{{ row.product.code }} / {{ row.product.name }} · {{ row.reference.id }} · v{{ row.reference.version }}</option></select></label>
        <button v-if="current && canEdit" :disabled="locked || !rootProduct" @click="bindProduct">保存产品引用</button></div>
    </section>
    <section v-if="creating || legacy || current?.origin.legacyRef">
      <h2>历史工程来源</h2>
      <div class="inline">
        <label>登记编号<input v-model="legacyId" aria-label="历史来源登记编号" :disabled="busy" /></label>
        <button :disabled="busy || !legacyId" @click="openLegacy">读取历史来源</button>
      </div>
      <div v-if="legacy">
        <p>登记编号 {{ legacy.reference.id }} · 原记录 {{ legacy.original.registration.sourceRecordRef }} · 证据不足</p>
        <p>原件摘要 {{ legacy.original.registration.sourceHash }}</p>
        <ul><li v-for="(gap, index) in legacy.original.registration.evidenceGaps" :key="index">{{ gap }}</li></ul>
        <label v-if="creating"><input v-model="useLegacy" type="checkbox" :disabled="locked" />采用此来源创建新的复核草稿</label>
      </div>
      <form v-if="creating && permissions.has('plm-engineering-revision:legacy-register')" @submit.prevent="registerLegacy">
        <fieldset :disabled="locked || !rootProduct" class="grid">
          <label>来源提交 SHA<input v-model="legacyForm.repo" aria-label="来源提交 SHA" pattern="[0-9a-f]{40}" required /></label>
          <label>原记录键<input v-model="legacyForm.record" aria-label="历史原记录键" maxlength="512" required /></label>
          <label>来源路径<input v-model="legacyForm.path" maxlength="2048" /></label>
          <label>原件 SHA-256<input v-model="legacyForm.digest" aria-label="历史原件 SHA-256" pattern="[0-9a-f]{64}" required /></label>
          <label>证据缺项（每行一项）<textarea v-model="legacyForm.gaps" aria-label="历史证据缺项" required /></label>
          <label>登记理由<textarea v-model="legacyForm.reason" aria-label="历史登记理由" maxlength="2048" required /></label>
          <button>登记所选产品的历史来源</button>
        </fieldset>
      </form>
    </section>
    <section v-if="current"><h2>配置草稿</h2><p>选项值保持原样；小数按文本保存。可明确选择当前获准的选项规则，完整适用范围仍需核准。</p>
      <p v-if="current.draft.configuration?.optionCheck" role="status">选项检查：{{ optionStatus(current.draft.configuration.optionCheck.status) }}；规则版本 {{ current.draft.configuration.optionCheck.ruleRef.version }}。仅覆盖选项约束。</p>
      <ul v-if="current.draft.configuration?.optionCheck?.findings.length"><li v-for="(f, i) in current.draft.configuration.optionCheck.findings" :key="i">{{ findingText(f.code) }}：{{ f.name }}</li></ul>
      <button :disabled="busy || !current.draft.productRef || !permissions.has('plm-configuration-rule:query')" @click="findRules">查找获准选项规则</button>
      <label>获准选项规则<select v-model="ruleSelection" aria-label="获准选项规则" :disabled="locked"><option value="">{{ configuration.ruleRef ? `保留规则版本 ${configuration.ruleRef.version}` : '暂不选择规则' }}</option><option v-for="r in rules" :key="refKey(r.reference)" :value="refKey(r.reference)">{{ r.body.name }} · v{{ r.reference.version }} · {{ r.reference.id }}</option></select></label>
      <button :disabled="locked || !canEdit" @click="configuration.ruleRef = null; ruleSelection = ''">清除所选规则</button>
      <form @submit.prevent="saveConfiguration"><fieldset :disabled="locked || !canEdit"><div class="grid">
        <label>配置适用性<select v-model="configuration.applicability" aria-label="配置适用性"><option value="UNKNOWN">待确定</option><option value="APPLIES">拟适用</option><option value="NOT_APPLICABLE">拟不适用</option></select></label>
        <label>配置说明<textarea v-model="configuration.reason" aria-label="配置说明" :required="configuration.applicability === 'NOT_APPLICABLE'" /></label>
      </div><div v-for="(option, index) in configuration.options" :key="index" class="inline">
        <label>选项名<input v-model="option.name" :aria-label="`选项名 ${index + 1}`" required maxlength="256" /></label>
        <label>类型<select :value="option.type" :aria-label="`选项类型 ${index + 1}`" @change="optionType(index, ($event.target as HTMLSelectElement).value)"><option value="STRING">文本</option><option value="DECIMAL">小数</option><option value="BOOLEAN">是 / 否</option></select></label>
        <label v-if="option.type === 'STRING'">值<input v-model="option.text" :aria-label="`选项值 ${index + 1}`" /></label>
        <label v-else-if="option.type === 'DECIMAL'">精确小数<input v-model="option.decimal" :aria-label="`选项值 ${index + 1}`" inputmode="decimal" required /></label>
        <label v-else>值<select :value="option.boolean == null ? '' : String(option.boolean)" :aria-label="`选项值 ${index + 1}`" required @change="option.boolean = ($event.target as HTMLSelectElement).value === '' ? null : ($event.target as HTMLSelectElement).value === 'true'"><option value="">请选择</option><option value="true">是</option><option value="false">否</option></select></label>
        <button type="button" @click="configuration.options.splice(index, 1)">移除选项</button>
      </div><button type="button" @click="configuration.options.push({ name: '', type: 'STRING', text: '' })">添加选项</button><button>保存配置草稿</button><button type="button" @click="clearConfiguration">清除配置草稿</button></fieldset></form>
    </section>
    <section v-if="current"><header><h2>组成位置</h2><button v-if="canEdit" :disabled="locked" @click="newComponent">添加组成位置</button></header>
      <div class="table-scroll"><table><thead><tr><th>位置 / 顺序</th><th>内容</th><th>来源</th><th>操作</th></tr></thead><tbody><tr v-for="item in current.draft.components" :key="item.occurrenceId"><td>{{ item.path }} / {{ item.semanticOrder }}</td><td>{{ kindNames[item.kind] }}</td><td>{{ item.sourceRef ? `${item.sourceRef.id} · v${item.sourceRef.version}` : '来源待补齐' }}</td><td><button v-if="canEdit" :disabled="locked" @click="editComponent(item)">编辑位置</button><button v-if="canEdit" :disabled="locked" @click="removeComponent(item)">移除位置</button></td></tr></tbody></table></div>
      <form v-if="component" @submit.prevent="saveComponent"><fieldset :disabled="locked || !canEdit"><div class="grid">
        <label>位置路径<input v-model="component.path" aria-label="位置路径" maxlength="2048" required /></label><label>语义顺序<input v-model.number="component.semanticOrder" aria-label="语义顺序" type="number" min="0" step="1" required /></label>
        <label>内容类型<select v-model="component.kind" aria-label="组成类型" @change="component.sourceRef = null; componentSource = ''"><option v-for="(label, value) in kindNames" :key="value" :value="value">{{ label }}</option></select></label>
        <label>适用性<select v-model="component.applicability" aria-label="组成适用性"><option value="UNKNOWN">待确定</option><option value="APPLIES">拟适用</option><option value="NOT_APPLICABLE">拟不适用</option></select></label>
        <label>适用性说明<textarea v-model="component.reason" aria-label="组成适用性说明" :required="component.applicability === 'NOT_APPLICABLE'" /></label><label>说明<textarea v-model="component.note" aria-label="组成说明" /></label>
        <label v-if="component.kind === 'PRODUCT'">工程产品来源<select v-model="componentSource" aria-label="组成来源"><option value="">{{ component.sourceRef ? '保留已记录来源' : '来源待补齐' }}</option><option v-for="row in products" :key="refKey(row.reference)" :value="refKey(row.reference)">{{ row.product.code }} / {{ row.product.name }} · {{ row.reference.id }} · v{{ row.reference.version }}</option></select></label>
        <div v-if="component.kind === 'QUALITY_SPEC'"><label>质量规格搜索<input v-model="qualitySearch" aria-label="质量规格搜索" /></label><button type="button" :disabled="!permissions.has('quality-specification:query')" @click="findQuality(1)">查找已批准质量规格</button><button v-if="qualityMore" type="button" @click="findQuality(qualityPage + 1)">更多质量规格</button><label>质量规格来源<select v-model="componentSource" aria-label="组成质量规格来源"><option value="">{{ component.sourceRef ? '保留已记录规格版本' : '请选择已批准的准确版本' }}</option><option v-for="q in qualitySources" :key="refKey(q.version.reference)" :value="refKey(q.version.reference)">{{ q.version.body.name }} · v{{ q.version.reference.version }}</option></select></label><p>保存时会核对规格当前批准与完整来源，工程草稿无需预先冻结。</p></div>
        <div v-if="component.kind === 'DOCUMENT'"><button type="button" :disabled="!permissions.has('plm-controlled-document:query')" @click="findDocuments">查找本修订文档</button><label>文档来源<select v-model="componentSource" aria-label="组成文档来源"><option value="">{{ component.sourceRef ? '保留已记录文档版本' : '请选择准确文档版本' }}</option><option v-for="d in documents" :key="refKey(d.reference)" :value="refKey(d.reference)">{{ d.body.name }} · {{ d.body.revisionLabel }} · v{{ d.reference.version }}</option></select></label><p>文档工作版本可加入组成；专业批准与用途许可仍需完成。</p></div>
        <div v-if="component.kind === 'ROUTING'"><button type="button" :disabled="!permissions.has('plm-engineering-routing:query')" @click="findRoutings">查找本修订工艺</button><label>工艺来源<select v-model="componentSource" aria-label="组成工艺来源"><option value="">{{ component.sourceRef ? '保留已记录工艺版本' : '请选择准确工艺版本' }}</option><option v-for="r in routings" :key="refKey(r.reference)" :value="refKey(r.reference)">{{ r.body.name }} · v{{ r.reference.version }} · {{ r.reference.id }}</option></select></label></div>
        <div v-if="component.kind === 'EBOM' || component.kind === 'MBOM'"><button type="button" :disabled="!permissions.has('plm-engineering-bom:query')" @click="findBoms">查找本修订 BOM</button><label>结构来源<select v-model="componentSource" aria-label="组成BOM来源"><option value="">{{ component.sourceRef ? '保留已记录结构版本' : '请选择准确结构版本' }}</option><option v-for="b in boms" :key="refKey(b.reference)" :value="refKey(b.reference)">{{ b.body.kind }} / {{ b.body.name }} · v{{ b.reference.version }}</option></select></label><p>准确结构版本可以加入组成；结构评审、制造映射和冻结仍需完成。</p></div>
      </div><p v-if="component.kind === 'ROUTING'">工艺草稿的准确版本可加入组成；工艺缺项、评审与冻结仍需完成。</p><p v-else-if="!['PRODUCT', 'EBOM', 'MBOM', 'DOCUMENT'].includes(component.kind)">该内容的正式来源入口尚未接入。可记录缺项说明，后续补齐准确来源。</p>
      <button type="button" @click="component.sourceRef = null; componentSource = ''">清除来源引用</button><button>保存组成位置</button><button type="button" @click="component = null">取消编辑位置</button></fieldset></form>
    </section>
    <EngineeringSourceCheckView v-if="current" :revision="current" :blocked="busy || !!pending" @pending="checkPending = $event" />
    <section v-if="current?.draft.productRef && canEdit">
      <h2>三方差异与重基</h2>
      <p v-if="current.compositionBase">当前组成基准 {{ current.compositionBase.id }} · v{{ current.compositionBase.version }}</p>
      <fieldset :disabled="locked" class="grid">
        <label v-if="!current.compositionBase">本修订的基准工作版本<input v-model="baseVersion" aria-label="基准工作版本" inputmode="numeric" @input="rebasePreview = null" /></label>
        <label>传入对象类型<select v-model="incomingKind" aria-label="传入对象类型" @change="rebasePreview = null"><option value="candidate">工程修订的工作候选</option><option value="baseline">准确的已冻结基线</option></select></label>
        <label>传入对象编号<input v-model="incomingId" aria-label="传入对象编号" @input="rebasePreview = null" /></label>
        <label v-if="incomingKind === 'candidate'">传入工作版本<input v-model="incomingVersion" aria-label="传入工作版本" inputmode="numeric" @input="rebasePreview = null" /></label>
        <button type="button" :disabled="!incomingId || (!current.compositionBase && !baseVersion) || (incomingKind === 'candidate' && !incomingVersion)" @click="previewRebase">预览三方差异</button>
      </fieldset>
      <div v-if="rebasePreview">
        <p>基准 {{ rebasePreview.sources.base.id }} · v{{ rebasePreview.sources.base.version }}；传入 {{ rebasePreview.sources.incoming.id }} · v{{ rebasePreview.sources.incoming.version }}</p>
        <p>工程 BOM、制造 BOM、工艺路线和受控文档须属于目标工程修订。跨修订重基时，请选用已绑定到目标修订的准确专业版本。</p>
        <p v-if="rebasePreview.baseMissing.length">基准来源状态：{{ rebasePreview.baseMissing.map(missingText).join('；') }}</p>
        <p v-if="rebasePreview.incomingMissing.length">传入来源状态：{{ rebasePreview.incomingMissing.map(missingText).join('；') }}</p>
        <p v-if="!rebasePreview.changes.length">组成字段一致。保存会追加工作版及来源记录。</p>
        <fieldset :disabled="locked">
          <div v-for="change in rebasePreview.changes" :key="change.path" class="rebase-item">
            <h3>{{ change.path }}<span v-if="change.conflict"> · 双方变更冲突</span></h3>
            <div class="grid"><div>基准<pre>{{ displayValue(change.baseValue) }}</pre></div><div>当前<pre>{{ displayValue(change.currentValue) }}</pre></div><div>传入<pre>{{ displayValue(change.incomingValue) }}</pre></div></div>
            <div class="grid" v-if="choices[change.path]">
              <label>决定<select v-model="choices[change.path]!.decision" :aria-label="`决定 ${change.path}`"><option value="">请选择</option><option value="KEEP_BASE">保留基准值</option><option value="ACCEPT_SOURCE">采用传入值</option><option value="MANUAL">人工确定</option></select></label>
              <label>决定理由<input v-model="choices[change.path]!.reason" :aria-label="`决定理由 ${change.path}`" maxlength="8000" /></label>
              <label v-if="choices[change.path]!.decision === 'MANUAL'">人工值（JSON）<textarea v-model="choices[change.path]!.manual" :aria-label="`人工值 ${change.path}`" /></label>
            </div>
          </div>
          <label>重基原因<textarea v-model="rebaseReason" aria-label="重基原因" maxlength="8000" /></label>
          <button type="button" :disabled="!canSaveRebase" @click="saveRebase">追加重基工作版</button>
        </fieldset>
      </div>
    </section>
    <section v-if="current"><h2>工作历史与差异</h2><form class="inline" @submit.prevent="loadHistory"><label>工作版本<input v-model="historyVersion" aria-label="历史工作版本" pattern="[1-9][0-9]*" :disabled="busy" required /></label><button :disabled="busy">读取历史</button></form>
      <div v-if="history"><p>版本 {{ history.reference.version }}：{{ history.draft.goal }}。原因：{{ history.reason }}</p><p>记录了 {{ history.draft.components.length }} 个组成位置。{{ history.isCurrent ? '当前工作快照。' : '历史工作快照，不覆盖当前草稿。' }}</p><details><summary>历史配置与组成</summary><pre>{{ JSON.stringify(history.draft, null, 2) }}</pre></details></div>
      <details v-if="(history ?? current).rebase"><summary>原重基决定</summary><pre>{{ JSON.stringify((history ?? current).rebase, null, 2) }}</pre></details>
      <form class="inline" @submit.prevent="loadDiff"><label>起始工作版本<input v-model="beforeVersion" aria-label="差异起始版本" pattern="[1-9][0-9]*" :disabled="busy" required /></label><label>结束工作版本<input v-model="afterVersion" aria-label="差异结束版本" pattern="[1-9][0-9]*" :disabled="busy" required /></label><button :disabled="busy">比较工作版本</button></form>
      <div v-if="diff"><p v-if="!diff.changes.length">两个工作快照没有内容差异。</p><div v-for="change in diff.changes" :key="change.path"><h3>{{ change.path }}</h3><div class="grid"><pre>{{ JSON.stringify(change.before, null, 2) }}</pre><pre>{{ JSON.stringify(change.after, null, 2) }}</pre></div></div></div>
    </section>
  </div>
</template>

<script setup lang="ts">
import EngineeringSourceCheckView from './EngineeringSourceCheckView.vue'
import { configurationApi, type ConfigurationSnapshot } from '@/api/plm/configurationRules'
import { qualityApi, type QualityView } from '@/api/mes/qualitySpecification'
import { documentApi, type DocumentSnapshot } from '@/api/plm/controlledDocument'
import { bomApi, type BomSnapshot } from '@/api/plm/engineeringBom'
import { routingApi, type RoutingSnapshot } from '@/api/plm/engineeringRouting'
import { baselineApi } from '@/api/plm/engineeringBaselines'
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { revisionApi, type LegacyReference, type LegacyRegistration, type RebaseDecision, type RebasePreview, type RebaseResolution, type RevisionCommand, type RevisionComponent, type RevisionConfiguration, type RevisionConflict, type RevisionCreation, type RevisionDiff, type RevisionPatch, type RevisionResult, type RevisionScope, type RevisionSnapshot } from '@/api/plm/engineeringRevisions'
import { engineeringApi, type EngineeringReference, type EngineeringVersion } from '@/api/plm/engineeringProducts'
import { latestRevisionConflict, revisionResponse, pendingRevisionOperation, resolveRevisionOperation, saveRevisionOperation, type PendingRevisionOperation } from '@/auth/revisionOperationRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
const permissions = usePermissionStore(), canRead = computed(() => permissions.loaded && permissions.has('plm-engineering-revision:query')), canEdit = computed(() => canRead.value && permissions.has('plm-engineering-revision:edit') && current.value?.state === 'DRAFT')
const checkPending = ref(false)
const busy = ref(false), error = ref(''), notice = ref(''), scope = ref<RevisionScope | null>(null), pending = ref<PendingRevisionOperation | null>(null), locked = computed(() => busy.value || !!pending.value || checkPending.value)
const conflictKey = ref(''), conflict = ref<RevisionConflict | null>(null)
const rows = ref<RevisionSnapshot[]>([]), search = ref(''), page = ref(1), current = ref<RevisionSnapshot | null>(null), creating = ref(false), creation = ref<RevisionCreation | null>(null), draftId = ref('')
const goal = ref(''), reason = ref(''), noParentReason = ref(''), department = ref(''), productSearch = ref(''), products = ref<EngineeringVersion[]>([]), rootProduct = ref('')
const rules = ref<ConfigurationSnapshot[]>([]), ruleSelection = ref('')
const qualitySources = ref<QualityView[]>([]), qualitySearch = ref(''), qualityPage = ref(1), qualityMore = ref(false)
const documents = ref<DocumentSnapshot[]>([])
const routings = ref<RoutingSnapshot[]>([]), boms = ref<BomSnapshot[]>([])
const emptyConfiguration = (): RevisionConfiguration => ({ options: [], applicability: 'UNKNOWN', reason: '' })
const configuration = ref<RevisionConfiguration>(emptyConfiguration()), component = ref<RevisionComponent | null>(null), componentSource = ref('')
const historyVersion = ref('1'), beforeVersion = ref('1'), afterVersion = ref('1'), history = ref<RevisionSnapshot | null>(null), diff = ref<RevisionDiff | null>(null)
const baseVersion = ref(''), incomingKind = ref('candidate'), incomingId = ref(''), incomingVersion = ref(''), rebaseReason = ref('')
const rebasePreview = ref<RebasePreview | null>(null)
const choices = ref<Record<string, { decision: RebaseDecision | ''; reason: string; manual: string }>>({})
const canSaveRebase = computed(() => !!rebaseReason.value.trim() && rebasePreview.value?.changes.every(({ path }) => {
  const choice = choices.value[path]
  return !!choice?.decision && !!choice.reason.trim() && (choice.decision !== 'MANUAL' || !!choice.manual.trim())
}))
const legacyId = ref(''), legacy = ref<LegacyReference | null>(null), useLegacy = ref(false)
const blankLegacy = () => ({ repo: '', record: '', path: '', digest: '', gaps: '', reason: '' })
const legacyForm = reactive(blankLegacy())
const kindNames = { PRODUCT: '工程产品', EBOM: '工程 BOM', MBOM: '制造 BOM', ROUTING: '工艺', DOCUMENT: '文档', QUALITY_SPEC: '质量规范' }
let generation = 0, live = true
const copy = <T,>(value: T): T => JSON.parse(JSON.stringify(value)) as T
const refKey = (value: EngineeringReference) => `${value.owner}:${value.id}:${value.version}:${value.digest}`
const displayValue = (value: unknown) => value == null ? '无' : typeof value === 'string' ? value : JSON.stringify(value, null, 2)
const decisionErrors: Record<string, string> = {
  ENGINEERING_REBASE_BASE_CHANGED: '组成基准已变化，请打开当前工作版后重新比较。',
  ENGINEERING_REBASE_SOURCE_CHANGED: '传入候选已变化，请核对原版本并重新预览。',
  ENGINEERING_REBASE_PRODUCT_MISMATCH: '基准、当前稿和传入来源须属于同一工程产品。',
  ENGINEERING_REBASE_RESOLUTION_REQUIRED: '请为每个差异填写决定和理由。',
  ENGINEERING_REBASE_MANUAL_INVALID: '人工值不符合所选字段的格式。',
  ENGINEERING_REVISION_CONFLICT_NOT_FOUND: '此原操作键下没有已保存的冲突清单。',
  ENGINEERING_WORKING_NOT_DRAFT: '当前工作版在审或已冻结，请重新打开确认状态。',
  ENGINEERING_LEGACY_REFERENCE_EXISTS: '已登记此版本的原记录，请读取原登记。',
  ENGINEERING_LEGACY_PRODUCT_MISMATCH: '历史来源须与所选工程产品一致。',
  ENGINEERING_PARENT_CHANGE_REQUIRED: '此产品已有基线，请从准确父基线开始后继设计，或采用已批准的工程变更。',
  ENGINEERING_PARENT_CHANGE_OWNER_REQUIRED: '缺少获准变更来源，暂不能创建改型修订。',
}
function missingText(value: string) {
  if (value.startsWith('QUALITY_SPEC:')) return '质量规格的批准、完整要求或当前来源不可用，请重新核对。'
  if (value.startsWith('DOCUMENT_APPROVAL_REQUIRED:')) return '所引用文档仍待专业评审与批准'
  if (value.startsWith('DOCUMENT:')) return '文档来源或内容仍有待确认事项'
  if (value.startsWith('BOM_REVIEW_AND_FREEZE_REQUIRED:')) return '所引用结构仍待评审与冻结'
  if (value.startsWith('BOM:')) return '结构来源仍有缺项，请打开所引用BOM核对'
  if (value.startsWith('ROUTING_REVIEW_AND_FREEZE_REQUIRED:')) return '所引用工艺仍待评审与冻结'
  if (value.startsWith('ROUTING:')) return '工艺来源仍有缺项，请打开所引用工艺核对'
  const sourceChanges: Record<string, string> = { CANDIDATE_CHANGED: '来源候选已产生后继工作版', CHECK_POLICY_CHANGED: '检查策略已变化', SOURCE_WATERMARK_CHANGED: '来源水位已变化', CHECK_FINDINGS_CHANGED: '检查发现项已变化', AUTHORITY_CHANGED: '来源授权已变化' }
  if (sourceChanges[value]) return sourceChanges[value]
  const text: Record<string, string> = { PRODUCT_UNRESOLVED: '工程产品待解析', PRODUCT_SOURCE_CHANGED: '工程产品来源已变化，需重新核对', CONFIGURATION_MISSING: '配置草稿待补齐', CONFIGURATION_RULE_CHANGED: '选项规则已撤回、替换或来源已变化，需重新核对', CONFIGURATION_OPTIONS_UNRESOLVED: '选项仍有缺项或冲突', CONFIGURATION_APPLICABILITY_REQUIRED: '完整配置适用范围仍待核准', CONFIGURATION_RULE_OWNER_REQUIRED: '配置规则待接入并核准', COMPOSITION_MISSING: '组成内容待补齐' }
  return text[value] || (value.startsWith('SOURCE_CHANGED:') ? '组成来源已变化：' : value.startsWith('SOURCE_REQUIRED:') ? '组成来源待补齐：' : '组成适用性待核准：') + value.split(':').slice(1).join(':')
}
function message(failure: any) {
  const code = failure?.response?.data?.message || failure?.message || ''
  const known: Record<string, string> = { ENGINEERING_NUMBER_RULE_REQUIRED: '尚未配置工程修订编号规则，请联系编号规则管理员。', ENGINEERING_NUMBER_RULE_INVALID: '当前编号规则无法分配修订号，请核对规则。', ENGINEERING_WORKING_VERSION_CHANGED: '工作草稿已变化，请查询原操作以核对冲突清单。', ENGINEERING_PRODUCT_CHANGED: '工程产品来源已变化或停用，请重新选择。', ENGINEERING_REVISION_PRODUCT_IMMUTABLE: '已绑定的工程产品身份不可替换。', CREATION_AUTHORITY_CHANGED: '当前创建范围已变化，请重新新建并选择部门。', CREATION_DEPARTMENT_FORBIDDEN: '请选择授权范围内的部门。', ACTION_FORBIDDEN: '当前权限无法执行此操作。', OBJECT_FORBIDDEN: '当前权限无法访问所选对象。', ORIGINAL_OPERATION_FORBIDDEN: '当前权限无法访问该原操作。', OPERATIONS_OUTCOME_UNKNOWN: '保存结果尚未确认，请查询原操作。' }
  return known[code] || decisionErrors[code] || (/^[A-Z][A-Z0-9_]+$/.test(code) ? '无法保存，请核对输入和原操作状态。' : code) || '暂时无法完成。'
}
function syncPending() { pending.value = scope.value ? pendingRevisionOperation(scope.value, 'ENGINEERING_REVISION') ?? pendingRevisionOperation(scope.value, 'ENGINEERING_LEGACY_REFERENCE') : null }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return
  const stamp = identityStamp(), epoch = generation
  const check = () => { requireIdentity(stamp); if (!live || epoch !== generation || !authenticationCanRead()) throw new Error('当前身份已变化。') }
  busy.value = true; error.value = ''; notice.value = ''
  try { check(); await action(check); check() }
  catch (failure: any) { try { check(); if (failure?.response?.status === 403) clearPrivate(); error.value = message(failure) } catch { /* Ignore obsolete responses. */ } }
  finally { try { check(); syncPending(); busy.value = false } catch { if (live && epoch === generation) busy.value = false } }
}
function clearEditor() { qualitySources.value = []; qualitySearch.value = ''; qualityPage.value = 1; qualityMore.value = false; documents.value = []; boms.value = []; routings.value = []; rules.value = []; ruleSelection.value = '';  current.value = null; creating.value = false; creation.value = null; draftId.value = ''; goal.value = ''; reason.value = ''; noParentReason.value = ''; department.value = ''; rootProduct.value = ''; products.value = []; productSearch.value = ''; component.value = null; componentSource.value = ''; configuration.value = emptyConfiguration(); history.value = null; diff.value = null; historyVersion.value = '1'; beforeVersion.value = '1'; afterVersion.value = '1'; clearSourceEditors() }
function clearSourceEditors() {
  baseVersion.value = ''; incomingKind.value = 'candidate'; incomingId.value = ''; incomingVersion.value = ''
  rebaseReason.value = ''; rebasePreview.value = null; choices.value = {}
  legacyId.value = ''; legacy.value = null; useLegacy.value = false; Object.assign(legacyForm, blankLegacy())
}
function setCurrent(value: RevisionSnapshot) { clearEditor(); current.value = value; draftId.value = value.reference.id; goal.value = value.draft.goal; configuration.value = copy(value.draft.configuration?.input ?? emptyConfiguration()); afterVersion.value = value.reference.version; baseVersion.value = value.parentWorkingVersion ?? ''; legacyId.value = value.origin.legacyRef?.id ?? '' }
async function reload() {
  await run(async check => {
    rows.value = []; clearEditor(); conflict.value = null; conflictKey.value = ''; scope.value = null
    if (!permissions.loaded) await permissions.loadMyActions()
    check(); if (!canRead.value) throw new Error('当前没有读取工程修订的权限。')
    const s = revisionResponse(await revisionApi.scope()); check()
    scope.value = s; conflictKey.value = latestRevisionConflict(s)
    const list = revisionResponse(await revisionApi.list(search.value, page.value)); check(); rows.value = list
  })
}
async function loadList(target: number) { await run(async check => { rows.value = []; const list = revisionResponse(await revisionApi.list(search.value, target)); check(); rows.value = list; page.value = target }) }
async function open(row: RevisionSnapshot) { const id = row.reference.id; await run(async check => { clearEditor(); const value = revisionResponse(await revisionApi.read(id)); check(); setCurrent(value) }) }
async function openNew() { await run(async check => { clearEditor(); const value = revisionResponse(await revisionApi.creationContext()); check(); creation.value = value; creating.value = true; draftId.value = crypto.randomUUID() }) }
async function findProducts() { const text = productSearch.value; await run(async check => { products.value = []; rootProduct.value = ''; componentSource.value = ''; const values = revisionResponse(await engineeringApi.list(text)); check(); products.value = values.filter(row => row.isCurrent && row.product.enabled) }) }
function selectedProduct(key: string) { const value = products.value.find(row => refKey(row.reference) === key); if (!value) throw new Error('请重新查找并明确选择工程产品来源。'); return copy(value.reference) }
async function write(command: Omit<RevisionCommand, 'operationId'>) {
  const s = scope.value; if (!s) return
  const body = copy(command)
  await run(async check => {
    const authorized = () => { check(); if (!canRead.value || !permissions.has(`plm-engineering-revision:${body.action === 'CREATE' ? 'add' : 'edit'}`)) throw new Error('当前权限无法保存工程修订。') }
    await saveRevisionOperation(s, 'ENGINEERING_REVISION', body.revisionId, authorized, operationId => revisionApi.save({ ...body, operationId })); check(); clearEditor(); rows.value = []
    const value = revisionResponse(await revisionApi.read(body.revisionId)); check(); setCurrent(value); const list = revisionResponse(await revisionApi.list(search.value, page.value)); check(); rows.value = list; notice.value = body.action === 'REBASE' ? '已追加重基工作版。旧检查保留在原候选版，请重新检查。' : '工作快照已保存。'
  })
}
async function patch(patches: RevisionPatch[]) { const value = current.value; if (!canEdit.value) return; if (!value || !reason.value.trim()) { error.value = '请填写修改原因。'; return } await write({ revisionId: value.reference.id, action: 'PATCH', expectedVersion: value.reference.version, expectedEtag: value.etag, reason: reason.value, patches }) }
async function saveGoal() {
  if (!creating.value) { await patch([{ op: 'SET_GOAL', goal: goal.value }]); return }
  try {
    const product = rootProduct.value ? selectedProduct(rootProduct.value) : null
    const evidence = useLegacy.value ? legacy.value : null
    if (useLegacy.value && (!evidence || product?.id !== evidence.original.registration.product.id)) throw new Error('请选择与已登记历史来源一致的当前工程产品。')
    await write({ revisionId: draftId.value, action: 'CREATE', expectedAuthorityVersion: creation.value?.authorityVersion, departmentId: department.value || null,
      origin: evidence ? { type: 'LEGACY_REVIEW', legacyRef: evidence.reference } : { type: 'NEW_PRODUCT', noParentReason: noParentReason.value }, goal: goal.value, reason: reason.value, productRef: product })
  } catch (failure) { error.value = message(failure) }
}
async function previewRebase() {
  const working = current.value, sourceId = incomingId.value, sourceVersion = incomingVersion.value, type = incomingKind.value, selectedBase = baseVersion.value
  if (!working || !canEdit.value || locked.value) return
  await run(async check => {
    rebasePreview.value = null; choices.value = {}
    async function workingRef(id: string, version: string) {
      if (!/^[1-9][0-9]*$/.test(version)) throw new Error('请输入准确的工作版本号。')
      const value = revisionResponse(await revisionApi.read(id, version)); check()
      if (value.reference.id !== id || value.reference.version !== version || value.candidateRef.version !== version) throw new Error('工作候选与所选版本不一致。')
      return value.candidateRef
    }
    const base = working.compositionBase ?? await workingRef(working.reference.id, selectedBase)
    const incoming = type === 'candidate' ? await workingRef(sourceId, sourceVersion) : revisionResponse(await baselineApi.read(sourceId)).reference
    check()
    const preview = revisionResponse(await revisionApi.rebasePreview(working.reference.id, { base, incoming })); check()
    if (refKey(preview.revision) !== refKey(working.reference) || refKey(preview.candidate) !== refKey(working.candidateRef) || preview.etag !== working.etag
      || refKey(preview.sources.base) !== refKey(base) || refKey(preview.sources.incoming) !== refKey(incoming)) throw new Error('工作版或来源已变化，请重新打开后核对。')
    choices.value = Object.fromEntries(preview.changes.map(change => [change.path, { decision: '', reason: '', manual: '' }]))
    rebasePreview.value = preview
  })
}
async function saveRebase() {
  const preview = rebasePreview.value
  if (!preview || !canSaveRebase.value || !canEdit.value || locked.value) return
  let resolutions: RebaseResolution[]
  try {
    resolutions = preview.changes.map(({ path }) => {
      const choice = choices.value[path]!
      return { path, decision: choice.decision as RebaseDecision, reason: choice.reason,
        ...(choice.decision === 'MANUAL' ? { manual: JSON.parse(choice.manual) } : {}) }
    })
  } catch { error.value = '请输入合法的人工 JSON 值。文本需用双引号，清除值请填 null。'; return }
  await write({ revisionId: preview.revision.id, action: 'REBASE', expectedVersion: preview.revision.version,
    expectedEtag: preview.etag, reason: rebaseReason.value, rebase: { sources: preview.sources, resolutions } })
}
function legacyResult(value: LegacyReference, scope: RevisionScope): RevisionResult {
  const { reference, original } = value
  if (reference.owner !== 'Main.Plm.EngineeringLegacyReference.v1' || reference.id !== original.id || original.tenantId !== scope.tenantId
    || original.actorId !== scope.actorId || value.evidenceStatus !== 'EVIDENCE_INCOMPLETE') throw new Error('历史来源与原登记不一致。')
  return { operationId: original.registration.operationId, objectId: original.registration.product.id, kind: 'ENGINEERING_LEGACY_REFERENCE',
    version: reference.version, digest: reference.digest }
}
async function openLegacy() {
  const id = legacyId.value
  await run(async check => {
    legacy.value = null; useLegacy.value = false
    const value = revisionResponse(await revisionApi.readLegacy(id)); check()
    if (value.reference.id !== id || value.original.tenantId !== scope.value?.tenantId) throw new Error('历史来源与所选登记不一致。')
    legacy.value = value
  })
}
async function registerLegacy() {
  const s = scope.value
  if (!s || locked.value) return
  await run(async check => {
    const product = selectedProduct(rootProduct.value)
    const input: Omit<LegacyRegistration, 'operationId'> = { product, sourceRepositorySha: legacyForm.repo, sourceRecordRef: legacyForm.record,
      sourcePath: legacyForm.path || null, sourceHash: legacyForm.digest, evidenceGaps: legacyForm.gaps.split(/\r?\n/).filter(line => !!line.trim()), reason: legacyForm.reason }
    const authorized = () => { check(); if (!permissions.has('plm-engineering-revision:legacy-register')) throw new Error('当前权限无法登记历史来源。') }
    let received: LegacyReference | undefined
    await saveRevisionOperation(s, 'ENGINEERING_LEGACY_REFERENCE', product.id, authorized, async operationId => {
      const response = await revisionApi.registerLegacy({ ...input, operationId }); check()
      received = revisionResponse(response)
      return { code: 0, data: legacyResult(received, s) }
    }); check()
    if (received) { legacy.value = received; legacyId.value = received.reference.id; useLegacy.value = false }
    notice.value = '历史来源已登记。'
  })
}
async function bindProduct() { try { await patch([{ op: 'SET_PRODUCT', productRef: selectedProduct(rootProduct.value) }]) } catch (failure) { error.value = message(failure) } }
function optionType(index: number, type: string) { const original = configuration.value.options[index]; if (!original) return; configuration.value.options[index] = type === 'DECIMAL' ? { name: original.name, type, decimal: '' } : type === 'BOOLEAN' ? { name: original.name, type, boolean: null } : { name: original.name, type: 'STRING', text: '' } }
const optionStatus = (s: string) => ({ OPTIONS_VALIDATED: '选项约束已通过', UNKNOWN: '缺少必要输入', CONFLICT: '存在约束冲突', NO_MATCH: '选项值不满足规则' }[s] || s)
const findingText = (s: string) => ({ REQUIRED_OPTION_MISSING: '必选项待补齐', DEPENDENCY_OPTION_MISSING: '依赖选项待补齐', OPTION_CONSTRAINT_CONFLICT: '选项约束冲突', OPTION_VALUE_NOT_ALLOWED: '选项值不在允许范围' }[s] || s)
async function findRules() { const product = current.value?.draft.productRef; if (!product) return; await run(async check => { rules.value = []; ruleSelection.value = ''; const list = revisionResponse(await configurationApi.list(product.id)); check(); for (const row of list) { if (!row.activeRef) continue; const approved = revisionResponse(await configurationApi.read(row.activeRef.id, row.activeRef.version)); check(); if (approved.isActive && !approved.missing.length && refKey(approved.body.productRef) === refKey(product)) rules.value.push(approved) } }) }
async function saveConfiguration() { const input = copy(configuration.value); if (ruleSelection.value) { const rule = rules.value.find(r => refKey(r.reference) === ruleSelection.value); if (!rule) { error.value = '请重新查找获准规则。'; return } input.ruleRef = copy(rule.reference) } await patch([{ op: 'SET_CONFIGURATION', configuration: input }]) }
async function clearConfiguration() { await patch([{ op: 'CLEAR_CONFIGURATION' }]) }
function newComponent() { componentSource.value = ''; component.value = { occurrenceId: crypto.randomUUID(), kind: 'PRODUCT', path: '', semanticOrder: 0, applicability: 'UNKNOWN', reason: '', note: '', sourceRef: null } }
function editComponent(value: RevisionComponent) { componentSource.value = ''; component.value = copy(value) }
async function findQuality(target: number) {
  const parent = current.value; if (!parent?.draft.productRef) return
  await run(async check => {
    if (target === 1) { qualitySources.value = []; componentSource.value = '' }
    const values = revisionResponse(await qualityApi.list(qualitySearch.value, target)); check()
    for (const item of values) {
      const reference = item.currentApproval?.body.specification
      if (!reference || item.version.body.product.id !== parent.draft.productRef!.id) continue
      const exact = reference.version === item.version.reference.version ? item : revisionResponse(await qualityApi.read(reference.id, reference.version)); check()
      const proof = revisionResponse(await qualityApi.source(reference)); check()
      if (refKey(exact.version.reference) !== refKey(reference) || refKey(proof.specification) !== refKey(reference) || proof.observation !== 'READ_ONLY_OBSERVATION') throw new Error('质量规格来源与所选准确版本不一致。')
      if (proof.availability === 'AVAILABLE' && refKey(exact.version.body.product) === refKey(parent.draft.productRef!)) qualitySources.value.push(exact)
    }
    qualityPage.value = target; qualityMore.value = values.length === 20
  })
}
async function findDocuments() { const parent = current.value; if (!parent) return; await run(async check => { documents.value = []; componentSource.value = ''; const values = revisionResponse(await documentApi.list(parent.reference.id)); check(); documents.value = values.filter(d => d.isCurrent && parent.draft.productRef && refKey(d.body.productRef) === refKey(parent.draft.productRef)) }) }
async function findRoutings() { const parent = current.value; if (!parent) return; await run(async check => { routings.value = []; componentSource.value = ''; const values = revisionResponse(await routingApi.list(parent.reference.id)); check(); routings.value = values.filter(r => r.isCurrent && parent.draft.productRef && refKey(r.body.productRef) === refKey(parent.draft.productRef)) }) }
async function findBoms() { const parent = current.value, kind = component.value?.kind; if (!parent) return; await run(async check => { boms.value = []; componentSource.value = ''; const values = revisionResponse(await bomApi.list(parent.reference.id)); check(); boms.value = values.filter(b => b.isCurrent && b.body.kind === kind && parent.draft.productRef && refKey(b.body.productRef) === refKey(parent.draft.productRef)) }) }
async function saveComponent() { if (!component.value) return; try { const value = copy(component.value); if (componentSource.value) { if (value.kind === 'QUALITY_SPEC') { const q = qualitySources.value.find(q => refKey(q.version.reference) === componentSource.value); if (!q) throw new Error('请重新查找已批准质量规格。'); value.sourceRef = copy(q.version.reference) } else if (value.kind === 'DOCUMENT') { const d = documents.value.find(d => refKey(d.reference) === componentSource.value); if (!d) throw new Error('请重新查找本修订文档。'); value.sourceRef = copy(d.reference) } else if (value.kind === 'ROUTING') { const r = routings.value.find(r => refKey(r.reference) === componentSource.value); if (!r) throw new Error('请重新查找本修订工艺。'); value.sourceRef = copy(r.reference) } else if (value.kind === 'EBOM' || value.kind === 'MBOM') { const b = boms.value.find(b => b.body.kind === value.kind && refKey(b.reference) === componentSource.value); if (!b) throw new Error('请重新查找本修订BOM。'); value.sourceRef = copy(b.reference) } else value.sourceRef = selectedProduct(componentSource.value) } await patch([{ op: 'UPSERT_COMPONENT', component: value }]) } catch (failure) { error.value = message(failure) } }
async function removeComponent(value: RevisionComponent) { await patch([{ op: 'REMOVE_COMPONENT', occurrenceId: value.occurrenceId }]) }
async function loadHistory() { const id = current.value?.reference.id, version = historyVersion.value; if (!id) return; await run(async check => { history.value = null; const value = revisionResponse(await revisionApi.read(id, version)); check(); history.value = value }) }
async function loadDiff() { const id = current.value?.reference.id, before = beforeVersion.value, after = afterVersion.value; if (!id) return; await run(async check => { diff.value = null; const value = revisionResponse(await revisionApi.diff(id, before, after)); check(); diff.value = value }) }
async function readConflict(id: string, check: () => void, objectId?: string) {
  const value = revisionResponse(await revisionApi.conflict(id)); check()
  if (value.operationId !== id || value.expected.revision.id !== value.current.revision.id || (objectId && value.expected.revision.id !== objectId))
    throw new Error('冲突清单与原操作不一致。')
  return value
}
async function loadConflict() {
  const id = conflictKey.value.toLowerCase()
  if (!id || !scope.value || !canRead.value) return
  await run(async check => { conflict.value = null; conflict.value = await readConflict(id, check) })
}
async function recover(close: boolean) {
  const s = scope.value, original = pending.value
  if (!s || !original) return
  await run(async check => {
    if (original.kind === 'ENGINEERING_LEGACY_REFERENCE') {
      let received: LegacyReference | undefined
      const result = await resolveRevisionOperation(s, original.kind, close, check, async (id, finish) => {
        const value = revisionResponse(await revisionApi.legacyOperation(id, finish)); check()
        received = value.result ?? undefined
        return { code: 0, data: { operationId: value.operationId, state: value.state, result: received ? legacyResult(received, s) : null } }
      }); check()
      if (result.state === 'COMMITTED' && received) { legacy.value = received; legacyId.value = received.reference.id; useLegacy.value = false }
      notice.value = result.state === 'COMMITTED' ? '已恢复原历史登记。' : result.state === 'CLOSED' ? '原操作已结束。' : '仍无法确认，请继续查询或结束未提交操作。'
      return
    }
    let evidence: RevisionConflict | undefined
    const result = await resolveRevisionOperation(s, original.kind, close, check, async (id, finish) => {
      const response = await revisionApi.operation(id, finish); check()
      if (revisionResponse(response).state === 'CONFLICT') evidence = await readConflict(id, check, original.objectId)
      return response
    }); check()
    if (result.state === 'CONFLICT' && evidence) {
      clearEditor(); conflict.value = evidence; conflictKey.value = evidence.operationId
      notice.value = '已读取原冲突清单。继续编辑前请打开最新工作版。'
      return
    }
    if (result.state === 'COMMITTED') { clearEditor(); rows.value = []; const values = revisionResponse(await revisionApi.list(search.value, page.value)); check(); rows.value = values }
    notice.value = result.state === 'COMMITTED' ? '原操作已提交，请重新打开最新工作快照。' : result.state === 'CLOSED' ? '原操作已结束，可以继续编辑。' : '仍无法确认，请继续查询或结束未提交操作。'
  })
}
function clearPrivate() { clearEditor(); rows.value = []; search.value = ''; page.value = 1; conflict.value = null; conflictKey.value = '' }
function clear() { generation++; busy.value = false; clearPrivate(); scope.value = null; pending.value = null; error.value = ''; notice.value = '' }
function identityChanged() { clear(); if (authenticationCanRead()) void reload() }
function storage(event: StorageEvent) { if (event.key?.startsWith('cp6_revision_operation_v1:')) { try { syncPending() } catch (failure) { error.value = message(failure) } } }
watch(canRead, allowed => { if (!allowed) clear() })
onMounted(() => { window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('storage', storage); void reload() })
onBeforeUnmount(() => { live = false; generation++; window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', storage) })
</script>

<style scoped>
.revision-page { display: grid; gap: 16px; padding: 24px; color: var(--el-text-color-primary); }
header,.inline { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; } header { justify-content: space-between; }
h1,h2 { margin: 0 0 8px; } section { background: var(--el-bg-color); border: 1px solid var(--el-border-color); border-radius: 8px; padding: 18px; }
label { display: grid; gap: 6px; } input,select,textarea,button { font: inherit; padding: 7px 10px; border: 1px solid var(--el-border-color); border-radius: 4px; background: var(--el-bg-color); color: inherit; max-width: 100%; box-sizing: border-box; }
button { cursor: pointer; margin: 4px; } button:disabled { opacity: .5; cursor: default; } .grid { display: grid; grid-template-columns: repeat(auto-fit,minmax(220px,1fr)); gap: 14px; }
.table-scroll { overflow: auto; margin-top: 16px; } table { width: 100%; border-collapse: collapse; } th,td { text-align: left; padding: 10px; border-bottom: 1px solid var(--el-border-color-lighter); }
fieldset { border: 0; padding: 0; } [role=alert] { color: var(--el-color-danger); } pre { white-space: pre-wrap; overflow-wrap: anywhere; }
.rebase-item { border-top: 1px solid var(--el-border-color-lighter); padding-block: 12px; }
.refs dd, td code { overflow-wrap: anywhere; }
</style>
