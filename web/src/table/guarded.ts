import type { GameEvents } from "../api/connection";

/** Zdarzenia, ktore dochodza tylko, dopoki polaczenie jest biezace. */
export function guarded(current: () => boolean, events: GameEvents): GameEvents {
  const only =
    <A extends unknown[]>(handler: (...args: A) => void) =>
    (...args: A): void => {
      if (current()) {
        handler(...args);
      }
    };
  return {
    room: only(events.room),
    view: only(events.view),
    move: only(events.move),
    lobby: only(events.lobby),
    reconnecting: only(events.reconnecting),
    reconnected: only(events.reconnected),
    closed: only(events.closed),
  };
}
