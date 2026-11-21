import { onMounted, ref, type Ref } from 'vue'

export function useLoad<T>(load: () => Promise<T>) {
    const data = ref<T | null>(null) as Ref<T | null>
    const error = ref<string | null>(null)
    const loading = ref(false)

    const reload = async () => {
        loading.value = true
        try {
            data.value = await load()
            error.value = null
        } catch (caught) {
            error.value = caught instanceof Error ? caught.message : String(caught)
        } finally {
            loading.value = false
        }
    }

    onMounted(reload)
    return { data, error, loading, reload }
}

// Runs an action that changes something, and keeps its error for the page to show
export function useAction() {
    const error = ref<string | null>(null)
    const busy = ref(false)

    const run = async (action: () => Promise<unknown>) => {
        busy.value = true
        error.value = null
        try {
            await action()
            return true
        } catch (caught) {
            error.value = caught instanceof Error ? caught.message : String(caught)
            return false
        } finally {
            busy.value = false
        }
    }

    return { error, busy, run }
}
