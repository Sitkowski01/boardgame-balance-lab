import { resolve } from "node:path";
import { PostgreSqlContainer } from "@testcontainers/postgresql";
import { defineConfig, devices } from "@playwright/test";

// E2E na prawdziwym serwerze gry (BalanceLab.Api) z PostgreSQL, ktory serwuje tez zbudowany front (web/dist)
// — tak jak w produkcji: jeden adres, bez proxy. Front buduje wczesniej `pnpm e2e` (vite build).
// Baza: BALANCE_LAB_DB z otoczenia (CI ma kontener uslugi), a bez niej wlasny kontener (Testcontainers).
// Konfiguracje czytaja tez procesy robocze Playwrighta; dziedzicza zmienna, wiec kontener startuje raz.
if (process.env.BALANCE_LAB_DB === undefined) {
  const db = await new PostgreSqlContainer("postgres:17-alpine").start();
  process.env.BALANCE_LAB_DB = `Host=${db.getHost()};Port=${String(db.getPort())};Username=${db.getUsername()};Password=${db.getPassword()};Database=${db.getDatabase()}`;
}

const port = 5199;
const root = resolve(import.meta.dirname, "..", "..");

// PW_CHANNEL=msedge: przegladarka z systemu, gdy nie da sie pobrac Chromium Playwrighta.
const browser = {
  ...devices["Desktop Chrome"],
  ...(process.env.PW_CHANNEL === undefined ? {} : { channel: process.env.PW_CHANNEL }),
};

export default defineConfig({
  testDir: ".",
  // Partie dziela jeden serwer, ale kazdy test zaklada wlasne pokoje — moga isc rownolegle.
  fullyParallel: true,
  forbidOnly: process.env.CI !== undefined,
  retries: process.env.CI === undefined ? 0 : 1,
  timeout: 90_000,
  reporter: process.env.CI === undefined ? "list" : [["list"], ["html", { open: "never" }]],
  use: { ...browser, baseURL: `http://127.0.0.1:${String(port)}`, trace: "retain-on-failure" },
  webServer: {
    command: `dotnet run --project "${resolve(root, "src", "BalanceLab.Api")}" --no-launch-profile`,
    url: `http://127.0.0.1:${String(port)}/health`,
    reuseExistingServer: false,
    timeout: 240_000,
    env: {
      ASPNETCORE_URLS: `http://127.0.0.1:${String(port)}`,
      BALANCE_LAB_DB: process.env.BALANCE_LAB_DB,
      Web__Root: resolve(root, "web", "dist"),
      // Szybkie boty i bez limitu zadan: testy klikaja szybciej niz czlowiek.
      Rooms__BotDelay: "00:00:00.200",
      Rooms__FillBot: "random",
      Rooms__AutopilotBot: "random",
      RateLimit__PerMinute: "100000",
    },
  },
});
