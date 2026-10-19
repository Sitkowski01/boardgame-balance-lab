import type { ConnectFourView } from "../connectfour/model";
import type { KlejnotySheet, KlejnotyView, PlayerView } from "../klejnoty/model";
import type { MozaikaPlayerView, MozaikaSheet, MozaikaView } from "../mozaika/model";

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

/** Plansza Mozaiki gracza: puste rzedy, pusta sciana i podloga. */
export function mozaikaPlayer(patch: Partial<MozaikaPlayerView> = {}): MozaikaPlayerView {
  return {
    score: 0,
    lines: Array.from({ length: 5 }, () => ({ color: null, count: 0 })),
    wall: [],
    floor: [0, 0, 0, 0, 0],
    marker: false,
    ...patch,
  };
}

/** Stol Mozaiki dla dwoch graczy: 5 warsztatow po 4 kafelki, pusty srodek ze znacznikiem. */
export function mozaika(patch: Partial<MozaikaView> = {}): MozaikaView {
  return {
    players: [mozaikaPlayer(), mozaikaPlayer()],
    factories: [
      [2, 1, 0, 0, 1],
      [0, 0, 4, 0, 0],
      [1, 1, 1, 1, 0],
      [0, 2, 0, 2, 0],
      [0, 0, 0, 1, 3],
    ],
    center: [0, 0, 0, 0, 0],
    centerMarker: true,
    bag: [16, 16, 15, 16, 17],
    box: [0, 0, 0, 0, 0],
    toMove: 0,
    round: 1,
    over: false,
    ...patch,
  };
}

/** Zasady standardowe: wzor sciany przesuniety o pole w kazdym rzedzie, podloga 1 1 2 2 2 3 3. */
export function mozaikaRules(): MozaikaSheet {
  return {
    wall: Array.from({ length: 5 }, (_, row) => Array.from({ length: 5 }, (_, column) => (column - row + 5) % 5)),
    floorPenalties: [1, 1, 2, 2, 2, 3, 3],
    rowBonus: 2,
    columnBonus: 7,
    colorBonus: 10,
    tilesPerColor: 20,
  };
}

/** Legalne ruchy jak z serwera dla pustej planszy: kazdy kolor z kazdego zrodla na kazdy rzad i podloge. */
export function mozaikaMoves(view: MozaikaView = mozaika()): string[] {
  const lines = ["0", "1", "2", "3", "4", "floor"];
  const sources: [string, number[]][] = [...view.factories.map((f, i): [string, number[]] => [`f${String(i)}`, f]), ["c", view.center]];
  return sources.flatMap(([source, tiles]) =>
    tiles.flatMap((count, color) => (count === 0 ? [] : lines.map((line) => `${source}:${"TOCGI"[color] ?? "?"}:${line}`))),
  );
}
