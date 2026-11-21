<template>
    <section class="panel p-4">
        <div class="flex items-center justify-between">
            <h2 class="text-sm font-medium text-white">Now playing</h2>
            <span class="text-xs text-ink-400">{{ playing.length ? `${playing.length} stream${playing.length === 1 ? '' : 's'}` : '' }}</span>
        </div>
        <p v-if="!playing.length" class="mt-3 text-sm text-ink-400">Nothing is playing.</p>

        <div v-for="stream in playing" :key="stream.sessionId" class="mt-4 rounded-md border border-ink-700 bg-ink-950 p-4">
            <div class="flex flex-wrap items-start justify-between gap-2">
                <div class="min-w-0">
                    <p class="truncate font-medium text-white">{{ title(stream) }}</p>
                    <p class="text-xs text-ink-400">
                        {{ stream.user.name }}<span v-if="stream.device"> · {{ stream.device.name }}</span>
                    </p>
                </div>
                <span class="rounded px-2 py-0.5 text-xs" :class="stream.delivery === 'hls' ? 'bg-amber-950 text-amber-200' : 'bg-emerald-950 text-emerald-200'">
                    {{ delivery(stream) }}
                </span>
            </div>

            <div class="mt-3 flex items-center gap-3 text-xs text-ink-400">
                <span class="w-12 text-right tabular-nums">{{ clock(stream.positionMs) }}</span>
                <div class="relative h-1.5 flex-1 overflow-hidden rounded bg-ink-700">
                    <!-- How far the transcode got, behind where the player is -->
                    <div
                        v-if="stream.transcoder?.encodedToMs && stream.durationMs"
                        class="absolute inset-y-0 left-0 bg-ink-600"
                        :style="{ width: `${Math.min(100, (stream.transcoder.encodedToMs / stream.durationMs) * 100)}%` }"
                    ></div>
                    <div class="absolute inset-y-0 left-0 bg-accent-500" :style="{ width: `${percent(stream)}%` }"></div>
                </div>
                <span class="w-12 tabular-nums">{{ stream.durationMs ? clock(stream.durationMs) : '' }}</span>
                <span class="w-14" :class="stream.isPaused ? 'text-amber-300' : 'text-emerald-300'">{{ stream.isPaused ? 'Paused' : 'Playing' }}</span>
            </div>

            <dl class="mt-4 grid gap-x-6 gap-y-2 text-xs sm:grid-cols-3">
                <div>
                    <dt class="label">Version</dt>
                    <dd>{{ version(stream) }}</dd>
                </div>
                <div>
                    <dt class="label">Audio</dt>
                    <dd>{{ track(stream.audio) }}<span v-if="stream.transcode && stream.transcode.audio !== 'copy'" class="text-amber-300"> → {{ stream.transcode.audio }}</span></dd>
                </div>
                <div>
                    <dt class="label">Subtitle</dt>
                    <dd>
                        {{ stream.subtitle ? track(stream.subtitle) : 'Off' }}
                        <span v-if="stream.subtitle && stream.transcode?.burnedSubtitleStreamId === stream.subtitle.streamId" class="text-amber-300">burned in</span>
                    </dd>
                </div>
            </dl>

            <div v-if="stream.transcoder" class="mt-4 grid gap-4 md:grid-cols-[14rem_1fr]">
                <dl class="grid grid-cols-2 gap-x-4 gap-y-2 text-xs md:grid-cols-1">
                    <div>
                        <dt class="label">Encoder</dt>
                        <dd>{{ stream.transcoder.encoder }}<span v-if="!stream.transcoder.running" class="text-ink-400"> (done)</span></dd>
                    </div>
                    <div>
                        <dt class="label">Speed</dt>
                        <dd :class="(stream.transcoder.speed ?? 0) < 1 && stream.transcoder.running ? 'text-red-300' : 'text-white'">
                            {{ stream.transcoder.speed ? `${stream.transcoder.speed.toFixed(1)}× realtime` : 'Waiting' }}
                            <span v-if="stream.transcoder.fps" class="text-ink-400">· {{ Math.round(stream.transcoder.fps) }} fps</span>
                        </dd>
                    </div>
                    <div>
                        <dt class="label">Ahead of the player</dt>
                        <dd>{{ ahead(stream) }}</dd>
                    </div>
                    <div v-if="stream.transcode">
                        <dt class="label">Why</dt>
                        <dd>{{ stream.transcode.reasons.map((r) => words(r.replaceAll('-', ' '))).join(', ') }}</dd>
                    </div>
                </dl>
                <div>
                    <p class="label">Encoding speed</p>
                    <SpeedChart v-if="stream.transcoder.history.length > 1" :history="stream.transcoder.history" />
                    <p v-else class="mt-6 text-xs text-ink-400">Waiting for ffmpeg's first reports, every two seconds.</p>
                </div>
            </div>
        </div>
    </section>
</template>

<script setup lang="ts">
import { onBeforeUnmount, ref } from 'vue'
import SpeedChart from '@/components/SpeedChart.vue'
import { usePoll } from '@/composables/usePoll'
import { words } from '@/services/format'
import { nowPlayingService } from '@/services/services'
import { missed, useEventStore } from '@/stores/events'
import type { NowPlaying, NowPlayingTrack } from '@/types/api'

const playing = ref<NowPlaying[]>([])

async function load() {
    playing.value = await nowPlayingService.list().catch(() => playing.value)
}

function title(stream: NowPlaying) {
    const item = stream.item
    if (item.seriesTitle) {
        const number = `S${String(item.seasonNumber ?? 0).padStart(2, '0')}E${String(item.episodeNumber ?? 0).padStart(2, '0')}`
        return `${item.seriesTitle} ${number} ${item.title}`
    }
    return item.title
}

function delivery(stream: NowPlaying) {
    if (stream.delivery === 'direct') return 'Direct play'
    return stream.transcode?.video === 'copy' ? 'Remux' : 'Transcode'
}

function version(stream: NowPlaying) {
    const v = stream.version
    const video = [v.videoCodec?.toUpperCase(), v.height ? `${v.height}p` : null, v.hdr?.toUpperCase()].filter(Boolean).join(' ')
    const out = stream.transcode && stream.transcode.video !== 'copy'
        ? ` → H264${stream.transcode.height ? ` ${stream.transcode.height}p` : ''}${stream.transcode.toneMap ? ' SDR' : ''}`
        : ''
    return `${v.instance} · ${video}${out}`
}

function track(t: NowPlayingTrack | null) {
    if (!t) return ''
    return [t.language?.toUpperCase(), t.codec?.toUpperCase(), t.channels ? `${t.channels}ch` : null, t.title, t.forced ? 'forced' : null]
        .filter(Boolean)
        .join(' ')
}

function percent(stream: NowPlaying) {
    return stream.durationMs ? Math.min(100, (stream.positionMs / stream.durationMs) * 100) : 0
}

function ahead(stream: NowPlaying) {
    const encoded = stream.transcoder?.encodedToMs
    if (!encoded) return 'Not yet'
    const seconds = Math.round((encoded - stream.positionMs) / 1000)
    return seconds >= 0 ? `${seconds} s` : `${-seconds} s behind`
}

function clock(ms: number) {
    const total = Math.floor(ms / 1000)
    const hours = Math.floor(total / 3600)
    const minutes = Math.floor((total % 3600) / 60)
    const seconds = String(total % 60).padStart(2, '0')
    return hours ? `${hours}:${String(minutes).padStart(2, '0')}:${seconds}` : `${minutes}:${seconds}`
}

// Positions only reach the server every ten seconds and a transcode reports every two, so three seconds is live enough
usePoll(load, 3000)
const unsubscribe = useEventStore().subscribe((event) => {
    if (event.type === 'playback.started' || event.type === 'playback.stopped' || missed(event)) {
        void load()
    }
})
onBeforeUnmount(unsubscribe)
</script>
