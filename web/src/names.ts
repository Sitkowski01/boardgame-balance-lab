import type { GameInfo } from "./api/schemas";
import { plural } from "./plural";

/**
 * Nazwy dla gracza. Serwer nazywa gry i boty po swojemu (ConnectFour, greedy:kolory, ismcts:300) — te same
 * nazwy sa w dziennikach i raportach, wiec zostaja; strona pokazuje je po polsku.
 */
const titles: Record<string, string> = { ConnectFour: "Czwórki" };

/** Nazwa gry na stronie; wariant zasad dopisuje serwer do nazwy, wiec gry z wariantami zostaja bez zmian. */
export function gameTitle(name: string): string {
  return titles[name] ?? name;
}

/** Imie miejsca przy stole: bot po polsku, czlowiek tak, jak sie wpisal; puste miejsce bez imienia. */
export function seatName(seat: { kind: "Human" | "Bot" | "Empty"; name: string | null }): string | null {
  return seat.kind === "Bot" ? playerName(seat.name ?? "") : seat.name;
}

/** Krotki opis gry na karcie w lobby. */
export const gameBlurbs: Record<string, string> = {
  klejnoty: "Zbieraj żetony, kupuj karty kopalń i przyciągaj patronów. Kto pierwszy do 15 punktów — w stylu Splendora.",
  mozaika: "Bierz kafelki z warsztatów i układaj ścianę tak, żeby nic nie spadło na podłogę — w stylu Azula.",
  connectfour: "Cztery krążki w linii: krótka partia na rozgrzewkę z botem MCTS, który nie wybacza.",
};

const botKinds: Record<string, string> = { greedy: "Zachłanny", random: "Losowy", mcts: "MCTS", ismcts: "ISMCTS" };
const botName = /^(greedy|random|mcts|ismcts)(?::(.+))?$/u;

/** Bot po polsku: greedy:kolory -> "Zachłanny: kolory", mcts:500 -> "MCTS 500"; inne imiona bez zmian. */
export function playerName(name: string): string {
  const match = botName.exec(name);
  if (match === null) {
    return name;
  }

  const kind = botKinds[match[1] ?? ""] ?? name;
  const detail = match[2];
  if (detail === undefined) {
    return kind;
  }

  return /^\d+$/u.test(detail) ? `${kind} ${detail}` : `${kind}: ${detail}`;
}

/** "3 gry, od 2 do 4 graczy" z katalogu serwera. */
export function gamesFact(games: readonly GameInfo[]): string {
  const n = games.length;
  const noun = plural(n, "gra", "gry", "gier");
  const min = Math.min(...games.map((g) => g.minPlayers));
  const max = Math.max(...games.map((g) => g.maxPlayers));
  return `${String(n)} ${noun}, ${min === max ? `${String(min)} graczy` : `od ${String(min)} do ${String(max)} graczy`}`;
}
