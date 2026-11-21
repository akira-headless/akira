<template>
    <div class="rounded-md border border-amber-700/60 bg-amber-950/40 p-4">
        <p class="text-sm text-amber-200">{{ message }}</p>
        <div class="mt-3 flex items-center gap-2">
            <code ref="shown" class="flex-1 overflow-x-auto rounded bg-ink-950 px-3 py-2 font-mono text-sm text-ink-200">{{ secret }}</code>
            <button class="button-quiet" @click="copy">{{ copyLabel(result) }}</button>
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { copyLabel, copyText, type CopyResult } from '@/composables/clipboard'

const props = defineProps<{ secret: string; message: string }>()
const shown = ref<HTMLElement | null>(null)
const result = ref<CopyResult | null>(null)

async function copy() {
    result.value = await copyText(props.secret, shown.value)
}
</script>
