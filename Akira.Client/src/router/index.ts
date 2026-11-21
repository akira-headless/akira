import { createRouter, createWebHistory } from 'vue-router'
import { useSessionStore } from '@/stores/session'

declare module 'vue-router' {
    interface RouteMeta {
        public?: boolean
        admin?: boolean
        title?: string
    }
}

export const router = createRouter({
    history: createWebHistory(),
    routes: [
        { path: '/setup', name: 'setup', component: () => import('@/pages/SetupPage.vue'), meta: { public: true, title: 'Set up' } },
        { path: '/sign-in', name: 'sign-in', component: () => import('@/pages/SignInPage.vue'), meta: { public: true, title: 'Sign in' } },
        {
            path: '/',
            component: () => import('@/components/AppLayout.vue'),
            children: [
                { path: '', name: 'dashboard', component: () => import('@/pages/DashboardPage.vue'), meta: { title: 'Overview' } },
                { path: 'events', name: 'events', component: () => import('@/pages/EventsPage.vue'), meta: { title: 'Live events' } },
                { path: 'devices', name: 'devices', component: () => import('@/pages/DevicesPage.vue'), meta: { title: 'Devices' } },
                { path: 'users', name: 'users', component: () => import('@/pages/UsersPage.vue'), meta: { title: 'Users', admin: true } },
                { path: 'api-keys', name: 'api-keys', component: () => import('@/pages/ApiKeysPage.vue'), meta: { title: 'API keys' } },
                { path: 'trakt', name: 'trakt', component: () => import('@/pages/TraktPage.vue'), meta: { title: 'Trakt' } },
                { path: 'subtitles', name: 'subtitles', component: () => import('@/pages/SubtitlesPage.vue'), meta: { title: 'Subtitle requests' } },
                { path: 'settings', name: 'settings', component: () => import('@/pages/SettingsPage.vue'), meta: { title: 'Removal', admin: true } },
                { path: 'webhooks', name: 'webhooks', component: () => import('@/pages/WebhooksPage.vue'), meta: { title: 'Webhooks', admin: true } },
                { path: 'audit', name: 'audit', component: () => import('@/pages/AuditPage.vue'), meta: { title: 'Audit log', admin: true } }
            ]
        },
        { path: '/:path(.*)*', redirect: '/' }
    ]
})

router.beforeEach(async (to) => {
    const session = useSessionStore()
    await session.check()

    // The server did not answer: nowhere to go until it does, the app shows that with a way to ask again
    if (!session.checked) {
        return false
    }
    if (session.setupRequired) {
        return to.name === 'setup' ? true : { name: 'setup' }
    }
    if (to.meta.public) {
        return session.signedIn ? { name: 'dashboard' } : true
    }
    if (!session.signedIn) {
        return { name: 'sign-in', query: to.fullPath === '/' ? {} : { next: to.fullPath } }
    }
    if (to.meta.admin && !session.isAdmin) {
        return { name: 'dashboard' }
    }
    return true
})

router.afterEach((to) => {
    document.title = to.meta.title ? `${to.meta.title} · Akira` : 'Akira'
})
