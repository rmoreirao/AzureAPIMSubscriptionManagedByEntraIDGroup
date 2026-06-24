import {defineConfig} from "vite"

export default defineConfig({
  root: "./src",
  base: "",
  server: {
    port: 3000,
  },
  build: {
    outDir: "../dist",
    emptyOutDir: true,
    rollupOptions: {
      input: {
        index: "./src/index.html",
        editor: "./src/editor.html",
      },
    },
  },
  publicDir: "../static",
})