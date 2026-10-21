import { expect, test } from "@playwright/test";
import { addBot, createRoom, yourTurn } from "./table.js";

// Cala partia z botem, a potem jej powtorka z dziennika w PostgreSQL przewijana suwakiem.
test("a full game against a bot ends with a result and a replay that can be scrolled", async ({ page }) => {
  await createRoom(page, { name: "Ala", game: "Czwórki" });
  await addBot(page, 2, "Losowy");
  await page.getByRole("button", { name: "Zacznij partię" }).click();

  const finished = page.getByRole("heading", { name: "Koniec partii" });
  const drop = page.getByRole("button", { name: /^Kolumna \d$/u }).and(page.locator(":enabled"));
  for (let turn = 0; turn < 30 && !(await finished.isVisible()); turn++) {
    await expect(yourTurn(page).or(finished)).toBeVisible();
    if (await finished.isVisible()) {
      break;
    }

    // Numer ostatniego ruchu na liscie (lista trzyma tylko kilka ostatnich, wiec liczba pozycji przestaje rosnac).
    const lastMove = page.getByRole("region", { name: "Ostatnie ruchy" }).getByRole("listitem").last();
    const before = (await lastMove.count()) === 0 ? 0 : Number(await lastMove.getAttribute("value"));
    await drop.first().click();
    // Czeka na swoj ruch na liscie, zanim zacznie nastepna ture.
    await expect.poll(async () => Number(await lastMove.getAttribute("value"))).toBeGreaterThan(before);
  }

  await expect(finished).toBeVisible();
  await expect(page.getByText(/^Wygrywa: |^Remis/u).first()).toBeVisible();

  await page.getByRole("link", { name: "Obejrzyj powtórkę" }).click();
  await expect(page).toHaveURL(/\/matches\/[0-9a-f-]{36}$/u);
  await expect(page.getByRole("heading", { name: "Powtórka: Czwórki" })).toBeVisible();
  await expect(page.getByLabel("Pozycja")).toHaveText(/^ruch 0 z \d+$/u);
  // Najpierw plansza (klatka z serwera), dopiero potem liczenie krazkow — inaczej 0 wyszloby przed wczytaniem.
  await expect(page.getByRole("img", { name: "puste" })).toHaveCount(42);
  await expect(page.getByRole("img", { name: /Ala|MCTS|Losowy/u })).toHaveCount(0);

  await page.getByRole("button", { name: "Następny ▶" }).click();
  await expect(page.getByRole("img", { name: "Ala" })).toHaveCount(1);

  const total = Number((await page.getByLabel("Pozycja").textContent())?.split(" z ")[1]);
  await page.getByRole("slider", { name: "Ruch" }).fill(String(total));
  await expect(page.getByLabel("Pozycja")).toHaveText(`ruch ${String(total)} z ${String(total)}`);
  await expect(page.getByRole("img", { name: /^(Ala|Losowy)$/u })).toHaveCount(total);

  // Po odswiezeniu powtorka otwiera sie z samego adresu (API serwuje index.html dla /matches/...).
  await page.reload();
  await expect(page.getByRole("heading", { name: "Powtórka: Czwórki" })).toBeVisible();
});
