// @vitest-environment jsdom
import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import type {
  ISpaceSceneElementAttributeDto,
  ISpaceSceneElementDto,
} from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import DesignElementPropertiesPanel from './DesignElementPropertiesPanel.vue'

const element = {
  revision: {
    logicalId: '11111111-1111-1111-1111-111111111111',
    lifecycleState: 'Active',
  },
  elementType: 'Column',
  geometryJson:
    '{"schemaVersion":1,"kind":"box","width":400,"height":5000,"depth":400}',
  x: 1000,
  y: 2000,
  z: 0,
  rotationZ: 0,
  width: 400,
  height: 5000,
  depth: 400,
} as unknown as ISpaceSceneElementDto

const attributes = [
  {
    namespace: 'design',
    key: 'label',
    valueType: 'String',
    value: 'Column A',
  },
] as ISpaceSceneElementAttributeDto[]

describe('DesignElementPropertiesPanel', () => {
  it('renders the selected semantic element and emits typed save/delete intents', async () => {
    const wrapper = mount(DesignElementPropertiesPanel, {
      props: { element, attributes },
      global: {
        plugins: [ElementPlus],
        directives: {
          permission: {},
        },
      },
    })

    expect(wrapper.get('[data-test="design-element-properties"]').text())
      .toContain('Column')
    expect(wrapper.text()).toContain(element.revision?.logicalId)

    wrapper.findComponent({ name: 'ElSelect' })
      .vm.$emit('update:modelValue', 'Door')
    await wrapper.vm.$nextTick()
    await wrapper.get('[data-test="save-element"]').trigger('click')
    await wrapper.get('[data-test="delete-element"]').trigger('click')

    expect(wrapper.emitted('save')).toHaveLength(1)
    expect(wrapper.emitted('save')?.[0]?.[0]).toMatchObject({
      elementType: 'Door',
      x: 1000,
      y: 2000,
      width: 400,
      height: 5000,
      depth: 400,
    })
    expect(wrapper.emitted('remove')).toHaveLength(1)
  })
})
