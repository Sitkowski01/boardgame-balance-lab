import { describe, expect, it } from "vitest";
import { gameBlurbs, gamesFact, gameTitle, playerName, seatName } from "./names";

describe("names for people", () => {
  it("names Connect Four in Polish and leaves other games and variants alone", () => {
    expect(gameTitle("ConnectFour")).toBe("Czwórki");
    expect(gameTitle("Klejnoty (win=10)")).toBe("Klejnoty (win=10)");
    expect(gameTitle("Mozaika")).toBe("Mozaika");
  });

  it("has a blurb for every game in the catalog", () => {
    expect(Object.keys(gameBlurbs).sort()).toEqual(["connectfour", "klejnoty", "mozaika"]);
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
