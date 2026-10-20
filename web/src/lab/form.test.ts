import fc from "fast-check";
import { describe, expect, it } from "vitest";
import { games } from "../test/labReports";
import { botChoices, botLabel, DefaultLimits as Limits, defaultForm, grouped, problems, toRequest, weight, work } from "./form";

const [connectFour, klejnoty, mozaika] = games as [(typeof games)[0], (typeof games)[0], (typeof games)[0]];

describe("experiment form", () => {
  it("offers greedy profiles of the game and search within the server limit", () => {
    expect(botChoices(mozaika).map((b) => b.spec)).toEqual([
      "greedy",
      "greedy:kolumny",
      "greedy:ostrozny",
      "mcts:50",
      "mcts:200",
      "ismcts:50",
      "ismcts:200",
      "random",
    ]);
    expect(botChoices(connectFour).map((b) => b.spec)).not.toContain("greedy");
    expect(botLabel(mozaika, "greedy:kolumny")).toBe("Zachłanny: kolumny");
    expect(botLabel(mozaika, "mcts:7")).toBe("mcts:7");
  });

  it("starts with two different bots the game understands", () => {
    expect(defaultForm(mozaika)).toEqual({
      game: "mozaika",
      kind: "Tournament",
      bots: ["greedy", "greedy:kolumny"],
      games: "100",
      seed: "1",
      variant: "",
    });
    expect(defaultForm(connectFour).bots).toEqual(["mcts:50", "random"]);
    expect(defaultForm(klejnoty, "League")).toMatchObject({ kind: "League", bots: ["greedy", "greedy:punkty", "random"], games: "40" });
    expect(defaultForm({ ...klejnoty, profiles: [] }, "League").bots).toEqual(["mcts:50", "random"]); // bez powtorzen
  });

  it("finds nothing wrong with the defaults of every game and kind", () => {
    for (const game of games) {
      for (const kind of ["Tournament", "League"] as const) {
        expect(problems(defaultForm(game, kind), game)).toEqual([]);
      }
    }
  });

  it("says what is wrong, field by field", () => {
    const form = defaultForm(klejnoty);

    expect(problems({ ...form, bots: ["random"] }, klejnoty)).toEqual(["Klejnoty to gra dla 2–4 graczy — tylu botów potrzeba przy stole."]);
    expect(problems({ ...form, bots: Array<string>(5).fill("random"), games: "100" }, klejnoty)[0]).toContain("2–4 graczy");
    expect(problems({ ...form, kind: "League", bots: ["random"] }, klejnoty)).toEqual(["Liga potrzebuje co najmniej dwóch botów."]);
    expect(problems({ ...form, kind: "League", bots: Array<string>(7).fill("random"), games: "40" }, klejnoty)).toEqual([
      "Najwyżej 6 botów.",
    ]);
    expect(problems({ ...form, games: "0" }, klejnoty)).toEqual([`Liczba partii: od 1 do ${String(Limits.maxGames)}.`]);
    expect(problems({ ...form, games: "1001" }, klejnoty)).toHaveLength(1);
    expect(problems({ ...form, games: "1e2" }, klejnoty)).toHaveLength(1);
    expect(problems({ ...form, games: "99" }, klejnoty)).toEqual([
      "Liczba partii musi być wielokrotnością 2, żeby każdy zaczynał tyle samo razy.",
    ]);
    expect(problems({ ...form, kind: "League", bots: ["a", "b", "c"], games: "41" }, klejnoty)[0]).toContain("wielokrotnością 2");
    expect(problems({ ...form, seed: "-1" }, klejnoty)).toEqual(["Ziarno to liczba całkowita od 0 do 2^64−1."]);
    expect(problems({ ...form, seed: "18446744073709551616" }, klejnoty)).toHaveLength(1);
    expect(problems({ ...form, seed: "18446744073709551615" }, klejnoty)).toEqual([]);
    expect(problems({ ...form, kind: "Comparison", variant: "  " }, klejnoty)).toEqual([
      "Co jeśli potrzebuje wariantu zasad — to on jest zmianą.",
    ]);
    expect(problems({ ...form, kind: "Comparison", variant: "win=13" }, klejnoty)).toEqual([]);
    expect(problems({ ...defaultForm(connectFour), variant: "x=1" }, connectFour)).toEqual(["ConnectFour nie ma wariantów zasad."]);
  });

  it("follows the limits of the server", () => {
    const tight = { maxGames: 10, maxBots: 2, maxBotIterations: 30, maxWork: 100 };

    expect(botChoices(mozaika, tight).map((b) => b.spec)).toContain("ismcts:30");
    expect(problems({ ...defaultForm(mozaika), games: "20" }, mozaika, tight)).toEqual(["Liczba partii: od 1 do 10."]);
    expect(problems({ ...defaultForm(mozaika, "League") }, mozaika, tight)).toContain("Najwyżej 2 botów.");
  });

  it("weighs bots and counts work like the server", () => {
    expect(["random", "greedy:x", "mcts:50", "ISMCTS:200", "ismcts", "mcts:x"].map(weight)).toEqual([1, 1, 50, 200, 1000, 1000]);
    const form = { ...defaultForm(klejnoty), bots: ["mcts:50", "random", "greedy"], games: "30" };
    expect(work(form)).toBe(30 * 52);
    expect(work({ ...form, kind: "Comparison" })).toBe(2 * 30 * 52);
    expect(work({ ...form, kind: "League" })).toBe(30 * 2 * 52);
    expect(work({ ...form, kind: "League", bots: [] })).toBe(0);
    expect([grouped(0), grouped(999), grouped(5100), grouped(6_000_000)]).toEqual(["0", "999", "5 100", "6 000 000"]);
  });

  it("refuses an experiment too big for the lab, but only when nothing else is wrong", () => {
    const big = { ...defaultForm(klejnoty, "League"), bots: Array<string>(6).fill("ismcts:200"), games: "1000" };

    expect(problems(big, klejnoty)).toEqual([
      "Za duży eksperyment dla laboratorium: 6 000 000 jednostek pracy, najwyżej 400 000 — mniej partii, botów albo iteracji MCTS.",
    ]);
    expect(problems({ ...big, games: "x" }, klejnoty)).toEqual([`Liczba partii: od 1 do ${String(Limits.maxGames)}.`]);
  });

  it("turns the form into the order the server reads", () => {
    expect(toRequest({ ...defaultForm(mozaika, "Comparison"), variant: " column=5 ", seed: "42" })).toEqual({
      game: "mozaika",
      kind: "Comparison",
      bots: ["greedy", "greedy:kolumny"],
      games: 100,
      seed: "42",
      variant: "column=5",
    });
    expect(toRequest(defaultForm(mozaika)).variant).toBeNull();
  });

  it("accepts exactly the game counts that are a multiple of the players", () => {
    fc.assert(
      fc.property(fc.integer({ min: 1, max: Limits.maxGames }), fc.integer({ min: 2, max: 4 }), (count, players) => {
        const form = { ...defaultForm(mozaika), bots: Array<string>(players).fill("random"), games: String(count) };
        expect(problems(form, mozaika).length === 0).toBe(count % players === 0);
      }),
    );
  });
});
