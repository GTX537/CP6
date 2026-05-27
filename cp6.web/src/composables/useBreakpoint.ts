import { ref, computed, onMounted, onBeforeUnmount, readonly } from 'vue'

const MOBILE_MAX = 767
const TABLET_MAX = 991

const width = ref(typeof window !== 'undefined' ? window.innerWidth : 1280)

let listenerCount = 0
function onResize() {
  width.value = window.innerWidth
}

export function useBreakpoint() {
  onMounted(() => {
    if (listenerCount === 0) {
      window.addEventListener('resize', onResize)
      width.value = window.innerWidth
    }
    listenerCount++
  })
  onBeforeUnmount(() => {
    listenerCount--
    if (listenerCount === 0) {
      window.removeEventListener('resize', onResize)
    }
  })

  const isMobile = computed(() => width.value <= MOBILE_MAX)
  const isTablet = computed(() => width.value > MOBILE_MAX && width.value <= TABLET_MAX)
  const isDesktop = computed(() => width.value > TABLET_MAX)

  return {
    width: readonly(width),
    isMobile,
    isTablet,
    isDesktop,
  }
}
