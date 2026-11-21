<template>
    <div ref="box" class="relative" @mouseleave="hover = null">
        <svg :viewBox="`0 0 ${width} ${height}`" :height="height" class="w-full overflow-visible" role="img" :aria-label="summary" @mousemove="move">
            <!-- Recessive grid: the realtime line is the one that matters -->
            <line :x1="left" :x2="width" :y1="y(top)" :y2="y(top)" class="stroke-ink-700" stroke-width="1" />
            <line :x1="left" :x2="width" :y1="y(0)" :y2="y(0)" class="stroke-ink-700" stroke-width="1" />
            <line :x1="left" :x2="width" :y1="y(1)" :y2="y(1)" class="stroke-ink-400" stroke-width="1" stroke-dasharray="3 3" />
            <text :x="left - 6" :y="y(top) + 4" text-anchor="end" class="fill-ink-400 text-[11px]">{{ top }}×</text>
            <text :x="left - 6" :y="y(1) + 4" text-anchor="end" class="fill-ink-400 text-[11px]">1×</text>
            <text :x="width" :y="y(1) - 5" text-anchor="end" class="fill-ink-400 text-[11px]">realtime</text>

            <polyline v-if="points.length > 1" :points="line" fill="none" stroke="#3d8bf7" stroke-width="2" stroke-linejoin="round" stroke-linecap="round" />

            <template v-if="hover !== null && points[hover]">
                <line :x1="points[hover]!.x" :x2="points[hover]!.x" :y1="y(top)" :y2="y(0)" class="stroke-ink-400" stroke-width="1" />
                <circle :cx="points[hover]!.x" :cy="points[hover]!.y" r="4" fill="#3d8bf7" class="stroke-ink-900" stroke-width="2" />
            </template>
        </svg>

        <div
            v-if="hover !== null && history[hover]"
            class="pointer-events-none absolute -top-2 rounded-md border border-ink-700 bg-ink-950 px-2 py-1 text-xs whitespace-nowrap shadow"
            :style="{ left: `${points[hover]!.x}px`, transform: 'translate(-50%, -100%)' }"
        >
            <span class="text-white">{{ history[hover]!.speed.toFixed(1) }}×</span>
            <span class="text-ink-400"> · {{ Math.round(history[hover]!.fps) }} fps · {{ ago(history[hover]!.at) }}</span>
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { ago } from '@/services/format'
import type { TranscoderSample } from '@/types/api'

const props = defineProps<{ history: TranscoderSample[] }>()

// Drawn in real pixels at the width it gets, so text and lines keep their size
const box = ref<HTMLElement | null>(null)
const width = ref(600)
const height = 112
const left = 34
const hover = ref<number | null>(null)
const observer = new ResizeObserver(([entry]) => {
    width.value = Math.max(200, Math.round(entry!.contentRect.width))
})
onMounted(() => box.value && observer.observe(box.value))
onBeforeUnmount(() => observer.disconnect())

// The scale fits the samples between 2× and 20×: the short burst ffmpeg reports at the start of a run would flatten the rest
const top = computed(() => Math.min(20, Math.max(2, Math.ceil(Math.max(...props.history.map((s) => s.speed), 0) * 1.15))))
const y = (speed: number) => height - 6 - (Math.min(speed, top.value) / top.value) * (height - 16)

const points = computed(() => {
    const times = props.history.map((s) => new Date(s.at).getTime())
    const first = Math.min(...times)
    const span = Math.max(Math.max(...times) - first, 1)
    return props.history.map((sample, index) => ({ x: left + ((times[index]! - first) / span) * (width.value - left), y: y(sample.speed) }))
})

const line = computed(() => points.value.map((p) => `${p.x.toFixed(1)},${p.y.toFixed(1)}`).join(' '))

const summary = computed(() => {
    const last = props.history.at(-1)
    return last ? `Encoding speed over the last minutes, now ${last.speed.toFixed(1)} times realtime` : 'No encoding speed reported yet'
})

function move(event: MouseEvent) {
    const svg = event.currentTarget as SVGSVGElement
    const x = ((event.clientX - svg.getBoundingClientRect().left) / svg.getBoundingClientRect().width) * width.value
    let nearest: number | null = null
    points.value.forEach((p, index) => {
        if (nearest === null || Math.abs(p.x - x) < Math.abs(points.value[nearest]!.x - x)) {
            nearest = index
        }
    })
    hover.value = nearest
}
</script>
