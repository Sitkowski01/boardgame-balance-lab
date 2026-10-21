import { Letters, tileObject } from "./model";

/** Zrodlo kafelkow: numer warsztatu albo srodek stolu. */
export type Source = number | "center";

/** Rzad wzoru 0-4 albo podloga. */
export type Line = number | "floor";

/**
 * Ruch Mozaiki w zapisie serwera (MozaikaGame.Codec): ZRODLO:KOLOR:RZAD, np. f2:C:3 albo c:T:floor.
 * Klient nie zna zasad: sklada ruch z dwoch klikniec (kafelki w zrodle, potem rzad) i wysyla go tylko,
 * gdy jest na liscie legalnych ruchow od serwera.
 */
export interface MozaikaMove {
  source: Source;
  color: number;
  line: Line;
}

export function parseMove(text: string): MozaikaMove {
  const [source, letter, line, ...rest] = text.split(":");
  const color = Letters.indexOf(letter ?? "");
  if (source === undefined || line === undefined || rest.length > 0 || letter?.length !== 1 || color < 0) {
    throw new Error(`Ruch '${text}' nie ma postaci ZRODLO:KOLOR:RZAD.`);
  }

  if (source !== "c" && !/^f\d+$/.test(source)) {
    throw new Error(`Ruch '${text}': zrodlo to fN albo c.`);
  }

  if (line !== "floor" && !/^\d$/.test(line)) {
    throw new Error(`Ruch '${text}': rzad to 0-4 albo floor.`);
  }

  return { source: source === "c" ? "center" : Number(source.slice(1)), color, line: line === "floor" ? "floor" : Number(line) };
}

export function formatMove({ source, color, line }: MozaikaMove): string {
  return `${source === "center" ? "c" : `f${String(source)}`}:${Letters[color] ?? "?"}:${String(line)}`;
}

/** Czy z tego zrodla mozna wziac ten kolor (na jakikolwiek rzad albo podloge). */
export function canTake(source: Source, color: number, legal: readonly string[]): boolean {
  return legal.some((move) => {
    const parsed = parseMove(move);
    return parsed.source === source && parsed.color === color;
  });
}

/** Ruchy dla wybranych kafelkow, po rzedzie docelowym. */
export function placements(source: Source, color: number, legal: readonly string[]): Map<Line, string> {
  const result = new Map<Line, string>();
  for (const move of legal) {
    const parsed = parseMove(move);
    if (parsed.source === source && parsed.color === color) {
      result.set(parsed.line, move);
    }
  }

  return result;
}

const sourceName = (source: Source): string => (source === "center" ? "ze środka" : `z warsztatu ${String(source + 1)}`);

/** Ruch po ludzku, do listy ostatnich ruchow i powtorki; rzedy liczone od 1, jak na planszy. */
export function describeMove(text: string): string {
  const move = parseMove(text);
  const target = move.line === "floor" ? "na podłogę" : `na rząd ${String(move.line + 1)}`;
  return `bierze ${tileObject(move.color)} ${sourceName(move.source)} ${target}`;
}
