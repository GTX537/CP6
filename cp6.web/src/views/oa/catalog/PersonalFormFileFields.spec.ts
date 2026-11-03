import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import { expect, it, vi } from 'vitest'
import Fields from './PersonalFormFields.vue'
import FileInput from './PersonalFileInput.vue'
import { schemaFields } from './personalFormJson'
vi.mock('@/api/oa/formFiles', () => ({ formFileApi: {} }))
it('blocks both clear actions while the attachment outcome is pending, then permits explicit removal', async () => {
  const wrapper = mount(Fields, { props: { fields: schemaFields('{"fields":[{"name":"files","type":"upload"}]}'), modelValue: { files: ['original'] } }, global: { stubs: { PersonalFileInput: true } } })
  const file = wrapper.findComponent(FileInput)
  file.vm.$emit('validity', false); await nextTick()
  const buttons = wrapper.findAll('.value-state button'); expect(buttons.map(b => b.text())).toEqual(['移除填写', '设为空值'])
  for (const button of buttons) { expect(button.attributes('disabled')).toBeDefined(); await button.trigger('click') }
  expect(wrapper.emitted('update:modelValue')).toBeUndefined()
  file.vm.$emit('validity', true); await nextTick(); await buttons[0]!.trigger('click')
  expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual([{}]); wrapper.unmount()
})
