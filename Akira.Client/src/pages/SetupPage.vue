<template>
    <div class="grid min-h-full place-items-center px-4">
        <div class="panel w-full max-w-md p-8">
            <h1 class="text-xl font-semibold text-white">Set up Akira</h1>

            <template v-if="!created">
                <p class="mt-2 text-sm text-ink-400">
                    The server wrote a setup code to its log when it started. Enter it to create the first admin.
                </p>
                <form class="mt-6 space-y-4" @submit.prevent="submit">
                    <div>
                        <label class="label" for="code">Setup code</label>
                        <input id="code" v-model="code" class="input font-mono uppercase" placeholder="ABC-DEF" autocomplete="off" required />
                    </div>
                    <div>
                        <label class="label" for="name">Your name</label>
                        <input id="name" v-model="name" class="input" maxlength="100" required />
                    </div>
                    <div>
                        <label class="label" for="pin">PIN (optional)</label>
                        <input id="pin" v-model="pin" class="input" inputmode="numeric" placeholder="Asked on shared devices" />
                    </div>
                    <ErrorNotice :message="action.error.value" />
                    <button class="button w-full" :disabled="action.busy.value">Create admin</button>
                </form>
            </template>

            <template v-else>
                <p class="mt-2 text-sm text-ink-400">You are the admin. Scripts and tools sign in with this API key.</p>
                <div class="mt-6">
                    <SecretOnce :secret="created" message="Shown only now. Keep it somewhere safe, it can sign in as you on any browser." />
                </div>
                <ErrorNotice class="mt-4" :message="action.error.value" />
                <button class="button mt-6 w-full" :disabled="action.busy.value" @click="continueIn">Continue</button>
            </template>
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import ErrorNotice from '@/components/ErrorNotice.vue'
import SecretOnce from '@/components/SecretOnce.vue'
import { useAction } from '@/composables/useLoad'
import { sessionService, setupService } from '@/services/services'
import { useSessionStore } from '@/stores/session'

const router = useRouter()
const session = useSessionStore()
const action = useAction()

const code = ref('')
const name = ref('')
const pin = ref('')
const created = ref<string | null>(null)

async function submit() {
    await action.run(async () => {
        const response = await setupService.complete(code.value.trim(), name.value.trim(), pin.value.trim() || null)
        created.value = response.apiKey.key
    })
}

async function continueIn() {
    const signedIn = await action.run(async () => session.signedInAs(await sessionService.signInWithApiKey(created.value!)))
    if (signedIn) {
        await router.push({ name: 'dashboard' })
    }
}
</script>
