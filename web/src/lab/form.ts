import { z } from "zod";
import type { GameInfo } from "../api/schemas";
import type { ExperimentKind, ExperimentRequest } from "./model";

/** Formularz eksperymentu przed wyslaniem; liczby jako tekst, tak jak w polach. */
export interface ExperimentForm {
  game: string;
  kind: ExperimentKind;
  bots: string[];
  games: string;
  seed: string;
  variant: string;
}

export const KindNames: Record<ExperimentKind, string> = { Tournament: "Turniej", League: "Liga", Comparison: "Co jeśli" };

export const KindHelp: Record<ExperimentKind, string> = {
  Tournament: "Boty grają przy jednym stole: przewaga miejsca, siła botów i elementy gry (karty, pola).",
  League: "Każdy bot z każdym w partiach dwuosobowych: ranking Elo i macierz „kto bije kogo”.",
  Comparison: "Ten sam turniej na oryginale i na wariancie zasad, z tych samych rozdań: co naprawdę zmienia poprawka.",
};

/** Limity serwera (GET /api/experiments/limits) — formularz mowi o nich, zanim serwer odmowi. */
export const labLimits = z.object({
  maxGames: z.number().int(),
  maxBots: z.number().int(),
  maxBotIterations: z.number().int(),
  maxWork: z.number(),
});
export type LabLimits = z.infer<typeof labLimits>;

/** Domyslne limity serwera (ExperimentOptions), zanim przyjda prawdziwe. */
export const DefaultLimits: LabLimits = { maxGames: 1000, maxBots: 6, maxBotIterations: 200, maxWork: 400_000 };

/** Liczba z odstepem co trzy cyfry ("6 000 000"), tak samo w kazdej przegladarce. */
export const grouped = (n: number): string => String(Math.round(n)).replace(/\B(?=(\d{3})+(?!\d))/g, " ");

/** Waga bota w budzecie pracy, jak na serwerze: proste boty 1, przeszukiwanie — liczba iteracji (bez liczby 1000). */
export function weight(bot: string): number {
  const [kind, iterations] = bot.split(":", 2);
  if (kind?.toLowerCase() !== "mcts" && kind?.toLowerCase() !== "ismcts") return 1;
  return iterations !== undefined && /^\d+$/.test(iterations) ? Number(iterations) : 1000;
}

/** Praca eksperymentu: partie razy wagi botow, ktore w nich graja (liga: kazdy z kazdym z pozostalych). */
export function work(form: ExperimentForm): number {
  const games = Number(form.games);
  const weights = form.bots.reduce((sum, bot) => sum + weight(bot), 0);
  switch (form.kind) {
    case "League":
      return games * Math.max(0, form.bots.length - 1) * weights;
    case "Comparison":
      return 2 * games * weights;
    case "Tournament":
      return games * weights;
  }
}

/** Boty do wyboru: proste, profile greedy tej gry i przeszukiwanie z budzetem w limicie serwera. */
export function botChoices(game: GameInfo, limits: LabLimits = DefaultLimits): { spec: string; label: string }[] {
  const greedy =
    game.profiles.length > 0
      ? [{ spec: "greedy", label: "Zachłanny" }, ...game.profiles.map((p) => ({ spec: `greedy:${p}`, label: `Zachłanny: ${p}` }))]
      : [];
  return [
    ...greedy,
    { spec: "mcts:50", label: "MCTS 50" },
    { spec: `mcts:${String(limits.maxBotIterations)}`, label: `MCTS ${String(limits.maxBotIterations)}` },
    { spec: "ismcts:50", label: "ISMCTS 50" },
    { spec: `ismcts:${String(limits.maxBotIterations)}`, label: `ISMCTS ${String(limits.maxBotIterations)}` },
    { spec: "random", label: "Losowy" },
  ];
}

/** Nazwa bota dla czlowieka; nieznany opis zostaje jak jest. */
export const botLabel = (game: GameInfo, spec: string): string => botChoices(game).find((b) => b.spec === spec)?.label ?? spec;

/** Sensowny start: dwa rozne boty, ktore gra rozumie, i liczba partii, ktora szybko sie liczy. */
export function defaultForm(game: GameInfo, kind: ExperimentKind = "Tournament"): ExperimentForm {
  const [first, second] = game.profiles.length > 0 ? ["greedy", `greedy:${game.profiles[0] ?? ""}`] : ["mcts:50", "random"];
  const bots = kind === "League" ? [first, second, "random"] : [first, second];
  return { game: game.key, kind, bots: [...new Set(bots)], games: kind === "League" ? "40" : "100", seed: "1", variant: "" };
}

/**
 * Co jest nie tak z formularzem (pusta lista = mozna wyslac). Te same zasady sprawdza serwer; tu tylko po to,
 * zeby powiedziec o nich od razu, przy polu, a nie po wyslaniu.
 */
export function problems(form: ExperimentForm, game: GameInfo, limits: LabLimits = DefaultLimits): string[] {
  const found: string[] = [];
  const games = Number(form.games);
  const players = form.kind === "League" ? 2 : form.bots.length;
  if (form.kind === "League" ? form.bots.length < 2 : form.bots.length < game.minPlayers || form.bots.length > game.maxPlayers) {
    found.push(
      form.kind === "League"
        ? "Liga potrzebuje co najmniej dwóch botów."
        : `${game.name} to gra dla ${String(game.minPlayers)}–${String(game.maxPlayers)} graczy — tylu botów potrzeba przy stole.`,
    );
  }

  if (form.bots.length > limits.maxBots) found.push(`Najwyżej ${String(limits.maxBots)} botów.`);
  if (!/^\d+$/.test(form.games) || games < 1 || games > limits.maxGames) {
    found.push(`Liczba partii: od 1 do ${String(limits.maxGames)}.`);
  } else if (players > 0 && games % players !== 0) {
    found.push(`Liczba partii musi być wielokrotnością ${String(players)}, żeby każdy zaczynał tyle samo razy.`);
  }

  if (!/^\d{1,20}$/.test(form.seed) || BigInt(form.seed) > 18446744073709551615n) found.push("Ziarno to liczba całkowita od 0 do 2^64−1.");
  if (form.kind === "Comparison" && form.variant.trim() === "") found.push("Co jeśli potrzebuje wariantu zasad — to on jest zmianą.");
  if (form.variant.trim() !== "" && !game.hasVariants) found.push(`${game.name} nie ma wariantów zasad.`);
  if (found.length === 0 && work(form) > limits.maxWork) {
    found.push(
      `Za duży eksperyment dla laboratorium: ${grouped(work(form))} jednostek pracy, najwyżej ${grouped(limits.maxWork)} — mniej partii, botów albo iteracji MCTS.`,
    );
  }
  return found;
}

export function toRequest(form: ExperimentForm): ExperimentRequest {
  return {
    game: form.game,
    kind: form.kind,
    bots: form.bots,
    games: Number(form.games),
    seed: form.seed,
    variant: form.variant.trim() === "" ? null : form.variant.trim(),
  };
}
