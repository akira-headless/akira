<template>
    <button :class="armed ? 'button-danger bg-red-950' : 'button-danger'" :disabled="disabled" @click="click">
        {{ armed ? confirmLabel : label }}
    </button>
</template>

<script setup lang="ts">
import { onBeforeUnmount, ref } from 'vue'

const props = withDefaults(defineProps<{ label: string; confirmLabel?: string; disabled?: boolean }>(), {
    confirmLabel: 'Click again to confirm',
    disabled: false
})
const emit = defineEmits<{ confirm: [] }>()

const armed = ref(false)
let timer: ReturnType<typeof setTimeout> | null = null

// A second click within a few seconds confirms, so a stray click never deletes anything
function click() {
    if (props.disabled) {
        return
    }
    if (armed.value) {
        disarm()
        emit('confirm')
        return
    }
    armed.value = true
    timer = setTimeout(disarm, 4000)
}

function disarm() {
    armed.value = false
    if (timer) {
        clearTimeout(timer)
        timer = null
    }
}

onBeforeUnmount(disarm)
</script>
