import type { z } from "zod";
import type { MovePlayed, RoomInfo, SeatTicket, TableView } from "./schemas";

/** Nowy pokoj: jak CreateRoomArgs na serwerze. */
export interface CreateRoomArgs {
  game: string;
  seats: number;
  variant: string | null;
  name: string;
}

/** Co serwer wysyla (IGameClient), juz sprawdzone schematem, i co dzieje sie z polaczeniem. */
export interface GameEvents {
  room: (room: RoomInfo) => void;
  view: (view: TableView) => void;
  move: (move: MovePlayed) => void;
  lobby: (room: RoomInfo) => void;
  /** Polaczenie zerwane, klient probuje wrocic. */
  reconnecting: () => void;
  /** Nowe polaczenie po zerwaniu: serwer go nie zna, wiec trzeba znow usiasc (Rejoin) albo ogladac. */
  reconnected: () => void;
  /** Polaczenie zamkniete na dobre. */
  closed: () => void;
  /** Wiadomosc serwera nie przeszla schematu (np. inna wersja serwera): komunikat dla gracza. */
  invalid: (problem: string) => void;
}

/**
 * Odbior wiadomosci z serwera: dane przechodza przez schemat, a niezgodne trafiaja do `invalid` z komunikatem,
 * zamiast rzucac wyjatek w srodku klienta SignalR — tam nikt by go nie zobaczyl, a stol stalby bez zmian.
 */
export function receive<T>(
  name: string,
  schema: z.ZodType<T>,
  handler: ((value: T) => void) | undefined,
  invalid: ((problem: string) => void) | undefined,
): (data: unknown) => void {
  return (data) => {
    const parsed = schema.safeParse(data);
    if (parsed.success) {
      handler?.(parsed.data);
    } else {
      invalid?.(`Serwer przysłał wiadomość ${name}, której ta strona nie rozumie. Odśwież stronę.`);
    }
  };
}

/**
 * Metody huba (GameHub). Komponenty znaja tylko ten kontrakt — klient SignalR siedzi w hub.ts,
 * a testy podstawiaja wlasna implementacje.
 */
export interface GameServer {
  createRoom: (args: CreateRoomArgs) => Promise<SeatTicket>;
  joinRoom: (room: string, name: string) => Promise<SeatTicket>;
  rejoin: (room: string, token: string) => Promise<SeatTicket>;
  watch: (room: string) => Promise<TableView | null>;
  watchLobby: () => Promise<RoomInfo[]>;
  addBot: (seat: number, bot: string) => Promise<void>;
  start: () => Promise<void>;
  /** @param number ile ruchow juz zagrano (TableView.moves) — serwer odrzuci ruch do nieaktualnej pozycji. */
  makeMove: (number: number, move: string) => Promise<void>;
  close: () => Promise<void>;
}

export type Connect = (events: Partial<GameEvents>) => Promise<GameServer>;

/**
 * Czy serwer odmowil (HubException z komunikatem), a nie zawiodla siec. Tylko odmowa znaczy np. "nie znam
 * tego biletu" — zerwane polaczenie w trakcie Rejoin nie jest powodem, zeby zapomniec miejsce.
 */
export function isRefusal(error: unknown): boolean {
  return error instanceof Error && error.message.includes(marker);
}

const marker = "HubException: ";

/** Bledy sieci i transportu z klienta SignalR i fetch — po angielsku i bez znaczenia dla gracza. */
const transport = /WebSocket|Failed to (?:fetch|start|complete)|connection|NetworkError|timeout|Invocation canceled/iu;

/** Co powiedziec graczowi, gdy zawiodla siec albo serwer gry nie odpowiada. */
export const OfflineMessage = "Nie udało się połączyć z serwerem gry. Sprawdź internet i odśwież stronę.";

/**
 * Komunikat dla gracza z bledu wywolania huba. Klient SignalR opakowuje HubException w zdanie
 * "An unexpected error occurred invoking 'X' on the server. HubException: <komunikat>"; z niego zostaje sam
 * komunikat serwera. Angielski blad transportu zamienia sie w zdanie po polsku, a wlasne bledy strony zostaja.
 */
export function hubMessage(error: unknown): string {
  const text = error instanceof Error ? error.message : String(error);
  const at = text.lastIndexOf(marker);
  if (at >= 0) return text.slice(at + marker.length);
  return transport.test(text) ? OfflineMessage : text;
}
