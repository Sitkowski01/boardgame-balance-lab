import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { fakeConnect, last, memoryTickets, type FakeLink } from "../test/fakeServer";
import { room, roomId, seat } from "../test/fixtures";
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

beforeEach(() => {
  localStorage.clear();
});

describe("sitting down at the table", () => {
  it("sits down from a new connection and remembers the ticket and the name", async () => {
    const { links, tickets, user } = page();
    await waitFor(() => {
      expect(links).toHaveLength(1);
    });
    send(last(links), (e) => {
      e.room(room());
    });

    await user.type(screen.getByLabelText("Twoje imię"), "  Ola ");
    await user.click(screen.getByRole("button", { name: "Usiądź przy stole" }));

    await waitFor(() => {
      expect(links).toHaveLength(2);
    });
    expect(links[0]?.server.close).toHaveBeenCalled();
    expect(links[1]?.server.joinRoom).toHaveBeenCalledWith(roomId, "Ola");
    expect(tickets.find(roomId)).toEqual({ room: roomId, seat: 1, token: "guest" });
    expect(localStorage.getItem("balance-lab:name")).toBe("Ola");
    send(last(links), (e) => {
      e.room(room({ version: 2, seats: [seat(0, { name: "Ala" }), seat(1, { name: "Ola" })] }));
    });
    expect(await screen.findByText("Czekamy, aż gospodarz zacznie partię.")).toBeInTheDocument();
  });

  it("shows why sitting down failed and keeps watching", async () => {
    const { links, user } = page({ setup: (s) => s.joinRoom.mockRejectedValue(new Error("HubException: Stol jest pelny.")) });
    await waitFor(() => {
      expect(links).toHaveLength(1);
    });
    send(last(links), (e) => {
      e.room(room());
    });

    expect(screen.getByRole("button", { name: "Usiądź przy stole" })).toBeDisabled();
    await user.type(screen.getByLabelText("Twoje imię"), "Ola");
    await user.click(screen.getByRole("button", { name: "Usiądź przy stole" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Stol jest pelny.");
    await waitFor(() => {
      expect(links).toHaveLength(3);
    });
    expect(links[2]?.server.watch).toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "OK" }));
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});
