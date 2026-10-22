import { vi } from "vitest";
import type { Connect, GameEvents, GameServer } from "../api/connection";
import type { SeatTicket } from "../api/schemas";
import type { Tickets } from "../table/tickets";
import { roomId } from "./fixtures";

/** Jedno otwarte polaczenie: metody huba jako atrapy i zdarzenia, ktore test "wysyla z serwera". */
export interface FakeLink {
  server: { [K in keyof GameServer]: ReturnType<typeof vi.fn<GameServer[K]>> };
  emit: GameEvents;
}

/** Atrapa Connect: kazde wywolanie to nowe polaczenie (jak w przegladarce), wszystkie widoczne w `links`. */
export function fakeConnect(setup: (server: FakeLink["server"]) => void = () => undefined): { connect: Connect; links: FakeLink[] } {
  const links: FakeLink[] = [];
  const connect = vi.fn((events: Partial<GameEvents>) => {
    const server: FakeLink["server"] = {
      createRoom: vi.fn<GameServer["createRoom"]>().mockResolvedValue({ room: roomId, seat: 0, token: "host" }),
      joinRoom: vi.fn<GameServer["joinRoom"]>().mockResolvedValue({ room: roomId, seat: 1, token: "guest" }),
      rejoin: vi.fn<GameServer["rejoin"]>((room, token) => Promise.resolve({ room, seat: token === "guest" ? 1 : 0, token })),
      watch: vi.fn<GameServer["watch"]>().mockResolvedValue(null),
      watchLobby: vi.fn<GameServer["watchLobby"]>().mockResolvedValue([]),
      addBot: vi.fn<GameServer["addBot"]>().mockResolvedValue(undefined),
      start: vi.fn<GameServer["start"]>().mockResolvedValue(undefined),
      makeMove: vi.fn<GameServer["makeMove"]>().mockResolvedValue(undefined),
      close: vi.fn<GameServer["close"]>().mockResolvedValue(undefined),
    };
    setup(server);
    const emit: GameEvents = {
      room: (r) => events.room?.(r),
      view: (v) => events.view?.(v),
      move: (m) => events.move?.(m),
      lobby: (r) => events.lobby?.(r),
      reconnecting: () => events.reconnecting?.(),
      reconnected: () => events.reconnected?.(),
      closed: () => events.closed?.(),
      invalid: (problem) => events.invalid?.(problem),
    };
    links.push({ server, emit });
    return Promise.resolve(server);
  });
  return { connect, links };
}

/** Bilety w pamieci zamiast sessionStorage. */
export function memoryTickets(initial: SeatTicket[] = []): Tickets & { all: Map<string, SeatTicket> } {
  const all = new Map(initial.map((t) => [t.room, t]));
  return {
    all,
    find: (room) => all.get(room) ?? null,
    save: (ticket) => {
      all.set(ticket.room, ticket);
    },
    forget: (room) => {
      all.delete(room);
    },
  };
}

/** Ostatnie polaczenie (np. po join otwiera sie nowe). */
export function last(links: FakeLink[]): FakeLink {
  const link = links.at(-1);
  if (link === undefined) {
    throw new Error("Nie otwarto zadnego polaczenia.");
  }

  return link;
}
