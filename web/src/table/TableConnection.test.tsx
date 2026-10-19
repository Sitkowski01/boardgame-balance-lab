import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { GameServer } from "../api/connection";
import { fakeConnect, last, memoryTickets, type FakeLink } from "../test/fakeServer";
import { room, roomId } from "../test/fixtures";
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

describe("TablePage connection", () => {
  it("shows the connection state and sits down again after a reconnect", async () => {
    const { links } = page({ tickets: memoryTickets([host]) });
    await waitFor(() => {
      expect(links[0]?.server.rejoin).toHaveBeenCalledTimes(1);
    });

    send(last(links), (e) => {
      e.reconnecting();
    });
    expect(screen.getByText("Połączenie zerwane — łączę ponownie…")).toBeInTheDocument();
    send(last(links), (e) => {
      e.reconnected();
    });
    await waitFor(() => {
      expect(links[0]?.server.rejoin).toHaveBeenCalledTimes(2);
    });
    expect(screen.queryByText(/Połączenie zerwane/u)).not.toBeInTheDocument();
    send(last(links), (e) => {
      e.closed();
    });
    expect(screen.getByText("Brak połączenia z serwerem. Odśwież stronę.")).toBeInTheDocument();
  });

  it("reports a failed sit down after a reconnect", async () => {
    const { links } = page({ setup: (s) => s.watch.mockResolvedValueOnce(null).mockRejectedValue(new Error("Pokoj zniknal.")) });
    await waitFor(() => {
      expect(links[0]?.server.watch).toHaveBeenCalledTimes(1);
    });

    send(last(links), (e) => {
      e.reconnected();
    });

    expect(await screen.findByRole("alert")).toHaveTextContent("Pokoj zniknal.");
  });

  it("reports a server that cannot be reached", async () => {
    const connect = vi.fn().mockRejectedValue(new Error("Failed to fetch"));
    render(<TablePage room={roomId} connect={connect} tickets={memoryTickets()} navigate={vi.fn()} />);

    expect(await screen.findByRole("alert")).toHaveTextContent("Failed to fetch");
    expect(screen.getByText("Brak połączenia z serwerem. Odśwież stronę.")).toBeInTheDocument();
  });

  it("ignores events of a connection it already left", async () => {
    const { links, user } = page();
    await waitFor(() => {
      expect(links).toHaveLength(1);
    });
    const first = last(links);
    send(first, (e) => {
      e.room(room());
    });
    await user.type(screen.getByLabelText("Twoje imię"), "Ola");
    await user.click(screen.getByRole("button", { name: "Usiądź przy stole" }));
    await waitFor(() => {
      expect(links).toHaveLength(2);
    });

    send(first, (e) => {
      e.room(room({ version: 50, gameName: "Stary pokoj" }));
      e.closed();
    });

    expect(screen.queryByRole("heading", { name: "Stary pokoj" })).not.toBeInTheDocument();
    expect(screen.queryByText(/Brak połączenia/u)).not.toBeInTheDocument();
  });

  it("does not connect at all when the table is left at once", async () => {
    const { connect, view } = page();
    view.unmount();
    await Promise.resolve();

    expect(connect).not.toHaveBeenCalled();
  });

  it("closes the connection when leaving the table, also one still opening", async () => {
    const { links, view } = page();
    await waitFor(() => {
      expect(links).toHaveLength(1);
    });
    view.unmount();
    expect(links[0]?.server.close).toHaveBeenCalled();

    let open: (server: GameServer) => void = () => undefined;
    const pending = fakeConnect();
    const connect = vi.fn(() => new Promise<GameServer>((resolve) => (open = resolve)));
    const late = render(<TablePage room={roomId} connect={connect} tickets={memoryTickets()} navigate={vi.fn()} />);
    await waitFor(() => {
      expect(connect).toHaveBeenCalled();
    });
    late.unmount();
    await pending.connect({});
    const server = last(pending.links).server;
    open(server);
    await waitFor(() => {
      expect(server.close).toHaveBeenCalled();
    });
    expect(server.watch).not.toHaveBeenCalled();
  });
});
