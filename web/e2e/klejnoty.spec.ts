import { expect, test } from "@playwright/test";
import { addBot, createRoom, yourTurn } from "./table.js";

// Dwoch ludzi w osobnych przegladarkach (osobne sessionStorage = osobne bilety) i bot przy jednym stole.
test("two players and a bot play Klejnoty live, a hidden card stays hidden and a reload keeps the seat", async ({ browser }) => {
  const alice = await (await browser.newContext()).newPage();
  const olga = await (await browser.newContext()).newPage();

  const table = await createRoom(alice, { name: "Ala", game: "Klejnoty", seats: 3 });

  await olga.goto("/");
  const room = olga.getByRole("listitem", { name: "Pokój Klejnoty" }).filter({ hasText: "Ala" }).first();
  await room.getByRole("link", { name: "Dołącz" }).click();
  await expect(olga).toHaveURL(table);
  await olga.getByLabel("Twoje imię").fill("Ola");
  await olga.getByRole("button", { name: "Usiądź przy stole" }).click();
  await expect(olga.getByText("Czekamy, aż gospodarz zacznie partię.")).toBeVisible();
  await expect(alice.getByRole("list", { name: "Miejsca" })).toContainText("Ola");

  await addBot(alice, 3, "Losowy");
  await expect(alice.getByRole("list", { name: "Miejsca" })).toContainText("Losowy");
  await alice.getByRole("button", { name: "Zacznij partię" }).click();

  // Ala bierze trzy rozne zetony.
  await expect(yourTurn(alice)).toBeVisible();
  await expect(yourTurn(olga)).toBeHidden();
  for (const color of ["biały", "niebieski", "zielony"]) {
    await alice.getByRole("button", { name: new RegExp(`^Bank: ${color}`, "u") }).click();
  }
  await alice.getByRole("button", { name: "Weź żetony" }).click();
  await expect(olga.getByRole("region", { name: "Ostatnie ruchy" })).toContainText("Ala: bierze biały, niebieski, zielony");

  // Ola bierze dwa czerwone (w banku 5 przy trzech graczach).
  await expect(yourTurn(olga)).toBeVisible();
  await olga.getByRole("button", { name: /^Bank: czerwony/u }).click();
  await olga.getByRole("button", { name: /^Bank: czerwony/u }).click();
  await olga.getByRole("button", { name: "Weź żetony" }).click();
  await expect(alice.getByRole("region", { name: "Ostatnie ruchy" })).toContainText("Ola: bierze dwa: czerwony");

  // Bot gra sam, potem znow Ala: rezerwuje w ciemno z talii 1.
  await expect(yourTurn(alice)).toBeVisible();
  await expect(alice.getByRole("region", { name: "Ostatnie ruchy" })).toContainText("Losowy:");
  await alice.getByLabel("Talia 1").getByRole("button", { name: "Rezerwuj w ciemno" }).click();

  // Ala widzi swoja karte, Ola tylko jej poziom.
  await expect(
    alice
      .getByLabel("Gracz Ala")
      .getByLabel("Rezerwa")
      .getByLabel(/^Karta #\d+$/u),
  ).toBeVisible();
  await expect(olga.getByLabel("Gracz Ala").getByLabel("Zakryta karta poziomu 1")).toBeVisible();
  await expect(olga.getByLabel("Gracz Ala").getByLabel(/^Karta #/u)).toHaveCount(0);

  // Odswiezenie strony: Ola wraca na swoje miejsce z biletem z tej karty.
  await olga.reload();
  await expect(olga.getByLabel("Gracz Ola").getByText("ty", { exact: true })).toBeVisible();
  await expect(yourTurn(olga)).toBeVisible();
  await expect(olga.getByLabel("Gracz Ala").getByLabel("Zakryta karta poziomu 1")).toBeVisible();
});
