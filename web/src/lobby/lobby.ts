import type { RoomInfo, RoomStatus } from "../api/schemas";

/** Zmiana pokoju z lobby: nowy pokoj dochodzi, znany jest podmieniany, chyba ze wiadomosc jest starsza. */
export function upsertRoom(rooms: readonly RoomInfo[], room: RoomInfo): RoomInfo[] {
  const known = rooms.find((r) => r.id === room.id);
  if (known === undefined) {
    return [...rooms, room];
  }

  return known.version > room.version ? [...rooms] : rooms.map((r) => (r.id === room.id ? room : r));
}

const order: Record<RoomStatus, number> = { Waiting: 0, Playing: 1, Finished: 2, Abandoned: 3 };

/** Najpierw stoly, do ktorych mozna usiasc, potem trwajace i skonczone; w grupie najnowsze na gorze. */
export function sortRooms(rooms: readonly RoomInfo[]): RoomInfo[] {
  return [...rooms].sort((a, b) => order[a.status] - order[b.status] || b.createdAt.localeCompare(a.createdAt));
}

export function freeSeats(room: RoomInfo): number {
  return room.seats.filter((s) => s.kind === "Empty").length;
}

export const statusLabel: Record<RoomStatus, string> = {
  Waiting: "czeka na graczy",
  Playing: "w grze",
  Finished: "zakończona",
  Abandoned: "przerwana",
};
