import { expect, type Page } from "@playwright/test";

/** Zaklada pokoj z lobby i czeka na stol (adres /rooms/{id}). */
export async function createRoom(page: Page, options: { name: string; game: string; seats?: number }): Promise<string> {
  await page.goto("/");
  const form = page.getByRole("form", { name: "Nowy pokój" });
  await form.getByLabel("Twoje imię").fill(options.name);
  await form.getByRole("radio", { name: options.game }).check();
  if (options.seats !== undefined) {
    await form.getByLabel("Miejsca").selectOption(String(options.seats));
  }

  await form.getByRole("button", { name: "Załóż pokój" }).click();
  await expect(page).toHaveURL(/\/rooms\/[0-9a-f-]{36}$/u);
  await expect(page.getByRole("button", { name: "Zacznij partię" })).toBeVisible();
  return page.url();
}

export async function addBot(page: Page, seat: number, label: string): Promise<void> {
  await page.getByLabel(`Bot na miejsce ${String(seat)}`).selectOption({ label });
  await page.getByRole("button", { name: "Dodaj bota" }).first().click();
}

export const yourTurn = (page: Page) => page.getByText("Twój ruch!");
