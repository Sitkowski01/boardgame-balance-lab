import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { fakeConnect, last, memoryTickets, type FakeLink } from "../test/fakeServer";
import { matchId, played, room, roomId, seat, table } from "../test/fixtures";
import { connectFour } from "../test/boards";
import { TablePage } from "./TablePage";

const now = Date.parse("2026-10-19T10:00:00Z");

function page(options: { tickets?: ReturnType<typeof memoryTickets>; setup?: (server: FakeLink["server"]) => void } = {}) {
  const tickets = options.tickets ?? memoryTickets();
  const { connect, links } = fakeConnect(options.setup);
  const navigate = vi.fn<(path: string) => void>();
  const view = render(<TablePage room={roomId} connect={connect} tickets={tickets} navigate={navigate} now={() => now} />);
  return { tickets, connect, links, navigate, view, user: userEvent.setup() };
}

/** Serwer wysyla stan pokoju (w act, bo zmienia stan Reacta). */
function send(link: FakeLink, event: (emit: FakeLink["emit"]) => void): void {
  act(() => {
    event(link.emit);
  });
}

const host = { room: roomId, seat: 0, token: "host" };

beforeEach(() => {
  localStorage.clear();
});

describe("TablePage", () => {
  it("watches without a ticket and shows the room", async () => {
    const { links } = page();
    await waitFor(() => {
      expect(links[0]?.server.watch).toHaveBeenCalledWith(roomId);
    });

    expect(screen.getByText("Wczytywanie stołu…")).toBeInTheDocument();
    send(last(links), (e) => {
      e.room(room());
    });

    expect(screen.getByRole("heading", { name: "Czwórki" })).toBeInTheDocument();
    expect(screen.getByText("oglądasz")).toBeInTheDocument();
    expect(screen.getByText("czeka na graczy")).toBeInTheDocument();
    expect(within(screen.getByRole("list", { name: "Miejsca" })).getByText("wolne miejsce")).toBeInTheDocument();
    expect(screen.queryByText(/^wariant:/u)).not.toBeInTheDocument();

    send(last(links), (e) => {
      e.room(room({ version: 2, variant: "win=9" }));
    });
    expect(screen.getByText("wariant: win=9")).toHaveClass("badge", "accent");
  });

  it("returns to the seat with the ticket from this browser tab", async () => {
    const { links } = page({ tickets: memoryTickets([host]) });
    await waitFor(() => {
      expect(links[0]?.server.rejoin).toHaveBeenCalledWith(roomId, "host");
    });
    send(last(links), (e) => {
      e.room(room());
    });

    expect(await screen.findByRole("button", { name: "Zacznij partię" })).toBeInTheDocument();
    expect(screen.queryByText("oglądasz")).not.toBeInTheDocument();
    expect(links[0]?.server.watch).not.toHaveBeenCalled();
  });

  it("forgets a ticket the server no longer knows and watches instead", async () => {
    const tickets = memoryTickets([host]);
    const { links } = page({
      tickets,
      setup: (s) => {
        s.rejoin.mockRejectedValue(new Error("HubException: Nieznany bilet."));
        s.watch.mockResolvedValue(table(connectFour(), { seat: null }));
      },
    });

    await waitFor(() => {
      expect(links[0]?.server.watch).toHaveBeenCalled();
    });
    expect(tickets.all.size).toBe(0);
  });

  it("lets the host add a chosen bot and start", async () => {
    const { links, user } = page({ tickets: memoryTickets([host]) });
    await waitFor(() => {
      expect(links[0]?.server.rejoin).toHaveBeenCalled();
    });
    send(last(links), (e) => {
      e.room(room());
    });

    await user.selectOptions(await screen.findByLabelText("Bot na miejsce 2"), "random");
    await user.click(screen.getByRole("button", { name: "Dodaj bota" }));
    await user.click(screen.getByRole("button", { name: "Zacznij partię" }));

    expect(last(links).server.addBot).toHaveBeenCalledWith(1, "random");
    expect(last(links).server.start).toHaveBeenCalled();
  });

  it("plays a move with the number of moves already made and shows the result", async () => {
    const { links, user } = page({ tickets: memoryTickets([host]) });
    await waitFor(() => {
      expect(links[0]?.server.rejoin).toHaveBeenCalled();
    });
    const seats = [seat(0, { name: "Ala" }), seat(1, { kind: "Bot", name: "random" })];
    send(last(links), (e) => {
      e.room(room({ status: "Playing", seats, toMove: 0, moves: 2, deadline: "2026-10-19T10:00:42Z" }));
      e.view(table(connectFour(), { moves: 2, legalMoves: ["3"] }));
      e.move(played(1, "3"));
      e.move(played(2, "4", { byBot: true }));
    });

    expect(screen.getByText("Twój ruch!")).toBeInTheDocument();
    expect(screen.getByLabelText("Czas na ruch")).toHaveTextContent("42 s");
    expect(
      within(screen.getByRole("region", { name: "Ostatnie ruchy" }))
        .getAllByRole("listitem")
        .map((li) => li.textContent),
    ).toEqual(["Ala: wrzuca do kolumny 4", "Losowy: wrzuca do kolumny 5"]);
    await user.click(screen.getByRole("button", { name: "Kolumna 4" }));
    expect(last(links).server.makeMove).toHaveBeenCalledWith(2, "3");

    send(last(links), (e) => {
      e.room(room({ version: 9, status: "Finished", seats, scores: [1, 0], matchId }));
    });
    expect(screen.getByRole("heading", { name: "Koniec partii" })).toBeInTheDocument();
    expect(screen.getByText("Wygrywa: Ala")).toBeInTheDocument();
    expect(screen.queryByLabelText("Czas na ruch")).not.toBeInTheDocument();
  });

  it("marks moves a bot made for a human and shows seat states", async () => {
    const { links } = page();
    await waitFor(() => {
      expect(links).toHaveLength(1);
    });
    send(last(links), (e) => {
      e.room(
        room({
          status: "Playing",
          toMove: 1,
          seats: [seat(0, { name: "Ala", connected: false }), seat(1, { name: "Ola", autopilot: true })],
        }),
      );
      e.move(played(1, "0", { seat: 0, byBot: true }));
    });

    expect(screen.getByText("rozłączony")).toBeInTheDocument();
    expect(screen.getByText("gra za niego bot")).toBeInTheDocument();
    expect(screen.getByText("gospodarz")).toBeInTheDocument();
    expect(within(screen.getByRole("region", { name: "Ostatnie ruchy" })).getByRole("listitem")).toHaveTextContent(
      /^Ala \(bot za gracza\): /u,
    );
    expect(screen.queryByLabelText("Czas na ruch")).not.toBeInTheDocument();
    expect(screen.queryByText("Twoje imię")).not.toBeInTheDocument();
  });

  it("shows a server refusal of a move", async () => {
    const { links, user } = page({
      tickets: memoryTickets([host]),
      setup: (s) =>
        s.makeMove.mockRejectedValue(
          new Error("An unexpected error occurred invoking 'MakeMove' on the server. HubException: Nie twoja kolej."),
        ),
    });
    await waitFor(() => {
      expect(links[0]?.server.rejoin).toHaveBeenCalled();
    });
    send(last(links), (e) => {
      e.room(room({ status: "Playing", toMove: 0 }));
      e.view(table(connectFour(), { legalMoves: ["0"] }));
    });

    await user.click(screen.getByRole("button", { name: "Kolumna 1" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Nie twoja kolej.");
  });

  it("links to the replay of a finished or abandoned match", async () => {
    const { links, navigate, user } = page();
    await waitFor(() => {
      expect(links).toHaveLength(1);
    });
    send(last(links), (e) => {
      e.room(room({ status: "Abandoned", matchId }));
    });

    expect(screen.getByRole("heading", { name: "Partia przerwana" })).toBeInTheDocument();
    await user.click(screen.getByRole("link", { name: "Obejrzyj powtórkę" }));
    await user.click(screen.getByRole("link", { name: "← Lobby" }));

    expect(navigate.mock.calls).toEqual([[`/matches/${matchId}`], ["/"]]);
  });
});
