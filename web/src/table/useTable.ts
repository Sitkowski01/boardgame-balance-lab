import { useCallback, useEffect, useReducer, useRef, useState } from "react";
import { hubMessage, isRefusal, type Connect, type GameServer } from "../api/connection";
import { guarded } from "./guarded";
import type { Tickets } from "./tickets";
import { emptyTable, reduceTable, type Table } from "./table";

export type Connection = "connecting" | "online" | "reconnecting" | "closed";

// Numer pokoju to Guid: inny adres serwer odrzuci (blad wiazania argumentu), wiec nie ma po co sie laczyc.
const roomPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/iu;

/** Serwer nie zna pokoju (Lobby.Find): rozszedl sie po partii albo adres jest zly. */
export function isMissingRoom(error: unknown): boolean {
  return isRefusal(error) && hubMessage(error).startsWith("Nie ma pokoju");
}

export interface TableControls {
  table: Table;
  link: Connection;
  error: string | null;
  /** Serwer nie zna tego pokoju (albo numer nie jest Guidem): strona pokazuje tylko to, bez polaczenia. */
  gone: boolean;
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
  const [gone, setGone] = useState(() => !roomPattern.test(room));
  // Numer pozycji, do ktorej poszedl ruch; dopoki widok jej nie zmieni, kolejny klik nic nie wysyla.
  const [sent, setSent] = useState<number | null>(null);
  const server = useRef<GameServer | null>(null);
  // Kazde nowe polaczenie (i odmontowanie) podbija numer: zdarzenia i wyniki starszego polaczenia sa pomijane.
  const generation = useRef(0);
  // Siadanie w toku: drugi klik (albo drugi Enter) nie zajmuje drugiego miejsca.
  const joining = useRef(false);

  /** Pokoju nie ma: polaczenie sie zamyka, a jego zdarzenia przestaja dochodzic. */
  const vanish = useCallback((): void => {
    generation.current += 1;
    void server.current?.close();
    server.current = null;
    setGone(true);
  }, []);

  /** Blad siadania: brak pokoju konczy strone stolu, inne bledy ida do komunikatu. */
  const failed = useCallback(
    (e: unknown): void => {
      if (isMissingRoom(e)) {
        vanish();
      } else {
        setError(hubMessage(e));
      }
    },
    [vanish],
  );

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
            sit(hub).catch(failed);
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
    [connect, sit, failed],
  );

  useEffect(() => {
    if (!roomPattern.test(room)) {
      return undefined;
    }

    // Stan zmienia sie dopiero po polaczeniu (asynchronicznie), nie w samym efekcie.
    let left = false;
    const opening = Promise.resolve().then(() => (left ? undefined : open(sit)));
    opening.catch((e: unknown) => {
      setLink("closed");
      failed(e);
    });
    return () => {
      left = true;
      generation.current += 1;
      void server.current?.close();
      server.current = null;
    };
  }, [room, open, sit, failed]);

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
    gone,
    pending,
    join: async (name) => {
      if (joining.current) {
        return;
      }

      joining.current = true;
      setError(null);
      setLink("connecting");
      try {
        await open(async (hub) => {
          const ticket = await hub.joinRoom(room, name);
          tickets.save(ticket);
          dispatch({ type: "seated", seat: ticket.seat });
        });
      } catch (e) {
        if (isMissingRoom(e)) {
          vanish();
          return;
        }

        setError(hubMessage(e));
        // Po nieudanym siadaniu wraca ogladanie; gdy i ono sie nie uda, zostaje komunikat i "brak polaczenia",
        // a nie wieczne "Laczenie…".
        await open(sit).catch((again: unknown) => {
          setLink("closed");
          if (isMissingRoom(again)) vanish();
        });
      } finally {
        joining.current = false;
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
