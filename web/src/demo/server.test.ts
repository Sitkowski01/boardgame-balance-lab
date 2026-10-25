import { describe, expect, it, vi } from "vitest";
import { hubMessage, isRefusal, type GameEvents } from "../api/connection";
import { played, room, roomId, table } from "../test/fixtures";
import { demoConnect } from "./server";
import type { DemoTransport } from "./transport";

const ticket = { room: roomId, seat: 0, token: "bilet" };

/** Atrapa serwera w workerze: odpowiedzi po nazwie metody i reczne wysylanie zmian. */
function fakeTransport(answers: Record<string, unknown> = {}) {
  const handlers = new Map<string, (kind: string, data: unknown) => void>();
  const transport = {
    connect: vi.fn(() => Promise.resolve("demo-7")),
    invoke: vi.fn((_connection: string, method: string) => Promise.resolve(JSON.stringify(answers[method] ?? { value: null }))),
    fetch: vi.fn<DemoTransport["fetch"]>(() => Promise.resolve(JSON.stringify({ status: 200, body: [] }))),
    listen: vi.fn((connection: string, handler: (kind: string, data: unknown) => void) => {
      handlers.set(connection, handler);
      return () => handlers.delete(connection);
    }),
  } satisfies DemoTransport;
  const emit = (kind: string, data: unknown): void => handlers.get("demo-7")?.(kind, data);
  return { transport, emit, handlers };
}

describe("demoConnect", () => {
  it("calls the hub methods by name with their arguments and checks the answers", async () => {
    const { transport } = fakeTransport({
      CreateRoom: { value: ticket },
      JoinRoom: { value: ticket },
      Rejoin: { value: ticket },
      Watch: { value: table({}) },
      WatchLobby: { value: [room()] },
    });
    const server = await demoConnect(transport)({});

    expect(await server.createRoom({ game: "klejnoty", seats: 3, variant: null, name: "Ala" })).toEqual(ticket);
    expect(await server.joinRoom(roomId, "Ola")).toEqual(ticket);
    expect(await server.rejoin(roomId, "bilet")).toEqual(ticket);
    expect(await server.watch(roomId)).toEqual(table({}));
    expect(await server.watchLobby()).toEqual([room()]);
    await server.addBot(2, "greedy");
    await server.start();
    await server.makeMove(4, "3");

    expect(transport.invoke.mock.calls).toEqual([
      ["demo-7", "CreateRoom", [{ game: "klejnoty", seats: 3, variant: null, name: "Ala" }]],
      ["demo-7", "JoinRoom", [roomId, "Ola"]],
      ["demo-7", "Rejoin", [roomId, "bilet"]],
      ["demo-7", "Watch", [roomId]],
      ["demo-7", "WatchLobby", []],
      ["demo-7", "AddBot", [2, "greedy"]],
      ["demo-7", "Start", []],
      ["demo-7", "MakeMove", [4, "3"]],
    ]);
  });

  it("turns a room refusal into the same error the SignalR client gives", async () => {
    const { transport } = fakeTransport({ MakeMove: { error: "Nie twoja kolej.", refusal: true } });
    const server = await demoConnect(transport)({});

    const error: unknown = await server.makeMove(1, "3").catch((e: unknown) => e);

    expect(isRefusal(error)).toBe(true);
    expect(hubMessage(error)).toBe("Nie twoja kolej.");
  });

  it("keeps an unreadable call an ordinary error, not a refusal", async () => {
    const { transport } = fakeTransport({ Start: { error: "Nieczytelne wywołanie Start", refusal: false } });
    const server = await demoConnect(transport)({});

    const error: unknown = await server.start().catch((e: unknown) => e);

    expect(isRefusal(error)).toBe(false);
    expect(hubMessage(error)).toBe("Nieczytelne wywołanie Start");
  });

  it("rejects an answer that does not match the schema", async () => {
    const { transport } = fakeTransport({ CreateRoom: { value: { room: "x" } } });
    const server = await demoConnect(transport)({});

    await expect(server.createRoom({ game: "x", seats: 2, variant: null, name: "Ala" })).rejects.toThrow();
  });

  it("passes checked changes to the events and reports ones it cannot read", async () => {
    const { transport, emit } = fakeTransport();
    const events = { room: vi.fn(), view: vi.fn(), move: vi.fn(), lobby: vi.fn(), invalid: vi.fn() } satisfies Partial<GameEvents>;
    await demoConnect(transport)(events);

    emit("room", room());
    emit("view", table({}));
    emit("move", played(1, "3"));
    emit("lobby", room({ version: 2 }));
    emit("move", { zly: true });
    emit("room", { zly: true });
    emit("view", { zly: true });
    emit("lobby", { zly: true });
    emit("nieznane", {});

    expect(events.room).toHaveBeenCalledWith(room());
    expect(events.view).toHaveBeenCalledWith(table({}));
    expect(events.move).toHaveBeenCalledOnce();
    expect(events.lobby).toHaveBeenCalledWith(room({ version: 2 }));
    // Komunikat nazywa wiadomosc jak klient SignalR (Room, View, Move, Lobby).
    expect(events.invalid.mock.calls.map((call) => String(call[0]))).toEqual(
      ["Move", "Room", "View", "Lobby"].map((name) => `Serwer przysłał wiadomość ${name}, której ta strona nie rozumie. Odśwież stronę.`),
    );
  });

  it("closes like a SignalR connection: stops listening, tells the server and reports closed", async () => {
    const { transport, handlers } = fakeTransport();
    const closed = vi.fn();
    const server = await demoConnect(transport)({ closed });

    await server.close();

    expect(handlers.size).toBe(0);
    expect(transport.invoke).toHaveBeenLastCalledWith("demo-7", "Close", []);
    expect(closed).toHaveBeenCalledOnce();
  });

  it("still reports closed when the server could not close the connection", async () => {
    const { transport } = fakeTransport();
    transport.invoke.mockRejectedValue(new Error("worker padl"));
    const warn = vi.spyOn(console, "warn").mockImplementation(() => undefined);
    const closed = vi.fn();
    const server = await demoConnect(transport)({ closed });

    await server.close();

    expect(closed).toHaveBeenCalledOnce();
    expect(warn).toHaveBeenCalledWith("Zamknięcie połączenia z serwerem demo nie powiodło się", expect.any(Error));
    warn.mockRestore();
  });
});
