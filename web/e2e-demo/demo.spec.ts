import { expect, test, type Page } from "@playwright/test";

const home = "/balance-lab/";

/** Strona demo z serwerem gotowym w przegladarce. */
async function openDemo(page: Page): Promise<void> {
  await page.goto(home);
  await expect(page.getByRole("status").filter({ hasText: "Serwer gry działa w tej karcie" })).toBeVisible({ timeout: 60_000 });
}

// Lobby od pierwszej chwili ma pokazowe partie Ali z botami, a skonczona ma powtorke — wszystko liczy przegladarka.
test("showcase games play on their own and a finished one can be replayed", async ({ page }) => {
  await openDemo(page);
  const rooms = page.getByRole("region", { name: "Pokoje" });
  await expect(rooms).toContainText("Ala");

  await expect(rooms.getByRole("heading", { name: "Ostatnie partie" })).toBeVisible({ timeout: 150_000 });
  // Skonczona partia w lobby prowadzi prosto do powtorki.
  await rooms.locator(".rooms.finished a").first().click();
  await expect(page).toHaveURL(/\/balance-lab\/matches\/[0-9a-f-]{36}$/u);
  await expect(page.getByLabel("Pozycja")).toHaveText(/^ruch 0 z \d+$/u);
  await page.getByRole("button", { name: "Następny ›" }).click();
  await expect(page.getByLabel("Pozycja")).toHaveText(/^ruch 1 z \d+$/u);
});

// Cala partia z botem w Czworki na serwerze w przegladarce, z wynikiem i powtorka.
test("a game against a bot runs in the browser to the result and its replay", async ({ page }) => {
  await openDemo(page);
  const form = page.getByRole("form", { name: "Nowy pokój" });
  await form.getByLabel("Twoje imię").fill("Ola");
  await form.getByRole("radio", { name: "Czwórki" }).check();
  await form.getByRole("button", { name: "Załóż pokój" }).click();
  await expect(page).toHaveURL(/\/balance-lab\/rooms\/[0-9a-f-]{36}$/u);
  // W demo nie ma linku dla znajomych: stol jest tylko w tej karcie.
  await expect(page.getByRole("button", { name: /Kopiuj link/u })).toHaveCount(0);
  await page.getByLabel("Bot na miejsce 2").selectOption({ label: "Losowy" });
  await page.getByRole("button", { name: "Dodaj bota" }).first().click();
  await page.getByRole("button", { name: "Zacznij partię" }).click();

  const finished = page.getByRole("heading", { name: "Koniec partii" });
  const drop = page.getByRole("button", { name: /^Kolumna \d$/u }).and(page.locator(":enabled"));
  for (let turn = 0; turn < 30 && !(await finished.isVisible()); turn++) {
    await expect(page.getByText("Twój ruch!").or(finished)).toBeVisible({ timeout: 30_000 });
    if (await finished.isVisible()) break;
    const lastMove = page.getByRole("region", { name: "Ostatnie ruchy" }).getByRole("listitem").last();
    const before = (await lastMove.count()) === 0 ? 0 : Number(await lastMove.getAttribute("value"));
    await drop.first().click();
    await expect.poll(async () => Number(await lastMove.getAttribute("value"))).toBeGreaterThan(before);
  }

  await expect(finished).toBeVisible();
  await page.getByRole("link", { name: "Obejrzyj powtórkę" }).click();
  await expect(page.getByRole("heading", { name: "Powtórka: Czwórki" })).toBeVisible();
  await expect(page.getByRole("img", { name: "puste" })).toHaveCount(42);
});

// Laboratorium w osobnym workerze: gotowa liga z pokazu i nowy turniej zamowiony z formularza.
test("the lab shows the showcase league and computes a new tournament", async ({ page }) => {
  await openDemo(page);
  await page.getByRole("link", { name: "Laboratorium", exact: true }).click();
  await expect(page).toHaveURL(/\/balance-lab\/lab$/u);
  const recent = page.getByRole("region", { name: "Ostatnie eksperymenty" });
  await expect(recent).toContainText("Liga: Klejnoty", { timeout: 60_000 });

  const form = page.getByRole("form", { name: "Nowy eksperyment" });
  await form.getByLabel("Gra").selectOption({ label: "Czwórki" });
  await form.getByLabel("Turniej").check();
  await form.getByLabel("Partie").fill("20");
  await form.getByRole("button", { name: "Uruchom eksperyment" }).click();

  await expect(page).toHaveURL(/\/balance-lab\/lab\/[0-9a-f-]{36}$/u);
  await expect(page.getByRole("region", { name: "Wyniki turnieju" })).toBeVisible({ timeout: 120_000 });
});

// Adres w aplikacji otwarty wprost (odswiezenie, link z zewnatrz) trafia na wlasciwa strone pod /balance-lab/.
test("a deep link opens its page under the base path", async ({ page }) => {
  await page.goto("/balance-lab/lab");
  await expect(page.getByRole("heading", { name: "Laboratorium balansu" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Balance Lab" })).toHaveAttribute("href", "/balance-lab/");

  await page.goto("/balance-lab/nie-ma-takiej");
  await expect(page.getByRole("heading", { name: "Nie ma takiej strony" })).toBeVisible();
});
