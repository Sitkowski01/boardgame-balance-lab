import { z } from "zod";
import { gameInfo, matchReplay, replayFrame, roomInfo, type GameInfo, type MatchReplay, type ReplayFrame, type RoomInfo } from "./schemas";

/** Odpowiedz serwera inna niz 2xx; komunikat pochodzi z ProblemDetails, gdy serwer go podal. */
export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

const problem = z.object({ detail: z.string().optional(), title: z.string().optional() });

/** Pobiera JSON i sprawdza go schematem: zle dane z sieci to blad tutaj, a nie dziwny stan w widoku. */
export async function getJson<T>(url: string, schema: z.ZodType<T>, fetcher: typeof fetch = fetch): Promise<T> {
  const response = await fetcher(url, { headers: { Accept: "application/json" } });
  if (!response.ok) {
    throw new ApiError(await describe(response), response.status);
  }

  return schema.parse(await response.json());
}

async function describe(response: Response): Promise<string> {
  const fallback = response.status === 404 ? "Nie ma takiej strony." : `Serwer odpowiedzial ${String(response.status)}.`;
  try {
    const body = problem.safeParse(await response.json());
    return body.success ? (body.data.detail ?? body.data.title ?? fallback) : fallback;
  } catch {
    return fallback;
  }
}

export const api = {
  games: (fetcher?: typeof fetch): Promise<GameInfo[]> => getJson("/api/games", z.array(gameInfo), fetcher),
  room: (id: string, fetcher?: typeof fetch): Promise<RoomInfo> => getJson(`/api/rooms/${encodeURIComponent(id)}`, roomInfo, fetcher),
  /** Tabela zasad gry dla wariantu (np. karty Klejnotow); schemat zna dopiero plansza tej gry. */
  rules: <T>(game: string, variant: string | null, schema: z.ZodType<T>, fetcher?: typeof fetch): Promise<T> =>
    getJson(
      `/api/games/${encodeURIComponent(game)}/rules${variant === null ? "" : `?variant=${encodeURIComponent(variant)}`}`,
      schema,
      fetcher,
    ),
  match: (id: string, fetcher?: typeof fetch): Promise<MatchReplay> =>
    getJson(`/api/matches/${encodeURIComponent(id)}`, matchReplay, fetcher),
  frame: (id: string, moves: number, fetcher?: typeof fetch): Promise<ReplayFrame> =>
    getJson(`/api/matches/${encodeURIComponent(id)}/frames/${String(moves)}`, replayFrame, fetcher),
};
