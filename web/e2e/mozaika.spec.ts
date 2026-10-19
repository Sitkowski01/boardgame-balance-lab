import { expect, test } from "@playwright/test";
import { addBot, createRoom, yourTurn } from "./table.js";

// Druga prawdziwa gra przez ten sam serwer, dziennik i powtorke: cala partia Mozaiki czlowieka z botem.
test("a full game of Mozaika against a bot goes round after round and can be replayed", async ({ page }) => {
  test.setTimeout(240_000);
  await createRoom(page, { name: "Ala", game: "Mozaika" });
  await addBot(page, 2, "Zachłanny (mocny)");
  await page.getByRole("button", { name: "Zacznij partię" }).click();

  const finished = page.getByRole("heading", { name: "Koniec partii" });
  const me = page.getByLabel("Gracz Ala");
  const tiles = page.getByRole("region", { name: "Warsztaty" }).getByRole("button").and(page.locator(":enabled"));
  const lastMove = page.getByRole("region", { name: "Ostatnie ruchy" }).getByRole("listitem").last();
  await expect(page.getByLabel("Runda")).toContainText("Runda 1");
  for (let turn = 0; turn < 150; turn++) {
    await expect(yourTurn(page).or(finished)).toBeVisible();
    if (await finished.isVisible()) {
      break;
    }

    const before = (await lastMove.count()) === 0 ? 0 : Number(await lastMove.getAttribute("value"));
    // Pierwsze kafelki, jakie wolno wziac, na pierwszy wolny rzad; gdy zaden ich nie przyjmie, na podloge.
    await tiles.first().click();
    const line = me.getByRole("button", { name: /^Połóż na rząd/u });
    await ((await line.count()) > 0 ? line.first() : me.getByRole("button", { name: "Połóż na podłogę" })).click();
    await expect.poll(async () => Number(await lastMove.getAttribute("value"))).toBeGreaterThan(before);
  }

  await expect(finished).toBeVisible();
  await expect(page.getByText(/^Wygrywa: |^Remis/u).first()).toBeVisible();
  // Partia trwa kilka rund: na scianie Ali leza juz kafelki.
  await expect(me.getByRole("gridcell", { selected: true }).first()).toBeVisible();

  await page.getByRole("link", { name: "Obejrzyj powtórkę" }).click();
  await expect(page.getByRole("heading", { name: "Powtórka: Mozaika" })).toBeVisible();
  await expect(page.getByLabel("Pozycja")).toHaveText(/^ruch 0 z \d+$/u);
  await expect(page.getByLabel("Runda")).toContainText("Runda 1");
  await expect(page.getByLabel("Gracz Ala").getByRole("gridcell", { selected: true })).toHaveCount(0);

  const total = Number((await page.getByLabel("Pozycja").textContent())?.split(" z ")[1]);
  await page.getByRole("slider", { name: "Ruch" }).fill(String(total));
  await expect(page.getByLabel("Pozycja")).toHaveText(`ruch ${String(total)} z ${String(total)}`);
  await expect(page.getByLabel("Gracz Ala").getByRole("gridcell", { selected: true }).first()).toBeVisible();
});
