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
  const at = (r: number, c: number): number | null => rows[r]?.[c] ?? null;
  rows.forEach((row, r) => {
    row.forEach((cell, c) => {
      if (cell === null) return;
      for (const [dr, dc] of directions) {
        const line = [0, 1, 2, 3].map((k) => [r + k * dr, c + k * dc] as const);
        if (line.every(([lr, lc]) => lc >= 0 && lc < width && at(lr, lc) === cell)) {
          for (const [lr, lc] of line) cells.add(lr * width + lc);
        }
      }
    });
  });
  return cells;
}
