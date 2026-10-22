import { useCallback, useEffect, useReducer, useRef, useState } from "react";
import { hubMessage, isRefusal, type Connect, type GameServer } from "../api/connection";
import { guarded } from "./guarded";
import type { Tickets } from "./tickets";
import { emptyTable, reduceTable, type Table } from "./table";

export type Connection = "connecting" | "online" | "reconnecting" | "closed";

export interface TableControls {
  table: Table;
  link: Connection;
  error: string | null;
  /** Ruch wyslany, a nowego widoku jeszcze nie ma: plansza nie przyjmuje drugiego kliku. */
  pending: boolean;
  join: (name: string) => Promise<void>;
  addBot: (seat: number, bot: string) => Promise<void>;
  start: () => Promise<void>;
  move: (move: string) => Promise<void>;
  clearError: () => void;
}

/**
 * Stol na zywo. Z biletem z tej karty przegladarki klient wraca na swoje miejsce (Rejoin), bez biletu
 * oglada. Polaczenie siedzi w jednym pokoju jako widz albo gracz, wiec usiasc przy stole po ogladaniu
 * mozna tylko z nowego polaczenia — join() je otwiera. Po zerwaniu sieci klient sam siada z powrotem.
 */
export function useTable(room: string, connect: Connect, tickets: Tickets): TableControls {
  const [table, dispatch] = useReducer(reduceTable, emptyTable);
  const [link, setLink] = useState<Connection>("connecting");
  const [error, setError] = useState<string | null>(null);
  // Numer pozycji, do ktorej poszedl ruch; dopoki widok jej nie zmieni, kolejny klik nic nie wysyla.
  const [sent, setSent] = useState<number | null>(null);
  const server = useRef<GameServer | null>(null);
  // Kazde nowe polaczenie (i odmontowanie) podbija numer: zdarzenia i wyniki starszego polaczenia sa pomijane.
  const generation = useRef(0);

  /** Siada z biletem albo oglada; bilet, ktorego serwer nie zna (np. po restarcie), jest zapominany. */
  const sit = useCallback(
    async (hub: GameServer): Promise<void> => {
      const ticket = tickets.find(room);
      if (ticket !== null) {
        try {
          dispatch({ type: "seated", seat: (await hub.rejoin(room, ticket.token)).seat });
          return;
        } catch (e) {
          // Bilet zapominamy tylko, gdy serwer go odrzucil; blad sieci przerywa probe (wroci z reconnectem).
          if (!isRefusal(e)) {
            throw e;
          }

          tickets.forget(room);
        }
      }

      dispatch({ type: "seated", seat: null });
      const view = await hub.watch(room);
      if (view !== null) {
        dispatch({ type: "view", view });
      }
    },
    [room, tickets],
  );

  /** Zamyka poprzednie polaczenie, otwiera nowe i siada przy stole tak, jak kaze `seat`. */
  const open = useCallback(
    async (seat: (hub: GameServer) => Promise<void>): Promise<void> => {
      const mine = ++generation.current;
      const current = (): boolean => mine === generation.current;
      const previous = server.current;
      server.current = null;
      await previous?.close();
      const hub: GameServer = await connect(
        guarded(current, {
          room: (info) => {
            dispatch({ type: "room", room: info });
          },
          view: (view) => {
            dispatch({ type: "view", view });
          },
          move: (move) => {
            dispatch({ type: "move", move });
          },
          lobby: () => undefined,
          reconnecting: () => {
            setLink("reconnecting");
          },
          reconnected: () => {
            setLink("online");
            sit(hub).catch((e: unknown) => {
              setError(hubMessage(e));
            });
          },
          closed: () => {
            setLink("closed");
          },
          invalid: (problem) => {
            setError(problem);
          },
        }),
      );
      if (!current()) {
        await hub.close();
        return;
      }

      server.current = hub;
      setLink("online");
      await seat(hub);
    },
    [connect, sit],
  );

  useEffect(() => {
    // Stan zmienia sie dopiero po polaczeniu (asynchronicznie), nie w samym efekcie.
    const failed = (e: unknown): void => {
      setLink("closed");
      setError(hubMessage(e));
    };
    let left = false;
    const opening = Promise.resolve().then(() => (left ? undefined : open(sit)));
    opening.catch(failed);
    return () => {
      left = true;
      generation.current += 1;
      void server.current?.close();
      server.current = null;
    };
  }, [open, sit]);

  const act = useCallback(async (action: (hub: GameServer) => Promise<void>): Promise<void> => {
    const hub = server.current;
    if (hub === null) {
      setError("Brak połączenia z serwerem.");
      return;
    }

    try {
      setError(null);
      await action(hub);
    } catch (e) {
      setError(hubMessage(e));
    }
  }, []);

  const moves = table.view?.moves ?? 0;
  const pending = sent === moves;

  return {
    table,
    link,
    error,
    pending,
    join: async (name) => {
      setError(null);
      setLink("connecting");
      try {
        await open(async (hub) => {
          const ticket = await hub.joinRoom(room, name);
          tickets.save(ticket);
          dispatch({ type: "seated", seat: ticket.seat });
        });
      } catch (e) {
        setError(hubMessage(e));
        await open(sit).catch(() => undefined);
      }
    },
    addBot: (seat, bot) => act((hub) => hub.addBot(seat, bot)),
    start: () => act((hub) => hub.start()),
    move: async (move) => {
      if (pending) {
        return;
      }

      setSent(moves);
      await act(async (hub) => {
        try {
          await hub.makeMove(moves, move);
        } catch (e) {
          setSent(null);
          throw e;
        }
      });
    },
    clearError: () => {
      setError(null);
    },
  };
}
