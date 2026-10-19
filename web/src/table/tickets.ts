import { z } from "zod";
import { seatTicket, type SeatTicket } from "../api/schemas";

/**
 * Bilety miejsc w sessionStorage: odswiezenie strony wraca na to samo miejsce (Rejoin), a druga karta
 * przegladarki ma wlasne miejsca — mozna w niej usiasc jako drugi gracz. Nazwa gracza jest w localStorage.
 * Przegladarka moze odmowic dostepu (tryb prywatny, zablokowane dane), wiec kazdy zapis i odczyt jest w try.
 */
export interface Tickets {
  find: (room: string) => SeatTicket | null;
  save: (ticket: SeatTicket) => void;
  forget: (room: string) => void;
}

const key = (room: string): string => `balance-lab:ticket:${room}`;

export function storedTickets(storage: () => Storage = () => sessionStorage): Tickets {
  return {
    find: (room) => {
      try {
        const text = storage().getItem(key(room));
        const parsed = text === null ? null : seatTicket.safeParse(JSON.parse(text));
        return parsed?.success === true && parsed.data.room === room ? parsed.data : null;
      } catch {
        return null;
      }
    },
    save: (ticket) => {
      try {
        storage().setItem(key(ticket.room), JSON.stringify(ticket));
      } catch {
        // Bez pamieci przegladarki gra dziala dalej, tylko odswiezenie nie wroci na miejsce.
      }
    },
    forget: (room) => {
      try {
        storage().removeItem(key(room));
      } catch {
        // Jak wyzej.
      }
    },
  };
}

const nameKey = "balance-lab:name";
const name = z.string().min(1);

export function loadName(storage: () => Storage = () => localStorage): string {
  try {
    const parsed = name.safeParse(storage().getItem(nameKey));
    return parsed.success ? parsed.data : "";
  } catch {
    return "";
  }
}

export function saveName(value: string, storage: () => Storage = () => localStorage): void {
  try {
    storage().setItem(nameKey, value);
  } catch {
    // Nazwa zostanie tylko do zamkniecia strony.
  }
}
