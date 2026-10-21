import { expect, test } from "@playwright/test";

// Laboratorium na prawdziwym serwerze: zamowienie z formularza, postep, wyniki z wykresami i wlasny adres eksperymentu.
test("a league of Mozaika bots runs from the lab and its result survives a reload", async ({ page }) => {
  test.setTimeout(180_000);
  await page.goto("/");
  await page.getByRole("link", { name: "laboratorium", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Laboratorium balansu" })).toBeVisible();

  const form = page.getByRole("form", { name: "Nowy eksperyment" });
  await form.getByLabel("Gra").selectOption({ label: "Mozaika" });
  await form.getByLabel("Liga").check();
  await form.getByLabel("Partie na parę").fill("4");
  await form.getByRole("button", { name: "Uruchom eksperyment" }).click();

  await expect(page).toHaveURL(/\/lab\/[0-9a-f-]{36}$/u);
  await expect(page.getByRole("region", { name: "Wyniki ligi" })).toBeVisible({ timeout: 120_000 });
  await expect(page.getByRole("progressbar", { name: "Postęp eksperymentu" })).toHaveAttribute("aria-valuetext", "12 z 12 partii");
  await expect(page.getByRole("list", { name: "Ranking Elo" }).getByRole("listitem")).toHaveCount(3);
  await expect(page.getByRole("table", { name: "Kto bije kogo" }).getByRole("row")).toHaveCount(4);

  await page.reload();
  await expect(page.getByRole("region", { name: "Wyniki ligi" })).toBeVisible();
  await expect(page.getByRole("region", { name: "Ostatnie eksperymenty" })).toContainText("Liga: Mozaika");
});

test("a what-if comparison of Klejnoty shows the changed rule against the original", async ({ page }) => {
  test.setTimeout(180_000);
  await page.goto("/lab");

  const form = page.getByRole("form", { name: "Nowy eksperyment" });
  await form.getByLabel("Gra").selectOption({ label: "Klejnoty" });
  await form.getByLabel("Co jeśli").check();
  await expect(form.getByRole("button", { name: "Uruchom eksperyment" })).toBeDisabled();
  await form.getByLabel("Wariant (co jeśli)").fill("win=10");
  await form.getByLabel("Partie").fill("20");
  await form.getByRole("button", { name: "Uruchom eksperyment" }).click();

  const results = page.getByRole("region", { name: "Wyniki porównania" });
  await expect(results).toBeVisible({ timeout: 120_000 });
  await expect(results).toContainText("Klejnoty (win=10)");
  await expect(results.getByRole("list", { name: "Boty: zmiana odsetka wygranych" }).getByRole("listitem")).toHaveCount(2);
  await expect(results.getByText("Długość partii")).toBeVisible();
});

test("a bad order is explained by the server and nothing starts", async ({ page }) => {
  await page.goto("/lab");
  const form = page.getByRole("form", { name: "Nowy eksperyment" });
  await form.getByLabel("Gra").selectOption({ label: "Klejnoty" });
  await form.getByLabel("Wariant zasad (opcjonalnie)").fill("win=abc");
  await form.getByRole("button", { name: "Uruchom eksperyment" }).click();

  await expect(form.getByRole("alert")).toContainText("'abc' to nie liczba");
  await expect(page).toHaveURL(/\/lab$/u);
});
