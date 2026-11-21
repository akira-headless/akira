import { onBeforeUnmount, onMounted } from 'vue'

// Asks every few seconds while the tab is in view, and at once when it comes back
export function usePoll(load: () => Promise<unknown>, everyMs: number) {
    let timer: ReturnType<typeof setInterval> | null = null

    function ask() {
        if (!document.hidden) {
            void load()
        }
    }

    onMounted(() => {
        void load()
        timer = setInterval(ask, everyMs)
        document.addEventListener('visibilitychange', ask)
    })

    onBeforeUnmount(() => {
        if (timer) {
            clearInterval(timer)
        }
        document.removeEventListener('visibilitychange', ask)
    })
}
