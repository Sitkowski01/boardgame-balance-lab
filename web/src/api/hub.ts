import { HttpTransportType, HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { z } from "zod";
import { receive, type Connect, type GameEvents, type GameServer } from "./connection";
import { movePlayed, roomInfo, seatTicket, tableView } from "./schemas";

/**
 * Polaczenie z GameHub po WebSocket. Jedno polaczenie siedzi w jednym pokoju, wiec kazdy stol
 * (i lobby) otwiera wlasne. Wiadomosci z serwera przechodza przez schematy, zanim trafia dalej.
 */
export const connectHub: Connect = async (events: Partial<GameEvents>, url = "/hubs/game"): Promise<GameServer> => {
  const hub = new HubConnectionBuilder()
    .withUrl(url, { transport: HttpTransportType.WebSockets, skipNegotiation: true })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build();

  hub.on("Room", receive("Room", roomInfo, events.room, events.invalid));
  hub.on("View", receive("View", tableView, events.view, events.invalid));
  hub.on("Move", receive("Move", movePlayed, events.move, events.invalid));
  hub.on("Lobby", receive("Lobby", roomInfo, events.lobby, events.invalid));
  hub.onreconnecting(() => events.reconnecting?.());
  hub.onreconnected(() => events.reconnected?.());
  hub.onclose(() => events.closed?.());
  await hub.start();

  const call = async <T>(schema: z.ZodType<T>, method: string, ...args: unknown[]): Promise<T> =>
    schema.parse(await hub.invoke(method, ...args));

  return {
    createRoom: (args) => call(seatTicket, "CreateRoom", args),
    joinRoom: (room, name) => call(seatTicket, "JoinRoom", room, name),
    rejoin: (room, token) => call(seatTicket, "Rejoin", room, token),
    watch: (room) => call(tableView.nullable(), "Watch", room),
    watchLobby: () => call(z.array(roomInfo), "WatchLobby"),
    addBot: async (seat, bot) => {
      await hub.invoke("AddBot", seat, bot);
    },
    start: async () => {
      await hub.invoke("Start");
    },
    makeMove: async (number, move) => {
      await hub.invoke("MakeMove", number, move);
    },
    close: () => hub.stop(),
  };
};
