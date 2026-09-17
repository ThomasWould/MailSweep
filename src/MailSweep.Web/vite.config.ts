import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'
import { readFileSync } from 'node:fs'
import { homedir } from 'node:os'
import { join } from 'node:path'

const certificateDirectory = join(homedir(), '.aspnet', 'https')

// https://vite.dev/config/
export default defineConfig(({ command }) => ({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
    https: command === 'serve' ? {
      cert: readFileSync(join(certificateDirectory, 'mailsweep-vite.pem')),
      key: readFileSync(join(certificateDirectory, 'mailsweep-vite.key')),
    } : undefined,
  },
}))
