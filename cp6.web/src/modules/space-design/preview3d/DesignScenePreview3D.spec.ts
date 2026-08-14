import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { ISpaceDesignSceneDto } from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import type { DesignPreviewViewState } from './DesignScenePreview3D'
import DesignScenePreview3D from './DesignScenePreview3D.vue'

const preview = vi.hoisted(() => ({
  setScene: vi.fn(),
  resize: vi.fn(),
  setPreset: vi.fn(),
  setSelectedLogicalIds: vi.fn(),
  restoreViewState: vi.fn(),
  pick: vi.fn(),
  dispose: vi.fn(),
  onViewStateChange: undefined as ((state: unknown) => void) | undefined,
}))

vi.mock('./DesignScenePreview3D', () => ({
  DesignScenePreview3D: class {
    constructor(_canvas: HTMLCanvasElement, onViewStateChange?: (state: unknown) => void) {
      preview.onViewStateChange = onViewStateChange
    }
    setScene = preview.setScene
    resize = preview.resize
    setPreset = preview.setPreset
    setSelectedLogicalIds = preview.setSelectedLogicalIds
    restoreViewState = preview.restoreViewState
    pick = preview.pick
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
    preview.onViewStateChange = undefined
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

    expect(preview.setScene).toHaveBeenCalledWith(initial, true)
    expect(preview.setSelectedLogicalIds).toHaveBeenCalledWith([])
    expect(wrapper.text()).toContain('2D 2 / 3D 2')
    expect(wrapper.text()).toContain('2D/3D 清单一致')

    const saved = scene(3)
    await wrapper.setProps({ scene: saved })
    await flushPromises()

    expect(preview.setScene).toHaveBeenLastCalledWith(saved, false)
    expect(wrapper.text()).toContain('2D 3 / 3D 3')
    expect(wrapper.text()).toContain('Draft 只读预览')

    wrapper.unmount()
    expect(preview.dispose).toHaveBeenCalledOnce()
  })

  it('restores a saved camera after rebuilding and publishes later view changes', async () => {
    const viewState = {
      schemaVersion: 1 as const,
      cameraPosition: [4, 5, 6] as [number, number, number],
      target: [1, 2, 3] as [number, number, number],
    }
    const wrapper = mountPreview({ scene: scene(2), viewState })
    await flushPromises()

    expect(preview.restoreViewState).toHaveBeenCalledWith(viewState)
    preview.onViewStateChange?.(viewState)
    expect(wrapper.emitted('viewStateChange')).toEqual([[viewState]])
  })

  it('emits 3D object picks without treating camera drags as selections', async () => {
    preview.pick.mockReturnValue({ logicalId: 'rack-1', ownerKind: 'Rack' })
    const wrapper = mountPreview({ scene: scene(2) })
    await flushPromises()
    const canvas = wrapper.get('[data-test="design-preview-3d-canvas"]')

    canvas.element.dispatchEvent(new MouseEvent('pointerdown', {
      button: 0,
      clientX: 20,
      clientY: 30,
    }))
    canvas.element.dispatchEvent(new MouseEvent('pointerup', {
      clientX: 22,
      clientY: 31,
      ctrlKey: true,
    }))
    expect(preview.pick).toHaveBeenCalledWith(22, 31)
    expect(wrapper.emitted('select')).toEqual([[
      [{ logicalId: 'rack-1', ownerKind: 'Rack' }],
      'toggle',
    ]])

    preview.pick.mockClear()
    canvas.element.dispatchEvent(new MouseEvent('pointerdown', {
      button: 0,
      clientX: 20,
      clientY: 30,
    }))
    canvas.element.dispatchEvent(new MouseEvent('pointerup', {
      clientX: 40,
      clientY: 50,
    }))
    expect(preview.pick).not.toHaveBeenCalled()
  })
})

function mountPreview(props: {
  scene: ISpaceDesignSceneDto | null
  selectedLogicalIds?: readonly string[]
  viewState?: DesignPreviewViewState | null
}) {
  return mount(DesignScenePreview3D, {
    props,
    global: {
      stubs: {
        ElTag: { template: '<span><slot /></span>' },
        ElButton: { template: '<button @click="$emit(\'click\')"><slot /></button>' },
        ElButtonGroup: { template: '<div><slot /></div>' },
      },
    },
  })
}

function scene(contentRevision: number): ISpaceDesignSceneDto {
  return {
    schemaVersion: 1,
    authority: 'DesignRevision',
    runtimeOverlayIncluded: false,
    versionStatus: 'Draft',
    contentRevision,
  }
}
