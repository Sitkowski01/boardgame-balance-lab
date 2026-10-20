import type { GameInfo } from "../api/schemas";
import type { ComparisonReport, Difference, ItemStats, LeagueReport, Proportion, TournamentReport } from "../lab/model";

export const p = (successes: number, trials: number, low?: number, high?: number): Proportion => {
  const rate = trials === 0 ? 0 : successes / trials;
  return { successes, trials, rate, low: low ?? Math.max(0, rate - 0.05), high: high ?? Math.min(1, rate + 0.05) };
};

export const cards = { singular: "karta", plural: "karty", strong: "za mocna", weak: "za slaba", dead: "martwa" };
export const fields = { singular: "pole sciany", plural: "pola sciany", strong: "za mocne", weak: "za slabe", dead: "martwe" };

export function item(patch: Partial<ItemStats> = {}): ItemStats {
  return { id: 0, name: "#0", group: "poziom 1", taken: p(50, 100), ownerWins: p(20, 50), verdict: "None", ...patch };
}

/** Turniej dwoch botow w gre z dwiema grupami elementow: jeden za mocny, jeden martwy, jeden za slaby. */
export function tournament(patch: Partial<TournamentReport> = {}): TournamentReport {
  return {
    game: "Klejnoty",
    games: 100,
    seed: "7",
    completed: 98,
    draws: 4,
    contestants: [
      { name: "greedy", wins: p(70, 98, 0.62, 0.79), meanScore: 0.71 },
      { name: "random", wins: p(24, 98, 0.17, 0.33), meanScore: 0.29 },
    ],
    seats: [
      { seat: 0, wins: p(55, 98, 0.46, 0.65), meanScore: 0.56 },
      { seat: 1, wins: p(39, 98, 0.31, 0.49), meanScore: 0.44 },
    ],
    length: { mean: 62.4, min: 40, median: 61, p90: 80, max: 101 },
    items: [
      item({ id: 0, name: "#0 mocna", ownerWins: p(40, 50, 0.67, 0.89), verdict: "Strong" }),
      item({ id: 1, name: "#1 martwa", taken: p(2, 100), ownerWins: p(1, 2), verdict: "Dead" }),
      item({ id: 2, name: "#2 slaba", group: "poziom 2", ownerWins: p(5, 50, 0.04, 0.21), verdict: "Weak" }),
      item({ id: 3, name: "#3 zwykla", group: "poziom 2" }),
      item({ id: 4, name: "#4 nikt", taken: p(0, 100), ownerWins: p(0, 0, 0, 1) }),
    ],
    itemGroups: [
      { group: "poziom 1", ownerWins: p(40, 100) },
      { group: "poziom 2", ownerWins: p(45, 100) },
    ],
    itemNoun: cards,
    ...patch,
  };
}

export function league(patch: Partial<LeagueReport> = {}): LeagueReport {
  return {
    game: "Mozaika",
    gamesPerPair: 40,
    seed: "3",
    ratings: [
      { name: "greedy", elo: 1720, meanScore: 0.8 },
      { name: "ismcts:50", elo: 1480, meanScore: 0.45 },
      { name: "random", elo: 1300, meanScore: 0.2 },
    ],
    pairs: [
      { first: "greedy", second: "ismcts:50", completed: 40, firstWins: p(30, 40), secondWins: p(10, 40), firstScore: 0.75 },
      { first: "greedy", second: "random", completed: 40, firstWins: p(38, 40), secondWins: p(2, 40), firstScore: 0.95 },
      { first: "ismcts:50", second: "random", completed: 0, firstWins: p(0, 0), secondWins: p(0, 0), firstScore: 0 },
    ],
    ...patch,
  };
}

export const d = (delta: number, low: number, high: number, baseline = 0.5): Difference => ({
  baseline,
  variant: baseline + delta,
  delta,
  low,
  high,
  significant: low > 0 || high < 0,
});

export function comparison(patch: Partial<ComparisonReport> = {}): ComparisonReport {
  return {
    baseline: tournament(),
    variant: tournament({ game: "Klejnoty (win=13)" }),
    contestants: [
      { name: "greedy", wins: d(0.06, 0.02, 0.1, 0.7) },
      { name: "random", wins: d(-0.01, -0.05, 0.03, 0.25) },
    ],
    seats: [
      { name: "miejsce 1", wins: d(-0.04, -0.08, -0.01) },
      { name: "miejsce 2", wins: d(0.02, -0.02, 0.06) },
    ],
    length: d(-6.5, -8, -5, 62),
    items: [
      { id: 0, name: "#0 mocna", taken: d(0.1, 0.05, 0.15), ownerWins: d(-0.12, -0.2, -0.04) },
      { id: 3, name: "#3 zwykla", taken: d(0.01, -0.03, 0.05), ownerWins: d(0.01, -0.04, 0.06) },
    ],
    ...patch,
  };
}

export const games: GameInfo[] = [
  { key: "connectfour", name: "ConnectFour", minPlayers: 2, maxPlayers: 2, hasVariants: false, variantSyntax: null, profiles: [] },
  {
    key: "klejnoty",
    name: "Klejnoty",
    minPlayers: 2,
    maxPlayers: 4,
    hasVariants: true,
    variantSyntax: "zmiany po srednikach: win=N",
    profiles: ["punkty", "silnik"],
  },
  {
    key: "mozaika",
    name: "Mozaika",
    minPlayers: 2,
    maxPlayers: 4,
    hasVariants: true,
    variantSyntax: "zmiany po srednikach: tiles=N",
    profiles: ["kolumny", "ostrozny"],
  },
];
