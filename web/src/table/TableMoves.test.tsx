import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { RoomInfo, SeatTicket } from "../api/schemas";
import { fakeConnect, last, memoryTickets, type FakeLink } from "../test/fakeServer";
import { played, room, roomId, seat, table } from "../test/fixtures";
import { connectFour } from "../test/boards";
import { TablePage } from "./TablePage";

const start = Date.parse("2026-10-19T10:00:00Z");
const host: SeatTicket = { room: roomId, seat: 0, token: "host" };
const full = [seat(0, { name: "Ala" }), seat(1, { name: "Ola" })];

/** Stol z danym biletem (albo bez) i jednym stanem pokoju od serwera. */
async function at(info: RoomInfo, ticket: SeatTicket | null, setup?: (server: FakeLink["server"]) => void) {
  const { connect, links } = fakeConnect(setup);
  const navigate = vi.fn<(path: string) => void>();
  let time = start;
  const clock = {
    advance: (ms: number) => {
      time += ms;
    },
  };
  const view = render(
    <TablePage
      room={roomId}
      connect={connect}
      tickets={memoryTickets(ticket === null ? [] : [ticket])}
      navigate={navigate}
      now={() => time}
    />,
  );
  await waitFor(() => {
    expect(ticket === null ? links[0]?.server.watch : links[0]?.server.rejoin).toHaveBeenCalled();
  });
  act(() => {
    last(links).emit.room(info);
  });
  return { links, navigate, view, clock, user: userEvent.setup() };
}

const shows = (text: string | RegExp): boolean => screen.queryByText(text) !== null;
const button = (name: string): HTMLElement | null => screen.queryByRole("button", { name });

beforeEach(() => {
  localStorage.clear();
});

describe("moves and connection", () => {
  const playing = room({ status: "Playing", seats: full, toMove: 0 });

  it("sends one move per position however many clicks, and again after a refusal or a new position", async () => {
    let refuse = true;
    const { links, user } = await at(playing, host, (s) =>
      s.makeMove.mockImplementation(() => (refuse ? Promise.reject(new Error("HubException: Siec padla.")) : new Promise(() => undefined))),
    );
    act(() => {
      last(links).emit.view(table(connectFour(), { moves: 4, legalMoves: ["0", "1"] }));
    });

    await user.click(screen.getByRole("button", { name: "Kolumna 1" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Siec padla.");
    refuse = false;
    await user.click(screen.getByRole("button", { name: "Kolumna 1" }));
    expect(screen.getByRole("button", { name: "Kolumna 2" })).toBeDisabled();
    expect(shows("Twój ruch!")).toBe(false);
    expect(screen.queryByRole("alert")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Kolumna 2" }));
    expect(last(links).server.makeMove.mock.calls).toEqual([
      [4, "0"],
      [4, "0"],
    ]);

    act(() => {
      last(links).emit.view(table(connectFour(), { version: 2, moves: 6, legalMoves: ["1"] }));
    });
    await user.click(screen.getByRole("button", { name: "Kolumna 2" }));
    expect(last(links).server.makeMove).toHaveBeenLastCalledWith(6, "1");
  });

  it("keeps the ticket when the network fails during a rejoin", async () => {
    const tickets = memoryTickets([host]);
    const { connect, links } = fakeConnect((s) => s.rejoin.mockRejectedValue(new Error("WebSocket closed")));
    render(<TablePage room={roomId} connect={connect} tickets={tickets} navigate={vi.fn()} />);

    expect(await screen.findByRole("alert")).toHaveTextContent("WebSocket closed");
    expect(tickets.find(roomId)).toEqual(host);
    expect(links[0]?.server.watch).not.toHaveBeenCalled();
  });

  it("becomes a spectator when the server no longer knows the ticket after a reconnect", async () => {
    let known = true;
    const { links } = await at(room({ seats: full }), host, (s) =>
      s.rejoin.mockImplementation((r, token) =>
        known ? Promise.resolve({ room: r, seat: 0, token }) : Promise.reject(new Error("HubException: Nieznany bilet.")),
      ),
    );
    expect(shows("oglądasz")).toBe(false);

    known = false;
    act(() => {
      last(links).emit.reconnected();
    });

    expect(await screen.findByText("oglądasz")).toBeInTheDocument();
    expect(button("Zacznij partię")).toBeNull();
  });

  it("clears an old error when the next action succeeds", async () => {
    let fail = true;
    const { user } = await at(room(), host, (s) =>
      s.start.mockImplementation(() => (fail ? Promise.reject(new Error("HubException: Za malo graczy.")) : Promise.resolve())),
    );

    await user.click(screen.getByRole("button", { name: "Zacznij partię" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Za malo graczy.");
    fail = false;
    await user.click(screen.getByRole("button", { name: "Zacznij partię" }));

    await waitFor(() => {
      expect(screen.queryByRole("alert")).toBeNull();
    });
  });

  it("names a move a bot made for a human only for humans", async () => {
    const { links } = await at(
      room({ status: "Playing", seats: [seat(0, { name: "Ala" }), seat(1, { kind: "Bot", name: "random" })] }),
      null,
    );
    act(() => {
      last(links).emit.move(played(1, "0", { seat: 1, byBot: true }));
    });

    expect(within(screen.getByRole("region", { name: "Ostatnie ruchy" })).getByRole("listitem")).toHaveTextContent(
      /^random: wrzuca do kolumny 1$/u,
    );
  });
});
