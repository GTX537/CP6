import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { ISpaceDesignSceneDto } from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import DesignScenePreview3D from './DesignScenePreview3D.vue'

const preview = vi.hoisted(() => ({
  setScene: vi.fn(),
  resize: vi.fn(),
  setPreset: vi.fn(),
  dispose: vi.fn(),
}))

vi.mock('./DesignScenePreview3D', () => ({
  DesignScenePreview3D: class {
    setScene = preview.setScene
    resize = preview.resize
    setPreset = preview.setPreset
    dispose = preview.dispose
  },
}))

class ResizeObserverStub {
  observe() {}
  disconnect() {}
}

describe('DesignScenePreview3D component', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.stubGlobal('ResizeObserver', ResizeObserverStub)
    preview.setScene.mockImplementation(async (scene: ISpaceDesignSceneDto) => {
      const count = scene.contentRevision ?? 0
      const hash = String(count).padStart(64, 'a')
      return {
        consistent: true,
        editorHash: hash,
        viewerHash: hash,
        differences: [],
        editor: { objectCount: count },
        viewer: { objectCount: count },
      }
    })
  })

  it('rebuilds the read-only preview when the saved Design scene is reloaded', async () => {
    const initial = scene(2)
    const wrapper = mount(DesignScenePreview3D, {
      props: { scene: initial },
      global: {
        stubs: {
          ElTag: { template: '<span><slot /></span>' },
          ElButton: { template: '<button @click="$emit(\'click\')"><slot /></button>' },
          ElButtonGroup: { template: '<div><slot /></div>' },
        },
      },
    })
    await flushPromises()

    expect(preview.setScene).toHaveBeenCalledWith(initial)
    expect(wrapper.text()).toContain('2D 2 / 3D 2')
    expect(wrapper.text()).toContain('2D/3D 清单一致')

    const saved = scene(3)
    await wrapper.setProps({ scene: saved })
    await flushPromises()

    expect(preview.setScene).toHaveBeenLastCalledWith(saved)
    expect(wrapper.text()).toContain('2D 3 / 3D 3')
    expect(wrapper.text()).toContain('Draft 只读预览')

    wrapper.unmount()
    expect(preview.dispose).toHaveBeenCalledOnce()
  })
})

function scene(contentRevision: number): ISpaceDesignSceneDto {
  return {
    schemaVersion: 1,
    authority: 'DesignRevision',
    runtimeOverlayIncluded: false,
    versionStatus: 'Draft',
    contentRevision,
  }
}
