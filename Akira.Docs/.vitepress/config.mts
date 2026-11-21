import { defineConfig } from 'vitepress'

// GitHub Pages serves the guide under the repository's name; the guide image serves it at the root
const base = process.env.DOCS_BASE ?? '/'

export default defineConfig({
    base,
    vite: {
        server: {
            watch: {
                usePolling: true
            }
        }
    },
    title: 'Akira',
    description: 'Documentation for Akira, a headless media server.',
    head: [['link', { rel: 'icon', type: 'image/svg+xml', href: `${base}akira-mark.svg` }]],
    themeConfig: {
        logo: '/akira-mark.svg',
        outline: { level: [2, 3] },
        nav: [
            { text: 'Home', link: '/' },
            { text: 'Getting Started', link: '/getting-started/introduction' },
            { text: 'Frontend', link: '/frontend/' },
            { text: 'Developers', link: '/developers/architecture' }
        ],
        sidebar: [
            {
                text: 'Getting Started',
                items: [
                    { text: 'What is Akira', link: '/getting-started/introduction' },
                    { text: 'Installation', link: '/getting-started/installation' },
                    { text: 'Configuration', link: '/getting-started/configuration' },
                    { text: 'Initial setup', link: '/getting-started/setup' }
                ]
            },
            {
                text: 'Frontend',
                items: [
                    { text: 'Build your own frontend', link: '/frontend/' },
                    { text: 'With an AI assistant', link: '/frontend/ai-assistant' }
                ]
            },
            {
                text: 'Features',
                items: [
                    { text: 'Playback', link: '/features/playback' },
                    { text: 'Subtitles', link: '/features/subtitles' }
                ]
            },
            { text: 'Troubleshooting', link: '/troubleshooting' },
            {
                text: 'Developers',
                items: [
                    { text: 'Architecture', link: '/developers/architecture' },
                    { text: 'API', link: '/developers/api' },
                    { text: 'Plugin API', link: '/developers/plugins' },
                    { text: 'Building', link: '/developers/building' }
                ]
            }
        ],
        search: {
            provider: 'local'
        }
    }
})
