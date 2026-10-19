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
 * Komunikat dla gracza z bledu wywolania huba. Klient SignalR opakowuje HubException w zdanie
 * "An unexpected error occurred invoking 'X' on the server. HubException: <komunikat>".
 */
/**
 * Czy serwer odmowil (HubException z komunikatem), a nie zawiodla siec. Tylko odmowa znaczy np. "nie znam
 * tego biletu" — zerwane polaczenie w trakcie Rejoin nie jest powodem, zeby zapomniec miejsce.
 */
export function isRefusal(error: unknown): boolean {
  return error instanceof Error && error.message.includes(marker);
}

const marker = "HubException: ";

export function hubMessage(error: unknown): string {
  const text = error instanceof Error ? error.message : String(error);
  const at = text.lastIndexOf(marker);
  return at >= 0 ? text.slice(at + marker.length) : text;
}
