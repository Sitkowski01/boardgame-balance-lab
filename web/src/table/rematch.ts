import type { Connect } from "../api/connection";
import type { RoomInfo, SeatTicket } from "../api/schemas";

/**
 * Rewanz: nowy stol z ta sama gra, liczba miejsc i wariantem, gracz na pierwszym miejscu, a pozostale miejsca
 * w tej samej kolejnosci — boty z tym samym opisem, miejsca innych ludzi wolne. Gdy przy stole sa same boty,
 * partia rusza od razu; inni ludzie musza najpierw usiasc, wiec wtedy rewanz konczy sie w poczekalni.
 *
 * Osobne polaczenie, bo jedno polaczenie to jedno miejsce. Po zamknieciu serwer trzyma miejsce przez
 * DisconnectGrace, a strona nowego stolu wraca na nie biletem od razu po przejsciu.
 */
export async function rematch(connect: Connect, room: RoomInfo, seat: number): Promise<SeatTicket> {
  const me = room.seats[seat];
  if (me?.kind !== "Human" || me.name === null) {
    throw new Error("Rewanż może zaproponować tylko gracz przy stole.");
  }

  const hub = await connect({});
  try {
    const ticket = await hub.createRoom({ game: room.game, seats: room.seats.length, variant: room.variant, name: me.name });
    const others = room.seats.filter((s) => s.seat !== seat);
    for (const [i, other] of others.entries()) {
      if (other.kind === "Bot" && other.name !== null) {
        await hub.addBot(i + 1, other.name);
      }
    }

    if (others.every((s) => s.kind === "Bot")) {
      await hub.start();
    }

    return ticket;
  } finally {
    await hub.close();
  }
}
