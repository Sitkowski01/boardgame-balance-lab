import type { MovePlayed, RoomInfo, SeatInfo, TableView } from "../api/schemas";

export const roomId = "11111111-2222-3333-4444-555555555555";
export const matchId = "99999999-8888-7777-6666-555555555555";

export function seat(index: number, patch: Partial<SeatInfo> = {}): SeatInfo {
  return { seat: index, kind: "Human", name: `Gracz${String(index)}`, connected: true, autopilot: false, ...patch };
}

export function room(patch: Partial<RoomInfo> = {}): RoomInfo {
  return {
    id: roomId,
    version: 1,
    game: "connectfour",
    gameName: "ConnectFour",
    variant: null,
    status: "Waiting",
    host: 0,
    seats: [seat(0, { name: "Ala" }), seat(1, { kind: "Empty", name: null, connected: false })],
    moves: 0,
    toMove: null,
    deadline: null,
    matchId: null,
    scores: [],
    createdAt: "2026-10-19T10:00:00Z",
    ...patch,
  };
}

export function table(view: unknown, patch: Partial<TableView> = {}): TableView {
  return { room: roomId, seat: 0, version: 1, moves: 0, view, legalMoves: [], deadline: null, ...patch };
}

export function played(number: number, move: string, patch: Partial<MovePlayed> = {}): MovePlayed {
  return { room: roomId, number, seat: (number - 1) % 2, move, byBot: false, ...patch };
}
