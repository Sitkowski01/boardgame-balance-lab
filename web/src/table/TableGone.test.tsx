import { act, fireEvent, render, renderHook, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { GameServer } from "../api/connection";
import type { SeatTicket } from "../api/schemas";
import { fakeConnect, last, memoryTickets, type FakeLink } from "../test/fakeServer";
import { matchId, room, roomId } from "../test/fixtures";
import { TablePage } from "./TablePage";
import { isMissingRoom, useTable } from "./useTable";

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

describe("sitting down when watching fails too", () => {
  it("keeps the join error and shows no connection instead of endless connecting", async () => {
    let calls = 0;
    const { links, user } = page({
      setup: (s) => {
        calls += 1;
        s.joinRoom.mockRejectedValue(new Error("HubException: Stol jest pelny."));
        // Trzecie polaczenie (powrot do ogladania) tez zawodzi.
        if (calls === 3) s.watch.mockRejectedValue(new Error("WebSocket closed"));
      },
    });
    await waitFor(() => {
      expect(links).toHaveLength(1);
    });
    send(last(links), (e) => {
      e.room(room());
    });

    await user.type(screen.getByLabelText("Twoje imię"), "Ola");
    await user.click(screen.getByRole("button", { name: "Usiądź przy stole" }));

    expect(await screen.findByText("Brak połączenia z serwerem. Odśwież stronę.")).toBeInTheDocument();
    expect(screen.getByRole("alert")).toHaveTextContent("Stol jest pelny.");
    expect(screen.queryByText("Łączenie…")).not.toBeInTheDocument();
  });

  it("shows the gone page when the room disappears between the join and watching again", async () => {
    let calls = 0;
    const { links, user } = page({
      setup: (s) => {
        calls += 1;
        s.joinRoom.mockRejectedValue(new Error("HubException: Stol jest pelny."));
        if (calls === 3) s.watch.mockRejectedValue(missing());
      },
    });
    await waitFor(() => {
      expect(links).toHaveLength(1);
    });
    send(last(links), (e) => {
      e.room(room());
    });

    await user.type(screen.getByLabelText("Twoje imię"), "Ola");
    await user.click(screen.getByRole("button", { name: "Usiądź przy stole" }));

    expect(await screen.findByRole("heading", { name: "Ten stół już nie istnieje" })).toBeInTheDocument();
  });

  it("locks the join button until the server answers", async () => {
    let answer: (ticket: SeatTicket) => void = () => undefined;
    const joining = new Promise<SeatTicket>((resolve) => (answer = resolve));
    const { links, user } = page({ setup: (s) => s.joinRoom.mockReturnValue(joining) });
    await waitFor(() => {
      expect(links).toHaveLength(1);
    });
    send(last(links), (e) => {
      e.room(room());
    });

    await user.type(screen.getByLabelText("Twoje imię"), "Ola");
    await user.dblClick(screen.getByRole("button", { name: "Usiądź przy stole" }));
    expect(screen.getByRole("button", { name: "Siadam…" })).toBeDisabled();
    fireEvent.submit(screen.getByRole("button", { name: "Siadam…" }).closest("form") as HTMLFormElement);
    await waitFor(() => {
      expect(links).toHaveLength(2);
    });
    act(() => {
      answer({ room: roomId, seat: 1, token: "guest" });
    });

    await waitFor(() => {
      expect(screen.queryByRole("button", { name: "Siadam…" })).not.toBeInTheDocument();
    });
    expect(links).toHaveLength(2);
    expect(links[1]?.server.joinRoom).toHaveBeenCalledTimes(1);
  });

  it("unlocks the join button after a refusal", async () => {
    const { links, user } = page({ setup: (s) => s.joinRoom.mockRejectedValue(new Error("HubException: Stol jest pelny.")) });
    await waitFor(() => {
      expect(links).toHaveLength(1);
    });
    send(last(links), (e) => {
      e.room(room());
    });

    await user.type(screen.getByLabelText("Twoje imię"), "Ola");
    await user.click(screen.getByRole("button", { name: "Usiądź przy stole" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Stol jest pelny.");
    await waitFor(() => {
      expect(screen.getByRole("button", { name: "Usiądź przy stole" })).toBeEnabled();
    });
  });

  it("sits down once even when join is called twice at the same time", async () => {
    const { connect, links } = fakeConnect();
    const tickets = memoryTickets();
    const { result } = renderHook(() => useTable(roomId, connect, tickets));
    await waitFor(() => {
      expect(links).toHaveLength(1);
    });

    await act(() => Promise.all([result.current.join("Ola"), result.current.join("Ola")]));

    expect(links).toHaveLength(2);
    expect(links[1]?.server.joinRoom).toHaveBeenCalledTimes(1);
    await act(() => result.current.join("Ola"));
    expect(links).toHaveLength(3);
  });
});

describe("a message from the server that does not fit the schema", () => {
  it("is shown at the table", async () => {
    const { links } = page();
    await waitFor(() => {
      expect(links).toHaveLength(1);
    });

    send(last(links), (e) => {
      e.invalid("Serwer przysłał wiadomość View, której ta strona nie rozumie. Odśwież stronę.");
    });

    expect(screen.getByRole("alert")).toHaveTextContent("Serwer przysłał wiadomość View");
  });
});

describe("TablePage states", () => {
  it("says it is connecting until the connection opens", async () => {
    let open: (server: GameServer) => void = () => undefined;
    const pending = fakeConnect();
    const connect = vi.fn(() => new Promise<GameServer>((resolve) => (open = resolve)));
    render(<TablePage room={roomId} connect={connect} tickets={memoryTickets()} navigate={vi.fn()} />);

    expect(screen.getByText("Łączenie…")).toHaveClass("banner");
    await waitFor(() => {
      expect(connect).toHaveBeenCalled();
    });
    await pending.connect({});
    act(() => {
      open(last(pending.links).server);
    });

    await waitFor(() => {
      expect(screen.queryByText("Łączenie…")).not.toBeInTheDocument();
    });
  });

  it("shows the clock only while the match is played", async () => {
    const deadline = "2026-10-19T10:00:42Z";
    const { links } = page();
    await waitFor(() => {
      expect(links).toHaveLength(1);
    });

    for (const status of ["Waiting", "Finished", "Abandoned"] as const) {
      send(last(links), (e) => {
        e.room(room({ version: 1 + ["Waiting", "Finished", "Abandoned"].indexOf(status), status, deadline, scores: [0, 0] }));
      });
      expect(screen.queryByLabelText("Czas na ruch")).not.toBeInTheDocument();
    }
  });

  it("explains the waiting room differently to the host and to a guest", async () => {
    const guestPage = page();
    await waitFor(() => {
      expect(guestPage.links).toHaveLength(1);
    });
    send(last(guestPage.links), (e) => {
      e.room(room());
    });
    const waiting = screen.getByRole("region", { name: "Poczekalnia" });
    expect(
      within(waiting).getByText(/^Stół czeka na graczy\. Usiądź na wolnym miejscu albo oglądaj, aż gospodarz zacznie\.$/u),
    ).toBeInTheDocument();
    guestPage.view.unmount();

    const hostPage = page({ tickets: memoryTickets([host]) });
    await waitFor(() => {
      expect(hostPage.links[0]?.server.rejoin).toHaveBeenCalled();
    });
    send(last(hostPage.links), (e) => {
      e.room(room());
    });
    expect(
      within(screen.getByRole("region", { name: "Poczekalnia" })).getByText(
        /^Posadź boty na wolnych miejscach albo wyślij link znajomym — partia rusza, gdy klikniesz „Zacznij partię”\.$/u,
      ),
    ).toBeInTheDocument();
  });

  it("colours each recent move by its seat", async () => {
    const { links } = page();
    await waitFor(() => {
      expect(links).toHaveLength(1);
    });
    send(last(links), (e) => {
      e.room(room({ status: "Playing", toMove: 0 }));
      e.move({ room: roomId, number: 1, seat: 0, move: "3", byBot: false });
      e.move({ room: roomId, number: 2, seat: 1, move: "4", byBot: false });
    });

    const items = within(screen.getByRole("region", { name: "Ostatnie ruchy" })).getAllByRole("listitem");
    expect(items.map((li) => li.className)).toEqual(["seat-0", "seat-1"]);
  });
});

describe("useTable without an open connection or with a move on its way", () => {
  it("says there is no connection when acting before it opens", async () => {
    const connect = vi.fn(() => new Promise<GameServer>(() => undefined));
    const tickets = memoryTickets();
    const { result } = renderHook(() => useTable(roomId, connect, tickets));

    await act(() => result.current.start());

    expect(result.current.error).toBe("Brak połączenia z serwerem.");
  });

  it("sends nothing while the previous move waits for a new view", async () => {
    const { connect, links } = fakeConnect((s) => s.makeMove.mockReturnValue(new Promise(() => undefined)));
    const tickets = memoryTickets([host]);
    const { result } = renderHook(() => useTable(roomId, connect, tickets));
    await waitFor(() => {
      expect(result.current.link).toBe("online");
    });

    act(() => {
      void result.current.move("3");
    });
    expect(result.current.pending).toBe(true);
    await act(() => result.current.move("4"));

    expect(last(links).server.makeMove.mock.calls).toEqual([[0, "3"]]);
  });
});
