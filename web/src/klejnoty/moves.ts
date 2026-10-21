import { gemName, Letters, type CardSheet } from "./model";

/**
 * Ruchy Klejnotow w zapisie serwera (KlejnotyGame.Codec): take:WUG, take2:RR, return:Z, reserve:17,
 * buy:17, buy-reserved:17, deck:2, pass. Klient nie zna zasad: sklada ruch z klikniec i wysyla go tylko,
 * gdy jest na liscie legalnych ruchow od serwera.
 */
export type KlejnotyMove =
  | { kind: "take"; gems: number[] }
  | { kind: "take2"; gem: number }
  | { kind: "return"; gem: number }
  | { kind: "reserve"; card: number }
  | { kind: "buy"; card: number }
  | { kind: "buy-reserved"; card: number }
  | { kind: "deck"; tier: number }
  | { kind: "pass" };

const gemOf = (letter: string): number => {
  const gem = Letters.indexOf(letter);
  if (gem < 0 || letter.length !== 1) {
    throw new Error(`Nieznany kolor '${letter}'.`);
  }

  return gem;
};

export function parseMove(text: string): KlejnotyMove {
  const [name, argument] = text.split(":", 2);
  if (name === "pass") {
    return { kind: "pass" };
  }

  if (argument === undefined) {
    throw new Error(`Ruch '${text}' bez argumentu.`);
  }

  const number = Number(argument);
  switch (name) {
    case "take":
      return { kind: "take", gems: Array.from(argument, gemOf) };
    case "take2":
      return { kind: "take2", gem: gemOf(argument.slice(0, 1)) };
    case "return":
      return { kind: "return", gem: gemOf(argument) };
    case "reserve":
    case "buy":
    case "buy-reserved":
      return { kind: name, card: number };
    case "deck":
      return { kind: "deck", tier: number };
    default:
      throw new Error(`Nieznany ruch '${text}'.`);
  }
}

/** Litery kolorow zawsze w kolejnosci serwera (W U G R K Z), niezaleznie od kolejnosci klikniec. */
export function letters(gems: readonly number[]): string {
  return [...gems]
    .sort((a, b) => a - b)
    .map((gem) => Letters[gem])
    .join("");
}

/**
 * Wybrane w banku zetony. Klik w nowy kolor doklada jeden zeton, klik w wybrany kolor go zdejmuje, a drugi
 * klik w jedyny wybrany kolor to para (take2) — jesli wolno ja wziac; inaczej tez zdejmuje. Po parze nowy
 * kolor zaczyna wybor od nowa.
 */
export function toggleGem(selection: readonly number[], gem: number, legal: readonly string[]): number[] {
  if (selection.length === 1 && selection[0] === gem) {
    return legal.includes(`take2:${letters([gem, gem])}`) ? [gem, gem] : [];
  }

  if (selection.includes(gem)) {
    return selection.filter((g) => g !== gem);
  }

  return isPair(selection) ? [gem] : [...selection, gem];
}

const isPair = (selection: readonly number[]): boolean => selection.length === 2 && selection[0] === selection[1];

/** Ruch z wybranych zetonow, jesli jest legalny; inaczej null (np. wybor jeszcze niepelny). */
export function takeMove(selection: readonly number[], legal: readonly string[]): string | null {
  const move = `${isPair(selection) ? "take2" : "take"}:${letters(selection)}`;
  return legal.includes(move) ? move : null;
}

/** Czy klik w ten kolor ma sens: zdejmuje wybor albo prowadzi do legalnego ruchu. */
export function canPick(selection: readonly number[], gem: number, legal: readonly string[]): boolean {
  if (selection.includes(gem)) {
    return true;
  }

  // Nowy kolor: albo poczatek pary (take2), albo kolejny kolor trojki, w ktorej sa wszystkie wybrane.
  const next = toggleGem(selection, gem, legal);
  return legal.some((move) => {
    const parsed = parseMove(move);
    return (
      (parsed.kind === "take2" && next.length === 1 && parsed.gem === gem) ||
      (parsed.kind === "take" && next.every((g) => parsed.gems.includes(g)))
    );
  });
}

/** Co mozna zrobic z karta: kupic (z rynku albo z rezerwy) i zarezerwowac. */
export function cardMoves(card: number, legal: readonly string[]): { buy: string | null; reserve: string | null } {
  const find = (...moves: string[]): string | null => moves.find((m) => legal.includes(m)) ?? null;
  return { buy: find(`buy:${String(card)}`, `buy-reserved:${String(card)}`), reserve: find(`reserve:${String(card)}`) };
}

const gemList = (gems: readonly number[]): string => gems.map(gemName).join(", ");

/** Ruch po ludzku, do listy ostatnich ruchow i powtorki. */
export function describeMove(text: string, cards: readonly CardSheet[] = []): string {
  const move = parseMove(text);
  const card = (id: number): string => {
    const sheet = cards[id];
    return sheet === undefined ? `kartę #${String(id)}` : `kartę (${gemName(sheet.bonus)}, ${String(sheet.points)} pkt)`;
  };
  switch (move.kind) {
    case "take":
      return `bierze ${gemList(move.gems)}`;
    case "take2":
      return `bierze dwa: ${gemList([move.gem])}`;
    case "return":
      return `oddaje ${gemList([move.gem])}`;
    case "reserve":
      return `rezerwuje ${card(move.card)}`;
    case "buy":
      return `kupuje ${card(move.card)}`;
    case "buy-reserved":
      return `kupuje z rezerwy ${card(move.card)}`;
    case "deck":
      return `rezerwuje w ciemno z talii ${String(move.tier)}`;
    case "pass":
      return "pasuje";
  }
}
