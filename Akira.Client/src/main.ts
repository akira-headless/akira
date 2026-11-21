import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from '@/App.vue'
import { router } from '@/router'
import { onUnauthorized } from '@/services/api'
import { useSessionStore } from '@/stores/session'
import { useEventStore } from '@/stores/events'
import '@/style.css'

const RELOADED = 'akira.reloaded'
let heading: string | null = null
router.beforeEach((to) => {
    heading = to.fullPath
})
router.afterEach(() => {
    heading = null
})
router.onError(() => {
    heading = null
})

// A tab left open across an update asks for files that are gone. The page loads again, once, where it was going
window.addEventListener('vite:preloadError', () => {
    try {
        if (Date.now() - Number(sessionStorage.getItem(RELOADED)) < 60_000) {
            return
        }
        sessionStorage.setItem(RELOADED, String(Date.now()))
    } catch {
        return
    }
    window.location.assign(heading ?? window.location.href)
})

const app = createApp(App)
app.use(createPinia())
app.use(router)

// A revoked device or an expired session lands on the sign in page, wherever it happened
onUnauthorized(() => {
    useSessionStore().lost()
    useEventStore().forget()
    if (router.currentRoute.value.name !== 'sign-in') {
        void router.push({ name: 'sign-in' })
    }
})

app.mount('#app')
