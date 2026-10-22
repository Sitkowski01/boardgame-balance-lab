import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { SeatTicket } from "../api/schemas";
import { fakeConnect, last, memoryTickets, type FakeLink } from "../test/fakeServer";
import { matchId, room, roomId } from "../test/fixtures";
import { TablePage } from "./TablePage";
import { isMissingRoom } from "./useTable";

function page(options: { room?: string; tickets?: ReturnType<typeof memoryTickets>; setup?: (server: FakeLink["server"]) => void } = {}) {
  const tickets = options.tickets ?? memoryTickets();
  const { connect, links } = fakeConnect(options.setup);
  const navigate = vi.fn<(path: string) => void>();
  const view = render(<TablePage room={options.room ?? roomId} connect={connect} tickets={tickets} navigate={navigate} />);
  return { tickets, connect, links, navigate, view, user: userEvent.setup() };
}

function send(link: FakeLink, event: (emit: FakeLink["emit"]) => void): void {
  act(() => {
    event(link.emit);
  });
}

const missing = (): Error =>
  new Error(`An unexpected error occurred invoking 'Watch' on the server. HubException: Nie ma pokoju ${roomId}.`);
const host: SeatTicket = { room: roomId, seat: 0, token: "host" };

/** Strona "stolu nie ma": tylko ta wiadomosc, bez bledu, banera polaczenia i wczytywania. */
function expectGone(): void {
  expect(screen.getByRole("heading", { name: "Ten stół już nie istnieje" })).toBeInTheDocument();
  expect(screen.getByText("Pokój rozszedł się po partii albo adres ma literówkę.")).toBeInTheDocument();
  expect(screen.getByRole("link", { name: "← Lobby" })).toHaveAttribute("href", "/");
  expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  expect(screen.queryByText(/Brak połączenia|Łączenie|Wczytywanie stołu/u)).not.toBeInTheDocument();
}

beforeEach(() => {
  localStorage.clear();
});

describe("a table that no longer exists", () => {
  it("says so once when the server does not know the room", async () => {
    const { links, navigate, user } = page({ setup: (s) => s.watch.mockRejectedValue(missing()) });

    expect(await screen.findByRole("heading", { name: "Ten stół już nie istnieje" })).toBeInTheDocument();
    expectGone();
    expect(screen.queryByRole("link", { name: "Powtórka partii" })).not.toBeInTheDocument();
    expect(links[0]?.server.close).toHaveBeenCalled();
    await user.click(screen.getByRole("link", { name: "← Lobby" }));
    expect(navigate).toHaveBeenCalledWith("/");
  });

  it("does not even connect for an address that is not a room number", async () => {
    const { connect } = page({ room: "------------------------------------" });

    expectGone();
    await act(() => Promise.resolve());
    expect(connect).not.toHaveBeenCalled();
  });

  it("forgets the ticket of a room that is gone", async () => {
    const tickets = memoryTickets([host]);
    page({
      tickets,
      setup: (s) => {
        s.rejoin.mockRejectedValue(new Error("HubException: Nie ma pokoju."));
        s.watch.mockRejectedValue(missing());
      },
    });

    expect(await screen.findByRole("heading", { name: "Ten stół już nie istnieje" })).toBeInTheDocument();
    expect(tickets.all.size).toBe(0);
  });

  it("links to the replay when the finished room disappears after a reconnect, and ignores the old connection", async () => {
    const { links, navigate, user } = page({ setup: (s) => s.watch.mockResolvedValueOnce(null).mockRejectedValue(missing()) });
    await waitFor(() => {
      expect(links[0]?.server.watch).toHaveBeenCalledTimes(1);
    });
    send(last(links), (e) => {
      e.room(room({ status: "Finished", scores: [1, 0], matchId }));
      e.reconnected();
    });

    expect(await screen.findByRole("heading", { name: "Ten stół już nie istnieje" })).toBeInTheDocument();
    expectGone();
    await user.click(screen.getByRole("link", { name: "Powtórka partii" }));
    expect(navigate).toHaveBeenCalledWith(`/matches/${matchId}`);

    // Zdarzenia porzuconego polaczenia nic juz nie zmieniaja.
    send(last(links), (e) => {
      e.room(room({ version: 5, status: "Finished", matchId: null }));
    });
    expect(screen.getByRole("link", { name: "Powtórka partii" })).toBeInTheDocument();
  });

  it("stops at a join refused because the room is gone, without watching again", async () => {
    const { links, user } = page({ setup: (s) => s.joinRoom.mockRejectedValue(new Error("HubException: Nie ma pokoju.")) });
    await waitFor(() => {
      expect(links).toHaveLength(1);
    });
    send(last(links), (e) => {
      e.room(room());
    });

    await user.type(screen.getByLabelText("Twoje imię"), "Ola");
    await user.click(screen.getByRole("button", { name: "Usiądź przy stole" }));

    expect(await screen.findByRole("heading", { name: "Ten stół już nie istnieje" })).toBeInTheDocument();
    expect(links).toHaveLength(2);
    expect(links[1]?.server.close).toHaveBeenCalled();
  });

  it("tells a missing room only from a server refusal", () => {
    expect(isMissingRoom(missing())).toBe(true);
    expect(isMissingRoom(new Error("HubException: Stol jest pelny."))).toBe(false);
    expect(isMissingRoom(new Error("Nie ma pokoju"))).toBe(false);
  });
});
