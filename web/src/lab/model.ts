import { z } from "zod";

// Raporty laboratorium (BalanceLab.Simulation przez /api/experiments): schemat tylko tego, co rysuje dashboard.

/** Odsetek z 95% przedzialem Wilsona. */
export const proportion = z.object({
  successes: z.number().int(),
  trials: z.number().int(),
  rate: z.number(),
  low: z.number(),
  high: z.number(),
});
export type Proportion = z.infer<typeof proportion>;

export const itemVerdict = z.enum(["None", "Strong", "Weak", "Dead"]);
export type ItemVerdict = z.infer<typeof itemVerdict>;

export const itemNoun = z.object({ singular: z.string(), plural: z.string(), strong: z.string(), weak: z.string(), dead: z.string() });
export type ItemNoun = z.infer<typeof itemNoun>;

export const itemStats = z.object({
  id: z.number().int(),
  name: z.string(),
  group: z.string(),
  taken: proportion,
  ownerWins: proportion,
  verdict: itemVerdict,
});
export type ItemStats = z.infer<typeof itemStats>;

export const tournamentReport = z.object({
  game: z.string(),
  games: z.number().int(),
  seed: z.string(),
  completed: z.number().int(),
  draws: z.number().int(),
  contestants: z.array(z.object({ name: z.string(), wins: proportion, meanScore: z.number() })),
  seats: z.array(z.object({ seat: z.number().int(), wins: proportion, meanScore: z.number() })),
  length: z.object({ mean: z.number(), min: z.number(), median: z.number(), p90: z.number(), max: z.number() }).nullable(),
  items: z.array(itemStats),
  itemGroups: z.array(z.object({ group: z.string(), ownerWins: proportion })),
  itemNoun: itemNoun,
});
export type TournamentReport = z.infer<typeof tournamentReport>;

export const leagueReport = z.object({
  game: z.string(),
  gamesPerPair: z.number().int(),
  seed: z.string(),
  ratings: z.array(z.object({ name: z.string(), elo: z.number(), meanScore: z.number() })),
  pairs: z.array(
    z.object({
      first: z.string(),
      second: z.string(),
      completed: z.number().int(),
      firstWins: proportion,
      secondWins: proportion,
      firstScore: z.number(),
    }),
  ),
});
export type LeagueReport = z.infer<typeof leagueReport>;

/** Roznica wariant minus oryginal z 95% przedzialem; istotna, gdy przedzial nie obejmuje zera. */
export const difference = z.object({
  baseline: z.number(),
  variant: z.number(),
  delta: z.number(),
  low: z.number(),
  high: z.number(),
  significant: z.boolean(),
});
export type Difference = z.infer<typeof difference>;

export const comparisonReport = z.object({
  baseline: tournamentReport,
  variant: tournamentReport,
  contestants: z.array(z.object({ name: z.string(), wins: difference })),
  seats: z.array(z.object({ name: z.string(), wins: difference })),
  length: difference.nullable(),
  items: z.array(z.object({ id: z.number().int(), name: z.string(), taken: difference, ownerWins: difference })),
});
export type ComparisonReport = z.infer<typeof comparisonReport>;

export const experimentKind = z.enum(["Tournament", "League", "Comparison"]);
export type ExperimentKind = z.infer<typeof experimentKind>;

export const experimentStatus = z.enum(["Queued", "Running", "Done", "Failed", "Cancelled"]);
export type ExperimentStatus = z.infer<typeof experimentStatus>;

export const experimentRequest = z.object({
  game: z.string(),
  kind: experimentKind,
  bots: z.array(z.string()),
  games: z.number().int(),
  seed: z.string(),
  variant: z.string().nullable(),
});
export type ExperimentRequest = z.infer<typeof experimentRequest>;

/** Eksperyment z serwera; wynik jeszcze niesprawdzony — jego schemat zalezy od rodzaju. */
export const experimentInfo = z.object({
  id: z.string(),
  request: experimentRequest,
  status: experimentStatus,
  done: z.number().int(),
  total: z.number().int(),
  error: z.string().nullable(),
  created: z.string(),
  finished: z.string().nullable(),
  result: z.unknown(),
});
export type ExperimentInfo = z.infer<typeof experimentInfo>;

export type ExperimentResult =
  | { kind: "Tournament"; report: TournamentReport }
  | { kind: "League"; report: LeagueReport }
  | { kind: "Comparison"; report: ComparisonReport };

/** Wynik skonczonego eksperymentu sprawdzony schematem jego rodzaju; null, gdy go nie ma albo jest nieczytelny. */
export function resultOf(info: ExperimentInfo): ExperimentResult | null {
  if (info.status !== "Done") return null;
  switch (info.request.kind) {
    case "Tournament": {
      const parsed = tournamentReport.safeParse(info.result);
      return parsed.success ? { kind: "Tournament", report: parsed.data } : null;
    }
    case "League": {
      const parsed = leagueReport.safeParse(info.result);
      return parsed.success ? { kind: "League", report: parsed.data } : null;
    }
    case "Comparison": {
      const parsed = comparisonReport.safeParse(info.result);
      return parsed.success ? { kind: "Comparison", report: parsed.data } : null;
    }
  }
}

export const finished = (status: ExperimentStatus): boolean => status !== "Queued" && status !== "Running";

/** Kiedy to bylo, po ludzku: "przed chwilą", "12 min temu", "3 godz. temu", a starsze datą "21.10". */
export function timeAgo(iso: string, now: number = Date.now()): string {
  const minutes = Math.floor((now - Date.parse(iso)) / 60_000);
  if (Number.isNaN(minutes)) return "";
  if (minutes < 1) return "przed chwilą";
  if (minutes < 60) return `${String(minutes)} min temu`;
  if (minutes < 24 * 60) return `${String(Math.floor(minutes / 60))} godz. temu`;
  const date = new Date(iso);
  return `${String(date.getDate()).padStart(2, "0")}.${String(date.getMonth() + 1).padStart(2, "0")}`;
}

/** Liczba z przecinkiem dziesietnym, jak pisze sie po polsku ("52,3"); raport HTML z CLI zostaje przy kropce. */
export const decimal = (value: number, digits: number): string => value.toFixed(digits).replace(".", ",");

/** Procent z jednym miejscem po przecinku ("52,3%"). */
export const percent = (rate: number): string => `${decimal(rate * 100, 1)}%`;

/** Punkty procentowe ze znakiem ("+4,1 pp"). */
export const points = (delta: number): string => `${delta > 0 ? "+" : delta < 0 ? "−" : "±"}${decimal(Math.abs(delta * 100), 1)} pp`;

/** Komorka macierzy ligi: sredni wynik wiersza przeciw kolumnie (0-1), null na przekatnej i bez partii. */
export function pairScore(report: LeagueReport, row: string, column: string): number | null {
  for (const pair of report.pairs) {
    if (pair.completed === 0) continue;
    if (pair.first === row && pair.second === column) return pair.firstScore;
    if (pair.first === column && pair.second === row) return 1 - pair.firstScore;
  }

  return null;
}

/** Element wobec swojej grupy: o ile czesciej wygrywa wlasciciel (w punktach odsetka). */
export function edge(report: TournamentReport, item: ItemStats): number {
  const group = report.itemGroups.find((g) => g.group === item.group);
  return group === undefined ? 0 : item.ownerWins.rate - group.ownerWins.rate;
}

/** Ocena slowem w rodzaju nazwy elementu ("martwa" karta, "martwe" pole). */
export function verdictWord(verdict: ItemVerdict, noun: ItemNoun): string {
  switch (verdict) {
    case "Strong":
      return noun.strong;
    case "Weak":
      return noun.weak;
    case "Dead":
      return noun.dead;
    case "None":
      return "";
  }
}

/** Znak oceny: zeby nie niosl jej sam kolor (czytelne bez rozrozniania barw i na wydruku). */
export const verdictMark = (verdict: ItemVerdict): string =>
  verdict === "Strong" ? "▲" : verdict === "Weak" ? "▼" : verdict === "Dead" ? "○" : "";
