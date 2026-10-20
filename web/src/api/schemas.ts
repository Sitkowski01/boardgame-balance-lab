import { z } from "zod";

// Kontrakt z serwerem (BalanceLab.Api): kazda wiadomosc z sieci przechodzi przez schemat, zanim trafi do stanu.
// Serwer wysyla camelCase, wyliczenia jako tekst i daty ISO.

export const roomStatus = z.enum(["Waiting", "Playing", "Finished", "Abandoned"]);
export type RoomStatus = z.infer<typeof roomStatus>;

export const seatInfo = z.object({
  seat: z.number().int(),
  kind: z.enum(["Empty", "Human", "Bot"]),
  name: z.string().nullable(),
  connected: z.boolean(),
  autopilot: z.boolean(),
});
export type SeatInfo = z.infer<typeof seatInfo>;

export const roomInfo = z.object({
  id: z.string(),
  version: z.number().int(),
  game: z.string(),
  gameName: z.string(),
  variant: z.string().nullable(),
  status: roomStatus,
  host: z.number().int(),
  seats: z.array(seatInfo),
  moves: z.number().int(),
  toMove: z.number().int().nullable(),
  deadline: z.string().nullable(),
  matchId: z.string().nullable(),
  scores: z.array(z.number()),
  createdAt: z.string(),
});
export type RoomInfo = z.infer<typeof roomInfo>;

/** Widok gry jest wlasny dla kazdej gry — sprawdza go dopiero plansza tej gry. */
export const tableView = z.object({
  room: z.string(),
  seat: z.number().int().nullable(),
  version: z.number().int(),
  moves: z.number().int(),
  view: z.unknown(),
  legalMoves: z.array(z.string()),
  deadline: z.string().nullable(),
});
export type TableView = z.infer<typeof tableView>;

export const movePlayed = z.object({
  room: z.string(),
  number: z.number().int(),
  seat: z.number().int(),
  move: z.string(),
  byBot: z.boolean(),
});
export type MovePlayed = z.infer<typeof movePlayed>;

export const seatTicket = z.object({ room: z.string(), seat: z.number().int(), token: z.string() });
export type SeatTicket = z.infer<typeof seatTicket>;

export const gameInfo = z.object({
  key: z.string(),
  name: z.string(),
  minPlayers: z.number().int(),
  maxPlayers: z.number().int(),
  hasVariants: z.boolean(),
  variantSyntax: z.string().nullable(),
  profiles: z.array(z.string()),
});
export type GameInfo = z.infer<typeof gameInfo>;

export const matchReplay = z.object({
  id: z.string(),
  game: z.string(),
  gameName: z.string(),
  variant: z.string().nullable(),
  players: z.array(z.string()),
  status: z.enum(["InProgress", "Finished", "Abandoned"]),
  moves: z.array(z.string()),
  scores: z.array(z.number()),
  verified: z.boolean(),
  problem: z.string().nullable(),
});
export type MatchReplay = z.infer<typeof matchReplay>;

export const replayFrame = z.object({ match: z.string(), moves: z.number().int(), view: z.unknown() });
export type ReplayFrame = z.infer<typeof replayFrame>;
