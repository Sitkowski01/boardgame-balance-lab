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
