import { describe, expect, it } from "vitest";
import { gameBlurbs, gameRules, gamesFact, gameTitle, itemName, playerName, seatName } from "./names";

describe("names for people", () => {
  it("names Connect Four in Polish and leaves other games and variants alone", () => {
    expect(gameTitle("ConnectFour")).toBe("Czwórki");
    expect(gameTitle("Klejnoty (win=10)")).toBe("Klejnoty (win=10)");
    expect(gameTitle("Mozaika")).toBe("Mozaika");
  });

  it("has a blurb for every game in the catalog", () => {
    expect(Object.keys(gameBlurbs).sort()).toEqual(["connectfour", "klejnoty", "mozaika"]);
  });

  it("explains every game of the catalog in exactly three steps", () => {
    expect(Object.keys(gameRules).sort()).toEqual(["connectfour", "klejnoty", "mozaika"]);
    expect(Object.values(gameRules).every((steps) => steps.length === 3 && steps.every((s) => s.endsWith(".")))).toBe(true);
  });

  it("translates bot names with their settings and keeps people's names", () => {
    expect(playerName("greedy")).toBe("Zachłanny");
    expect(playerName("greedy:kolory")).toBe("Zachłanny: kolory");
    expect(playerName("random")).toBe("Losowy");
    expect(playerName("mcts:500")).toBe("MCTS 500");
    expect(playerName("ismcts:300")).toBe("ISMCTS 300");
    expect(playerName("ismcts:3a")).toBe("ISMCTS: 3a");
    expect(playerName("Ala")).toBe("Ala");
    expect(playerName("greedyAla")).toBe("greedyAla");
    expect(playerName("Ala greedy")).toBe("Ala greedy");
    expect(playerName("greedy:")).toBe("greedy:");
  });
});

describe("item names", () => {
  it("spells out a Klejnoty card in words and leaves other items alone", () => {
    expect(itemName("#17 poz.1 U 0 pkt, koszt W1 G1 R2 K1")).toBe(
      "#17 · I · niebieska, 0 pkt · koszt: 1 biały, 1 zielony, 2 czerwone, 1 czarny",
    );
    expect(itemName("#83 poz.3 R 4 pkt, koszt G7")).toBe("#83 · III · czerwona, 4 pkt · koszt: 7 zielonych");
    expect(itemName("#5 poz.2 W 1 pkt, koszt U12 K22")).toBe("#5 · II · biała, 1 pkt · koszt: 12 niebieskich, 22 czarne");
    expect(itemName("rząd 1, kolumna 2 (turkus)")).toBe("rząd 1, kolumna 2 (turkus)");
    expect(itemName("#17 poz.4 U 0 pkt, koszt W1")).toBe("#17 poz.4 U 0 pkt, koszt W1");
    expect(itemName("x#17 poz.1 U 0 pkt, koszt W1")).toBe("x#17 poz.1 U 0 pkt, koszt W1");
  });
});

describe("lobby facts", () => {
  const game = (minPlayers: number, maxPlayers: number) => ({
    key: "g",
    name: "G",
    minPlayers,
    maxPlayers,
    hasVariants: false,
    variantSyntax: null,
    profiles: [],
  });

  it("counts games with the right Polish noun and the player range", () => {
    expect(gamesFact([game(2, 2), game(2, 4), game(2, 4)])).toBe("3 gry, od 2 do 4 graczy");
    expect(gamesFact([game(2, 2)])).toBe("1 gra, 2 graczy");
    expect(gamesFact(Array.from({ length: 5 }, () => game(1, 6)))).toBe("5 gier, od 1 do 6 graczy");
    expect(gamesFact(Array.from({ length: 12 }, () => game(2, 3)))).toBe("12 gier, od 2 do 3 graczy");
    expect(gamesFact(Array.from({ length: 22 }, () => game(3, 3)))).toBe("22 gry, 3 graczy");
    expect(gamesFact(Array.from({ length: 14 }, () => game(2, 2)))).toBe("14 gier, 2 graczy");
    expect(gamesFact(Array.from({ length: 11 }, () => game(2, 2)))).toBe("11 gier, 2 graczy");
  });

  it("names a seat for people", () => {
    expect(seatName({ kind: "Bot", name: "greedy:kolory" })).toBe("Zachłanny: kolory");
    expect(seatName({ kind: "Bot", name: null })).toBe("");
    expect(seatName({ kind: "Human", name: "random" })).toBe("random");
    expect(seatName({ kind: "Empty", name: null })).toBeNull();
  });
});
