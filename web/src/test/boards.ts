import type { ConnectFourView } from "../connectfour/model";
import type { KlejnotySheet, KlejnotyView, PlayerView } from "../klejnoty/model";

export function connectFour(patch: Partial<ConnectFourView> = {}): ConnectFourView {
  return { rows: Array.from({ length: 6 }, () => Array.from({ length: 7 }, () => null)), toMove: 0, winner: null, over: false, ...patch };
}

/** Karty jak w grze: 40/30/20 na poziomach, kolor rabatu po kolei, koszt w kolorze nastepnym. */
export function sheet(): KlejnotySheet {
  const tierOf = (id: number): number => (id < 40 ? 1 : id < 70 ? 2 : 3);
  return {
    cards: Array.from({ length: 90 }, (_, id) => {
      const cost = [0, 0, 0, 0, 0];
      cost[(id + 1) % 5] = tierOf(id) + 1;
      return { id, tier: tierOf(id), bonus: id % 5, points: tierOf(id) - 1, cost };
    }),
    patrons: Array.from({ length: 10 }, (_, id) => ({ id, requirement: [3, 3, 3, 0, 0], points: 3 })),
    winningPoints: 15,
  };
}

export function player(patch: Partial<PlayerView> = {}): PlayerView {
  return { tokens: [0, 0, 0, 0, 0, 0], bonuses: [0, 0, 0, 0, 0], points: 0, bought: [], reserved: [], patrons: [], ...patch };
}

export function klejnoty(patch: Partial<KlejnotyView> = {}): KlejnotyView {
  return {
    players: [player(), player()],
    bank: [4, 4, 4, 4, 4, 5],
    market: [0, 1, 2, 3, 40, 41, 42, 43, 70, 71, 72, 73],
    deckSizes: [36, 26, 16],
    patrons: [0, 1, 2],
    toMove: 0,
    finalRound: false,
    over: false,
    ...patch,
  };
}

/** Legalne ruchy jak z serwera dla pelnego banku: kazda trojka kolorow, dwa takie same i rezerwacje. */
export function openingMoves(): string[] {
  const letters = "WUGRK";
  const takes: string[] = [];
  for (let a = 0; a < 5; a++)
    for (let b = a + 1; b < 5; b++)
      for (let c = b + 1; c < 5; c++) takes.push(`take:${letters[a] ?? ""}${letters[b] ?? ""}${letters[c] ?? ""}`);
  return [...takes, ...Array.from(letters, (l) => l).map((l) => `take2:${l}${l}`), "reserve:0", "reserve:40", "deck:1", "deck:3"];
}
