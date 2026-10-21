import { z } from "zod";

/** Plansza Czworek (ConnectFourView): wiersze od gory, w polu gracz 0, 1 albo null. Ruch to numer kolumny od 0. */
export const connectFourView = z.object({
  rows: z.array(z.array(z.number().int().nullable())),
  toMove: z.number().int(),
  winner: z.number().int().nullable(),
  over: z.boolean(),
});
export type ConnectFourView = z.infer<typeof connectFourView>;

export function describeColumn(move: string): string {
  return `wrzuca do kolumny ${String(Number(move) + 1)}`;
}

const directions = [
  [0, 1],
  [1, 0],
  [1, 1],
  [1, -1],
] as const;

/** Pola zwycieskiej czworki (wiersz*kolumny+kolumna); pusto, gdy nikt nie ma czterech w linii. */
export function winningCells(rows: readonly (readonly (number | null)[])[]): Set<number> {
  const cells = new Set<number>();
  const width = rows[0]?.length ?? 0;
  // Poza plansza (takze kolumna -1 albo za ostatnia) nie ma krazka, wiec linia nie zawija sie do sasiedniego wiersza.
  const at = (r: number, c: number): number | null => rows[r]?.[c] ?? null;
  rows.forEach((row, r) => {
    row.forEach((cell, c) => {
      if (cell === null) return;
      for (const [dr, dc] of directions) {
        const line = [0, 1, 2, 3].map((k) => [r + k * dr, c + k * dc] as const);
        if (line.every(([lr, lc]) => at(lr, lc) === cell)) {
          for (const [lr, lc] of line) cells.add(lr * width + lc);
        }
      }
    });
  });
  return cells;
}

type Grid = readonly (readonly (number | null)[])[];

/**
 * Krazek ostatniego ruchu: nowy wzgledem poprzedniej pozycji krazek gracza, ktory ruszal sie ostatni (zaczyna
 * gracz 0, wiec przy n krazkach ostatni nalezy do gracza (n - 1) mod 2). Gdy miedzy widokami minely dwa ruchy
 * (ruch gracza i odpowiedz bota), obwodke dostaje odpowiedz. Na starcie, po cofnieciu albo po skoku o kilka
 * ruchow jednego gracza pusto.
 */
export function newDiscs(before: Grid | null, after: Grid): Set<number> {
  const cells = new Set<number>();
  if (before === null) return cells;
  const width = after[0]?.length ?? 0;
  const discs = after.flat().filter((cell) => cell !== null).length;
  const mover = 1 - (discs % 2);
  after.forEach((row, r) => {
    row.forEach((cell, c) => {
      if (cell === mover && (before[r]?.[c] ?? null) === null) cells.add(r * width + c);
    });
  });
  return cells.size === 1 ? cells : new Set();
}

/** Ta sama pozycja: kazde pole takie samo (widok po ponownym sprawdzeniu schematem to nowe tablice). */
export function sameRows(a: Grid, b: Grid): boolean {
  return a.length === b.length && a.every((row, r) => row.length === b[r]?.length && row.every((cell, c) => cell === b[r]?.[c]));
}
