<template>
    <section class="panel p-4">
        <h2 class="text-sm font-medium text-white">Plugins</h2>
        <ul class="mt-3 space-y-3 text-sm">
            <li v-for="plugin in overview.data.value?.plugins ?? []" :key="plugin.key">
                <div class="flex items-center justify-between gap-3">
                    <span class="min-w-0 truncate">
                        {{ plugin.name }}
                        <span class="text-xs text-ink-400">{{ plugin.provides.map(words).join(', ') }}</span>
                    </span>
                    <span class="shrink-0" :class="plugin.status === 'active' ? 'text-emerald-300' : 'text-ink-400'">
                        {{ statusLabel[plugin.status] }}
                    </span>
                </div>
                <p class="text-xs text-ink-400">
                    {{ plugin.source === 'built-in' ? 'Built in' : plugin.source }}<span v-if="plugin.version && plugin.source !== 'built-in'"> · {{ plugin.version }}</span>
                    <span v-if="plugin.status === 'notConfigured'"> · needs {{ missing(plugin).join(', ') }}</span>
                    <span v-else-if="plugin.status === 'disabled'"> · turn it on with <code>Plugins__{{ plugin.key }}__Enabled=true</code></span>
                </p>
            </li>
            <li v-for="skipped in overview.data.value?.skipped ?? []" :key="skipped.file" class="text-amber-200">
                {{ skipped.file }} <span class="text-xs">did not load: {{ skipped.reason }}</span>
            </li>
        </ul>
        <p v-if="overview.data.value" class="mt-3 truncate text-xs text-ink-400" :title="overview.data.value.path">
            From {{ overview.data.value.path }}
        </p>
    </section>
</template>

<script setup lang="ts">
import { useLoad } from '@/composables/useLoad'
import { words } from '@/services/format'
import { pluginService } from '@/services/services'
import type { PluginStatus, PluginSummary } from '@/types/api'

const overview = useLoad(pluginService.list)

const statusLabel: Record<PluginStatus, string> = {
    active: 'Active',
    notConfigured: 'Not configured',
    disabled: 'Off'
}

function missing(plugin: PluginSummary) {
    return plugin.settings.filter((s) => s.required && !s.isSet).map((s) => s.label)
}
</script>
