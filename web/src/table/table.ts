import type { MovePlayed, RoomInfo, TableView } from "../api/schemas";

/** Ile ostatnich ruchow pokazuje stol (cala partia jest w powtorce). */
export const RecentMoves = 12;

/** Stol tak, jak zna go klient: moje miejsce z biletu (null = widz), pokoj, widok i ostatnie ruchy. */
export interface Table {
  seat: number | null;
  room: RoomInfo | null;
  view: TableView | null;
  moves: MovePlayed[];
}

export const emptyTable: Table = { seat: null, room: null, view: null, moves: [] };

export type TableEvent =
  /** Miejsce z biletu; null, gdy klient tylko oglada (np. serwer nie zna juz biletu). */
  | { type: "seated"; seat: number | null }
  | { type: "room"; room: RoomInfo }
  | { type: "view"; view: TableView }
  | { type: "move"; move: MovePlayed };

/**
 * Wiadomosci moga przyjsc w innej kolejnosci, niz serwer je wyslal (np. stan z Rejoin i zmiana z grupy),
 * wiec kazda niesie wersje pokoju: starsza od tej, ktora juz mamy, jest pomijana.
 */
export function reduceTable(table: Table, event: TableEvent): Table {
  switch (event.type) {
    case "seated":
      return { ...table, seat: event.seat };
    case "room":
      return table.room !== null && event.room.version < table.room.version ? table : { ...table, room: event.room };
    case "view":
      return table.view !== null && event.view.version < table.view.version ? table : { ...table, view: event.view };
    case "move":
      return table.moves.some((m) => m.number >= event.move.number)
        ? table
        : { ...table, moves: [...table.moves, event.move].slice(-RecentMoves) };
  }
}

/** Kim jestem przy stole: moje miejsce (null = widz), czy jestem gospodarzem i czy teraz moj ruch. */
export function myRole(table: Table): { seat: number | null; host: boolean; myTurn: boolean } {
  const seat = table.seat;
  return {
    seat,
    host: seat !== null && table.room?.host === seat,
    // Widz (null) nie gra nawet wtedy, gdy pokoj nie ma nikogo na ruchu (toMove tez null).
    myTurn: seat !== null && table.room?.status === "Playing" && table.room.toMove === seat && (table.view?.legalMoves.length ?? 0) > 0,
  };
}

/** Pelne sekundy do terminu ruchu (zaokraglone w gore, nigdy ponizej zera); null, gdy terminu nie ma. */
export function secondsLeft(deadline: string | null, now: number): number | null {
  if (deadline === null) {
    return null;
  }

  return Math.max(0, Math.ceil((Date.parse(deadline) - now) / 1000));
}

/** Zwyciezcy po koncu partii: miejsca z najwyzszym wynikiem (remis = kilka miejsc). */
export function winners(scores: readonly number[]): number[] {
  const best = Math.max(...scores);
  return scores.flatMap((score, seat) => (score === best ? [seat] : []));
}
