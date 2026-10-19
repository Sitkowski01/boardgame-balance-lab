import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

// W trybie dev front i serwer gry sa pod jednym adresem: proxy przekazuje REST i hub (WebSocket) do API.
// W produkcji API samo serwuje zbudowany front (Web:Root), wiec sciezki sa te same.
const api = process.env.API_URL ?? "http://127.0.0.1:5080";

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      "/api": { target: api, changeOrigin: true },
      "/hubs": { target: api, changeOrigin: true, ws: true },
    },
  },
});
