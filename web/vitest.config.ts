import { defineConfig, mergeConfig } from "vitest/config";
import viteConfig from "./vite.config.js";

export default mergeConfig(
  viteConfig,
  defineConfig({
    test: {
      environment: "jsdom",
      // jsdom i klikanie przez user-event sa wolne na obciazonej maszynie (CI, rownolegle pliki).
      testTimeout: 15_000,
      include: ["src/**/*.test.{ts,tsx}"],
      // Pierwsze commity frontu (sama konfiguracja) nie maja jeszcze testow.
      passWithNoTests: true,
      setupFiles: ["src/test/setup.ts"],
      coverage: {
        provider: "v8",
        include: ["src/**/*.{ts,tsx}"],
        // main.tsx tylko montuje aplikacje, a hub.ts to cienka nakladka na klienta SignalR — oba sprawdza e2e.
        exclude: ["src/**/*.test.{ts,tsx}", "src/test/**", "src/main.tsx", "src/api/hub.ts"],
        thresholds: { lines: 95, branches: 90, functions: 95, statements: 95 },
      },
    },
  }),
);
