/**
 * Klatki powtorki: pozycja po n ruchach liczy serwer (powtorka z dziennika), klient je tylko pamieta.
 * Ta sama klatka nie jest pobierana dwa razy, a nieudane pobranie mozna powtorzyc.
 */
export interface Frames<T> {
  get: (moves: number) => Promise<T>;
  /** Pobiera w tle sasiednie klatki, zeby przewijanie o jeden ruch bylo natychmiastowe. */
  prefetch: (moves: number, last: number) => void;
}

export function frameCache<T>(load: (moves: number) => Promise<T>): Frames<T> {
  const cache = new Map<number, Promise<T>>();
  const get = (moves: number): Promise<T> => {
    const known = cache.get(moves);
    if (known !== undefined) {
      return known;
    }

    const loading = load(moves);
    cache.set(moves, loading);
    loading.catch(() => cache.delete(moves));
    return loading;
  };

  return {
    get,
    prefetch: (moves, last) => {
      for (const next of [moves + 1, moves - 1]) {
        if (next >= 0 && next <= last) {
          get(next).catch(() => undefined);
        }
      }
    },
  };
}

/** Pozycja suwaka w granicach partii: od 0 (rozdanie) do liczby ruchow. */
export function clampPosition(position: number, last: number): number {
  return Math.min(Math.max(0, Math.trunc(position)), last);
}
