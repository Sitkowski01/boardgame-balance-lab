import { defineConfig, mergeConfig } from "vitest/config";
import viteConfig from "./vite.config.js";

// Konfiguracja Vite jest funkcja trybu (zwykly albo demo); testy biora zwykly.
export default defineConfig((env) =>
  mergeConfig(
    viteConfig(env),
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
          // main.tsx tylko montuje aplikacje, hub.ts to cienka nakladka na klienta SignalR, a demo/worker.ts na runtime .NET
          // w workerze — sprawdzaja je e2e (na serwerze i w demo).
          exclude: ["src/**/*.test.{ts,tsx}", "src/test/**", "src/main.tsx", "src/api/hub.ts", "src/demo/worker.ts"],
          thresholds: { lines: 95, branches: 90, functions: 95, statements: 95 },
        },
      },
    }),
  ),
);
