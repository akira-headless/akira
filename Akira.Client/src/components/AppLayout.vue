<template>
    <div class="flex h-full">
        <aside class="flex w-56 shrink-0 flex-col border-r border-ink-800 bg-ink-900">
            <div class="flex items-center gap-2 px-5 py-5">
                <img src="/akira-mark.svg" alt="" class="h-6 w-auto" />
                <span class="text-base font-semibold text-white">Akira</span>
            </div>

            <nav class="flex-1 space-y-0.5 px-3">
                <RouterLink
                    v-for="link in links"
                    :key="link.name"
                    :to="{ name: link.name }"
                    class="block rounded-md px-3 py-1.5 text-sm text-ink-400 hover:bg-ink-800 hover:text-ink-200"
                    active-class="!bg-ink-800 !text-white"
                    :exact-active-class="link.name === 'dashboard' ? '!bg-ink-800 !text-white' : ''"
                >
                    {{ link.title }}
                </RouterLink>
            </nav>

            <div class="border-t border-ink-800 px-5 py-4 text-sm">
                <div class="flex items-center gap-2">
                    <span class="size-2 rounded-full" :class="dot" :title="`Live updates: ${events.status}`"></span>
                    <span class="truncate text-ink-200">{{ session.current?.user.name }}</span>
                    <span v-if="session.isAdmin" class="text-xs text-ink-400">admin</span>
                </div>
                <button class="mt-2 text-xs text-ink-400 hover:text-ink-200" @click="signOut">Sign out</button>
                <p v-if="signOutError" class="mt-1 text-xs text-red-300" role="alert">Not signed out. {{ signOutError }}</p>
            </div>
        </aside>

        <main class="min-w-0 flex-1 overflow-y-auto">
            <div class="mx-auto max-w-6xl px-8 py-8">
                <RouterView />
            </div>
        </main>
    </div>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useSessionStore } from '@/stores/session'
import { useEventStore } from '@/stores/events'

const session = useSessionStore()
const events = useEventStore()
const router = useRouter()
const signOutError = ref<string | null>(null)

const links = computed(() =>
    router
        .getRoutes()
        .filter((route) => route.name && route.meta.title && !route.meta.public && (!route.meta.admin || session.isAdmin))
        .map((route) => ({ name: route.name as string, title: route.meta.title as string }))
)

const dot = computed(() =>
    events.status === 'live' ? 'bg-emerald-400' : events.status === 'connecting' ? 'bg-amber-400' : 'bg-ink-600'
)

async function signOut() {
    signOutError.value = null
    try {
        await session.signOut()
    } catch (caught) {
        signOutError.value = caught instanceof Error ? caught.message : String(caught)
        return
    }
    events.forget()
    await router.push({ name: 'sign-in' })
}

onMounted(() => events.connect())
onBeforeUnmount(() => events.disconnect())
</script>
