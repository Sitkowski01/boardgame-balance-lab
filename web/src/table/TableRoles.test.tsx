import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { RoomInfo, SeatTicket } from "../api/schemas";
import { fakeConnect, last, memoryTickets, type FakeLink } from "../test/fakeServer";
import { matchId, room, roomId, seat, table } from "../test/fixtures";
import { connectFour } from "../test/boards";
import { TablePage } from "./TablePage";

const start = Date.parse("2026-10-19T10:00:00Z");
const host: SeatTicket = { room: roomId, seat: 0, token: "host" };
const guest: SeatTicket = { room: roomId, seat: 1, token: "guest" };
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

describe("what each person sees at the table", () => {
  it("host of a waiting room: start, bots for empty seats, no waiting note and no join form", async () => {
    await at(
      room({ seats: [seat(0, { name: "Ala" }), seat(1, { kind: "Empty", name: null }), seat(2, { kind: "Bot", name: "random" })] }),
      host,
    );

    expect(button("Zacznij partię")).not.toBeNull();
    expect(screen.getAllByRole("button", { name: "Dodaj bota" })).toHaveLength(1);
    expect(shows(/Czekamy/u)).toBe(false);
    expect(screen.queryByLabelText("Twoje imię")).toBeNull();
    expect(shows("oglądasz")).toBe(false);
  });

  it("seated guest of a waiting room: a waiting note only", async () => {
    await at(room({ seats: full }), guest);

    expect(shows("Czekamy, aż gospodarz zacznie partię.")).toBe(true);
    expect(button("Zacznij partię")).toBeNull();
    expect(button("Dodaj bota")).toBeNull();
  });

  it("spectator of a full waiting room: nothing to join", async () => {
    await at(room({ seats: full }), null);

    expect(screen.queryByLabelText("Twoje imię")).toBeNull();
    expect(shows(/Czekamy/u)).toBe(false);
  });

  it("game in progress: clock and board, no lobby controls and no result", async () => {
    const { links } = await at(room({ status: "Playing", seats: full, toMove: 1, deadline: "2026-10-19T10:00:30Z" }), host);
    act(() => {
      last(links).emit.view(table(connectFour()));
    });

    expect(screen.getByLabelText("Czas na ruch")).toHaveTextContent("30 s");
    expect(button("Zacznij partię")).toBeNull();
    expect(button("Dodaj bota")).toBeNull();
    expect(screen.queryByLabelText("Twoje imię")).toBeNull();
    expect(screen.queryByRole("region", { name: "Wynik" })).toBeNull();
    expect(screen.queryByRole("region", { name: "Ostatnie ruchy" })).toBeNull();
    expect(shows("Twój ruch!")).toBe(false);
    expect(screen.getByText("w grze")).toHaveClass("status", "Playing");
    expect(
      within(screen.getByRole("list", { name: "Miejsca" }))
        .getAllByRole("listitem")
        .map((li) => li.classList.contains("to-move")),
    ).toEqual([false, true]);
  });

  it("the clock counts down", async () => {
    const { clock } = await at(room({ status: "Playing", seats: full, toMove: 0, deadline: "2026-10-19T10:00:30Z" }), host);

    clock.advance(10_000);

    await waitFor(() => {
      expect(screen.getByLabelText("Czas na ruch")).toHaveTextContent("20 s");
    });
  });

  it("badges: bot, host, disconnected human and autopilot", async () => {
    await at(
      room({
        status: "Playing",
        toMove: 0,
        seats: [
          seat(0, { name: "Ala" }),
          seat(1, { kind: "Bot", name: "random", connected: false }),
          seat(2, { name: "Ola", connected: false, autopilot: true }),
        ],
      }),
      guest,
    );

    const [ala, bot, ola] = within(screen.getByRole("list", { name: "Miejsca" })).getAllByRole("listitem") as [
      HTMLElement,
      HTMLElement,
      HTMLElement,
    ];
    // Na ruchu jest Ala: znak slowem, nie tylko kolorem ramki.
    expect(ala).toHaveTextContent(/^AAlana ruchugospodarz$/u);
    expect(ala).toHaveClass("to-move");
    expect(bot).toHaveTextContent(/^⚙Losowybot$/u);
    expect(ola).toHaveTextContent(/^OOlarozłączonygra za niego bot$/u);
  });

  it("no connection banner once online", async () => {
    const { view } = await at(room(), null);

    expect(view.container.querySelector(".banner")).toBeNull();
  });

  it("result: every winner, unnamed seats and no replay link without a match", async () => {
    await at(
      room({ status: "Finished", seats: [seat(0, { name: "Ala" }), seat(1, { kind: "Empty", name: null })], scores: [0.5, 0.5] }),
      null,
    );

    expect(screen.getByText("Wygrywa: Ala, Miejsce 2")).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Obejrzyj powtórkę" })).toBeNull();
    expect(screen.queryByLabelText("Czas na ruch")).toBeNull();
  });

  it("abandoned game without scores names nobody", async () => {
    await at(room({ status: "Abandoned", seats: full }), null);

    expect(shows(/Wygrywa/u)).toBe(false);
  });

  it("links change the page without reloading it", async () => {
    const { navigate } = await at(room({ status: "Finished", seats: full, matchId }), null);

    expect(fireEvent.click(screen.getByRole("link", { name: "Obejrzyj powtórkę" }))).toBe(false);
    expect(fireEvent.click(screen.getByRole("link", { name: "← Lobby" }))).toBe(false);
    expect(navigate.mock.calls).toEqual([[`/matches/${matchId}`], ["/"]]);
  });

  it("a name of spaces cannot sit down", async () => {
    const { user } = await at(room(), null);

    await user.type(screen.getByLabelText("Twoje imię"), "   ");

    expect(button("Usiądź przy stole")).toBeDisabled();
  });
});
