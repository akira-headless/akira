<template>
    <PageHeader title="Removal" description="What removing a movie or episode does in Radarr and Sonarr, unless a user picked their own." />

    <ErrorNotice class="mb-4" :message="action.error.value ?? settings.error.value" />

    <form v-if="draft" class="panel space-y-5 p-5" @submit.prevent="save">
        <div class="grid gap-4 sm:grid-cols-2">
            <div v-for="type in types" :key="type">
                <label class="label" :for="type">{{ words(type) }}</label>
                <select :id="type" v-model="draft.defaults[type]" class="input">
                    <option v-for="behavior in behaviors" :key="behavior.value" :value="behavior.value">{{ behavior.label }}</option>
                </select>
            </div>
        </div>

        <label class="flex items-start gap-3 text-sm">
            <input v-model="draft.usersMayRemove" type="checkbox" class="mt-0.5 size-4 accent-accent-500" />
            <span>
                <span class="text-white">Users other than admins may remove</span>
                <span class="block text-xs text-ink-400">Off by default, so a child's profile cannot delete a movie.</span>
            </span>
        </label>

        <div class="flex items-center gap-3">
            <button class="button" :disabled="action.busy.value">Save</button>
            <span v-if="saved" class="text-sm text-emerald-300">Saved</span>
        </div>
    </form>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import ErrorNotice from '@/components/ErrorNotice.vue'
import PageHeader from '@/components/PageHeader.vue'
import { useAction, useLoad } from '@/composables/useLoad'
import { words } from '@/services/format'
import { settingsService } from '@/services/services'
import type { EffectiveRemoval, RemovalBehavior, ServerRemovalSettings } from '@/types/api'

const types: (keyof EffectiveRemoval)[] = ['movie', 'episode', 'season', 'series']
const behaviors: { value: RemovalBehavior; label: string }[] = [
    { value: 'deleteAndUnmonitor', label: 'Delete the files and unmonitor' },
    { value: 'deleteKeepMonitored', label: 'Delete the files, keep monitored and search again' },
    { value: 'unmonitorOnly', label: 'Only unmonitor, keep the files' }
]

const settings = useLoad(settingsService.removal)
const action = useAction()
const draft = ref<ServerRemovalSettings | null>(null)
const saved = ref(false)

watch(settings.data, (value) => {
    draft.value = value ? { defaults: { ...value.defaults }, usersMayRemove: value.usersMayRemove } : null
})

async function save() {
    saved.value = false
    if (await action.run(async () => (settings.data.value = await settingsService.saveRemoval(draft.value!)))) {
        saved.value = true
    }
}
</script>
