import { defineConfig, devices } from "@playwright/test";

// E2E demo: zbudowana strona statyczna (dist-demo, `pnpm build:demo`) pod /balance-lab/, bez zadnego serwera gry —
// lobby, boty, powtorki i laboratorium licza sie w przegladarce (.NET w WebAssembly w Web Workerach).
const port = 4711;

const browser = {
  ...devices["Desktop Chrome"],
  ...(process.env.PW_CHANNEL === undefined ? {} : { channel: process.env.PW_CHANNEL }),
};

export default defineConfig({
  testDir: ".",
  // Kazda karta ma wlasny serwer, wiec testy moga isc rownolegle.
  fullyParallel: true,
  forbidOnly: process.env.CI !== undefined,
  retries: process.env.CI === undefined ? 0 : 1,
  timeout: 180_000,
  reporter: process.env.CI === undefined ? "list" : [["list"], ["html", { open: "never", outputFolder: "../playwright-report-demo" }]],
  use: { ...browser, baseURL: `http://127.0.0.1:${String(port)}`, trace: "retain-on-failure" },
  webServer: {
    command: `pnpm exec vite preview --mode demo --host 127.0.0.1 --port ${String(port)} --strictPort`,
    url: `http://127.0.0.1:${String(port)}/balance-lab/`,
    // Vite z konfiguracja web/ (tryb demo: baza /balance-lab/, katalog dist-demo).
    cwd: "..",
    reuseExistingServer: false,
    timeout: 60_000,
  },
});
