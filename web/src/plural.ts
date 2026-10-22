/**
 * Polska liczba mnoga: 1 karta, 2-4 karty (ale 12-14 kart), 5 i wiecej kart, 0 kart, 22 karty.
 * Zwraca sama forme slowa, liczbe dopisuje wywolujacy.
 */
export function plural(n: number, one: string, few: string, many: string): string {
  if (n === 1) {
    return one;
  }

  const units = n % 10;
  const tens = n % 100;
  return units >= 2 && units <= 4 && (tens < 12 || tens > 14) ? few : many;
}
