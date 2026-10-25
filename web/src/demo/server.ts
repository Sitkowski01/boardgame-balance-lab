import { z } from "zod";
import { receive, type Connect, type GameEvents, type GameServer } from "../api/connection";
import { movePlayed, roomInfo, seatTicket, tableView } from "../api/schemas";
import type { DemoTransport } from "./transport";

/** Odpowiedz huba demo: wartosc albo blad; odmowa pokoju (refusal) to odpowiednik HubException z serwera. */
const answer = z.union([z.object({ value: z.unknown() }), z.object({ error: z.string(), refusal: z.boolean() })]);

/**
 * Odmowa wyglada jak blad z klienta SignalR ("... HubException: komunikat"), wiec hubMessage i isRefusal
 * dzialaja tak samo jak przy prawdziwym serwerze — strony nie wiedza, z kim rozmawiaja.
 */
function unwrap(text: string): unknown {
  const parsed = answer.parse(JSON.parse(text));
  if ("value" in parsed) return parsed.value;
  throw new Error(parsed.refusal ? `HubException: ${parsed.error}` : parsed.error);
}

/** Polaczenie z hubem demo zamiast SignalR: te same metody, te same schematy wiadomosci. */
export function demoConnect(transport: DemoTransport): Connect {
  return async (events: Partial<GameEvents>): Promise<GameServer> => {
    const connection = await transport.connect();
    const handlers: Record<string, (data: unknown) => void> = {
      room: receive("Room", roomInfo, events.room, events.invalid),
      view: receive("View", tableView, events.view, events.invalid),
      move: receive("Move", movePlayed, events.move, events.invalid),
      lobby: receive("Lobby", roomInfo, events.lobby, events.invalid),
    };
    const stop = transport.listen(connection, (kind, data) => handlers[kind]?.(data));
    const call = async <T>(schema: z.ZodType<T>, method: string, ...args: unknown[]): Promise<T> =>
      schema.parse(unwrap(await transport.invoke(connection, method, args)));
    const run = async (method: string, ...args: unknown[]): Promise<void> => {
      unwrap(await transport.invoke(connection, method, args));
    };

    return {
      createRoom: (args) => call(seatTicket, "CreateRoom", args),
      joinRoom: (room, name) => call(seatTicket, "JoinRoom", room, name),
      rejoin: (room, token) => call(seatTicket, "Rejoin", room, token),
      watch: (room) => call(tableView.nullable(), "Watch", room),
      watchLobby: () => call(z.array(roomInfo), "WatchLobby"),
      addBot: (seat, bot) => run("AddBot", seat, bot),
      start: () => run("Start"),
      makeMove: (number, move) => run("MakeMove", number, move),
      close: async () => {
        stop();
        // Jak stop() klienta SignalR: zamkniecie sie nie wywraca (blad tylko w konsoli), a strona zawsze dostaje
        // "closed" — inaczej nie otworzylaby nowego polaczenia.
        try {
          await run("Close");
        } catch (e) {
          console.warn("Zamknięcie połączenia z serwerem demo nie powiodło się", e);
        } finally {
          events.closed?.();
        }
      },
    };
  };
}

const response = z.object({ status: z.number(), body: z.unknown() });

/**
 * `fetch` dla demo: zadania do /api odpowiada serwer w workerze (tymi samymi kodami i JSON-em co ASP.NET Core),
 * a cala reszta (np. czcionki) idzie do sieci jak zwykle. `route` wybiera worker dla adresu (laboratorium ma osobny).
 */
export function demoFetch(route: (path: string) => DemoTransport, network: typeof fetch): typeof fetch {
  return async (input, init) => {
    const url = new URL(input instanceof Request ? input.url : String(input), window.location.href);
    if (url.origin !== window.location.origin || !url.pathname.startsWith("/api/")) {
      return network(input, init);
    }

    const method = init?.method ?? (input instanceof Request ? input.method : "GET");
    // Tresc jako tekst niezaleznie od tego, jak ja podano (napis, Request z trescia, Blob...); GET jej nie ma.
    const raw = init?.body ?? (input instanceof Request && input.body !== null ? await input.clone().text() : null);
    const body =
      raw === null ? null : typeof raw === "string" ? raw : raw instanceof Blob ? await raw.text() : await new Response(raw).text();
    const { status, body: data } = response.parse(JSON.parse(await route(url.pathname).fetch(method, url.pathname + url.search, body)));
    return data === null
      ? new Response(null, { status })
      : new Response(JSON.stringify(data), { status, headers: { "Content-Type": "application/json" } });
  };
}
