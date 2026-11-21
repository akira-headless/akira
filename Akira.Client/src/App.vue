<template>
    <div v-if="session.unreachable" class="grid min-h-full place-items-center px-4">
        <div class="panel w-full max-w-md p-8 text-center" role="alert">
            <img src="/akira-mark.svg" alt="" class="mx-auto mb-5 h-10 w-auto" />
            <h1 class="text-xl font-semibold text-white">Akira could not be reached</h1>
            <p class="mt-2 text-sm text-ink-400">{{ session.unreachable }}</p>
            <button class="button mt-6" :disabled="asking" @click="again">Try again</button>
        </div>
    </div>
    <RouterView v-else />
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useSessionStore } from '@/stores/session'

const session = useSessionStore()
const router = useRouter()
const asking = ref(false)

// The first navigation was refused while the server was away, so the address in the bar is still where the user wants to be
async function again() {
    asking.value = true
    await session.check()
    asking.value = false
    if (session.checked) {
        await router.replace(window.location.pathname + window.location.search + window.location.hash)
    }
}
</script>
