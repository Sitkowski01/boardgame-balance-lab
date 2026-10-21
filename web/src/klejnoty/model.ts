import { z } from "zod";

/** Kolory w kolejnosci serwera (KlejnotyView): W U G R K, potem zloto Z (tylko zetony). */
export const Letters = "WUGRKZ";
export const Gold = 5;
export const Colors = 5;
export const GemNames = ["biały", "niebieski", "zielony", "czerwony", "czarny", "złoto"] as const;

/** Nazwa koloru; numer spoza 0-5 to "?" (nie powinien przyjsc z serwera, ale napis zostaje czytelny). */
export function gemName(gem: number): string {
  return GemNames[gem] ?? "?";
}

const counts = (length: number) => z.array(z.number().int().nonnegative()).length(length);

export const reservedView = z.object({ card: z.number().int().nullable(), tier: z.number().int(), hidden: z.boolean() });
export type ReservedView = z.infer<typeof reservedView>;

export const playerView = z.object({
  tokens: counts(6),
  bonuses: counts(5),
  points: z.number().int(),
  bought: z.array(z.number().int()),
  reserved: z.array(reservedView),
  patrons: z.array(z.number().int()),
});
export type PlayerView = z.infer<typeof playerView>;

/** Plansza Klejnotow z punktu widzenia odbiorcy (KlejnotyView); cudze karty wziete w ciemno sa bez numeru. */
export const klejnotyView = z.object({
  players: z.array(playerView),
  bank: counts(6),
  market: z.array(z.number().int().nullable()).length(12),
  deckSizes: counts(3),
  patrons: z.array(z.number().int()),
  toMove: z.number().int(),
  finalRound: z.boolean(),
  over: z.boolean(),
});
export type KlejnotyView = z.infer<typeof klejnotyView>;

export const cardSheet = z.object({
  id: z.number().int(),
  tier: z.number().int(),
  bonus: z.number().int(),
  points: z.number().int(),
  cost: counts(5),
});
export type CardSheet = z.infer<typeof cardSheet>;

export const patronSheet = z.object({ id: z.number().int(), requirement: counts(5), points: z.number().int() });
export type PatronSheet = z.infer<typeof patronSheet>;

/** Karty i patroni wariantu partii (GET /api/games/klejnoty/rules). */
export const klejnotySheet = z.object({ cards: z.array(cardSheet), patrons: z.array(patronSheet), winningPoints: z.number().int() });
export type KlejnotySheet = z.infer<typeof klejnotySheet>;

/** Rynek: 12 slotow, po 4 na poziom, od poziomu 1. */
export function marketRows(market: readonly (number | null)[]): (number | null)[][] {
  return [market.slice(0, 4), market.slice(4, 8), market.slice(8, 12)];
}

export function totalTokens(player: PlayerView): number {
  return player.tokens.reduce((sum, n) => sum + n, 0);
}
