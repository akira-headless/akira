<template>
    <section v-if="bridges.data.value?.length" class="panel p-4">
        <h2 class="text-sm font-medium text-white">Connect an app</h2>
        <p class="mt-1 text-sm text-ink-400">Apps that do not speak Akira's own API, through a bridge. They play your library as you.</p>

        <div v-for="bridge in bridges.data.value" :key="bridge.key" class="mt-4 rounded-md bg-ink-950 p-3 text-sm">
            <p class="text-white">{{ bridge.name }}</p>
            <p class="mt-1 text-xs text-ink-400">{{ bridge.instructions }}</p>

            <form v-if="bridge.connect === 'link'" class="mt-3 flex flex-wrap items-end gap-3" @submit.prevent="createLink(bridge.key)">
                <div class="w-56">
                    <label class="label" :for="`name-${bridge.key}`">Name</label>
                    <input :id="`name-${bridge.key}`" v-model="names[bridge.key]" class="input" :placeholder="`${bridge.name} on the TV`" maxlength="100" />
                </div>
                <button class="button" :disabled="action.busy.value">Make link</button>
            </form>

            <div v-else-if="bridge.connect === 'address'" class="mt-3 flex flex-wrap items-center gap-2">
                <code class="min-w-0 flex-1 truncate rounded bg-ink-900 px-2 py-1 text-xs text-white">{{ bridge.address }}</code>
                <button class="button-quiet" @click="copy(bridge.address, $event)">{{ copied === bridge.address ? copyLabel(result) : 'Copy' }}</button>
            </div>

            <div v-if="created?.bridge === bridge.key" class="mt-3 rounded-md border border-amber-700/60 p-3">
                <p class="text-amber-200">This url is shown once. Anyone who has it plays your library; revoke {{ created.device.name }} below to stop it.</p>
                <div class="mt-2 flex flex-wrap items-center gap-2">
                    <code class="min-w-0 flex-1 truncate rounded bg-ink-900 px-2 py-1 text-xs text-white" :title="created.url">{{ created.url }}</code>
                    <button class="button-quiet" @click="copy(created.url, $event)">{{ copied === created.url ? copyLabel(result) : 'Copy' }}</button>
                    <a v-if="created.appUrl" class="button-quiet" :href="created.appUrl">Open in {{ bridge.name }}</a>
                </div>
            </div>
        </div>
        <ErrorNotice class="mt-3" :message="action.error.value" />
    </section>
</template>

<script setup lang="ts">
import { reactive, ref } from 'vue'
import ErrorNotice from '@/components/ErrorNotice.vue'
import { useAction, useLoad } from '@/composables/useLoad'
import { copyLabel, copyText, type CopyResult } from '@/composables/clipboard'
import { bridgeService } from '@/services/services'
import type { LinkCreated } from '@/types/api'

const emit = defineEmits<{ created: [] }>()

const bridges = useLoad(bridgeService.list)
const action = useAction()
const names = reactive<Record<string, string>>({})
const created = ref<LinkCreated | null>(null)
const copied = ref<string | null>(null)
const result = ref<CopyResult | null>(null)

async function createLink(key: string) {
    created.value = null
    await action.run(async () => {
        created.value = await bridgeService.createLink(key, names[key]?.trim() || null)
        names[key] = ''
        emit('created')
    })
}

// The address shown next to the button is what gets selected when copying is not possible
async function copy(text: string, event: MouseEvent) {
    const shown = (event.currentTarget as HTMLElement | null)?.previousElementSibling
    result.value = await copyText(text, shown)
    copied.value = text
}
</script>
