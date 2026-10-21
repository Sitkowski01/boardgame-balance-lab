import { z } from "zod";

/** Kolory w kolejnosci serwera (MozaikaView): T O C G I. */
export const Letters = "TOCGI";
export const WallSize = 5;
export const TileNames = ["turkus", "ochra", "karmin", "grafit", "perła"] as const;
/** Te same kolory w bierniku, do zdan typu "bierze ochrę". */
const TileObjects = ["turkus", "ochrę", "karmin", "grafit", "perłę"] as const;

/** Nazwa koloru; numer spoza 0-4 to "?" (nie powinien przyjsc z serwera, ale napis zostaje czytelny). */
export function tileName(tile: number): string {
  return TileNames[tile] ?? "?";
}

/** Nazwa koloru jako dopelnienie ("bierze perłę"); numer spoza 0-4 to "?". */
export function tileObject(tile: number): string {
  return TileObjects[tile] ?? "?";
}

const counts = z.array(z.number().int().nonnegative()).length(5);

export const lineView = z.object({ color: z.number().int().min(0).max(4).nullable(), count: z.number().int().nonnegative() });
export type LineView = z.infer<typeof lineView>;

export const mozaikaPlayerView = z.object({
  score: z.number().int(),
  lines: z.array(lineView).length(WallSize),
  wall: z.array(z.number().int().min(0).max(24)),
  floor: counts,
  marker: z.boolean(),
});
export type MozaikaPlayerView = z.infer<typeof mozaikaPlayerView>;

/** Stol Mozaiki (MozaikaView): wszystko jawne, worek i pudelko tylko jako liczby kafelkow. */
export const mozaikaView = z.object({
  players: z.array(mozaikaPlayerView),
  factories: z.array(counts),
  center: counts,
  centerMarker: z.boolean(),
  bag: counts,
  box: counts,
  toMove: z.number().int(),
  round: z.number().int(),
  over: z.boolean(),
});
export type MozaikaView = z.infer<typeof mozaikaView>;

/** Liczby wariantu partii (GET /api/games/mozaika/rules): wzor sciany rzedami, kary podlogi i premie. */
export const mozaikaSheet = z.object({
  wall: z.array(z.array(z.number().int().min(0).max(4)).length(WallSize)).length(WallSize),
  floorPenalties: z.array(z.number().int().nonnegative()),
  rowBonus: z.number().int(),
  columnBonus: z.number().int(),
  colorBonus: z.number().int(),
  tilesPerColor: z.number().int(),
});
export type MozaikaSheet = z.infer<typeof mozaikaSheet>;

export const total = (tiles: readonly number[]): number => tiles.reduce((sum, n) => sum + n, 0);

/** Ile rzeczy lezy na podlodze: kafelki i znacznik pierwszego gracza. */
export const floorCount = (player: MozaikaPlayerView): number => total(player.floor) + (player.marker ? 1 : 0);

/** Kara, ktora podloga odbierze na koniec rundy; ponad liczbe pol nic juz nie kosztuje. */
export function floorPenalty(player: MozaikaPlayerView, sheet: MozaikaSheet): number {
  return sheet.floorPenalties.slice(0, floorCount(player)).reduce((sum, n) => sum + n, 0);
}
