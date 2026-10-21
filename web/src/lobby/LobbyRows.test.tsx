import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { GameInfo, RoomInfo } from "../api/schemas";
import { fakeConnect, last, memoryTickets, type FakeLink } from "../test/fakeServer";
import { matchId, room, seat } from "../test/fixtures";
import { LobbyPage } from "./LobbyPage";

const games: GameInfo[] = [
  { key: "connectfour", name: "ConnectFour", minPlayers: 2, maxPlayers: 2, hasVariants: false, variantSyntax: null, profiles: [] },
  { key: "klejnoty", name: "Klejnoty", minPlayers: 2, maxPlayers: 4, hasVariants: true, variantSyntax: "win=N", profiles: ["silnik"] },
];
const loadGames = () => Promise.resolve(games);

async function lobby(rooms: RoomInfo[] = [], setup?: (server: FakeLink["server"]) => void) {
  const { connect, links } = fakeConnect((s) => {
    s.watchLobby.mockResolvedValue(rooms);
    setup?.(s);
  });
  const navigate = vi.fn<(path: string) => void>();
  render(<LobbyPage connect={connect} tickets={memoryTickets()} navigate={navigate} loadGames={loadGames} />);
  await screen.findByRole("form", { name: "Nowy pokój" });
  return { links, navigate, user: userEvent.setup() };
}

const row = (name: string): HTMLElement => screen.getByRole("listitem", { name });

beforeEach(() => {
  localStorage.clear();
});

describe("room rows", () => {
  it("offer the right way in for each stage", async () => {
    await lobby([
      room({ id: "full", gameName: "Pelny", seats: [seat(0, { name: "Ala" }), seat(1, { name: "Ola" })] }),
      room({ id: "play", gameName: "Gra", status: "Playing", matchId }),
      room({ id: "done", gameName: "Koniec", status: "Finished" }),
      room({ id: "gone", gameName: "Przerwana", status: "Abandoned", matchId }),
    ]);

    expect(within(row("Pokój Pelny")).queryByRole("link")).toBeNull();
    expect(
      within(row("Pokój Gra"))
        .getAllByRole("link")
        .map((a) => a.textContent),
    ).toEqual(["Oglądaj"]);
    expect(within(row("Pokój Koniec")).queryByRole("link")).toBeNull();
    expect(
      within(row("Pokój Przerwana"))
        .getAllByRole("link")
        .map((a) => a.textContent),
    ).toEqual(["Powtórka"]);
  });

  it("list the players with the status and no variant when there is none", async () => {
    await lobby([room({ id: "a", seats: [seat(0, { name: "Ala" }), seat(1, { name: "Ola" }), seat(2, { kind: "Empty", name: null })] })]);

    expect(row("Pokój Czwórki")).toHaveTextContent("Ala, Ola · 2/3");
    expect(row("Pokój Czwórki")).not.toHaveTextContent("(");
    expect(within(row("Pokój Czwórki")).getByText("czeka na graczy")).toHaveClass("status", "Waiting");
  });

  it("go to the replay without reloading the page", async () => {
    const { navigate } = await lobby([room({ id: "a", status: "Finished", matchId })]);

    expect(fireEvent.click(screen.getByRole("link", { name: "Powtórka" }))).toBe(false);
    expect(navigate).toHaveBeenCalledWith(`/matches/${matchId}`);
  });

  it("show no error before anything goes wrong", async () => {
    await lobby();

    expect(screen.queryByRole("alert")).toBeNull();
  });
});

describe("new room form", () => {
  it("takes every number of seats up to the maximum and sends no empty variant", async () => {
    const { links, user } = await lobby();
    const form = screen.getByRole("form", { name: "Nowy pokój" });
    await user.type(within(form).getByLabelText("Twoje imię"), " Ala ");
    await user.click(within(form).getByRole("radio", { name: "Klejnoty" }));
    await user.selectOptions(within(form).getByLabelText("Miejsca"), "4");
    await user.type(within(form).getByLabelText("Wariant zasad"), "  ");

    expect(fireEvent.submit(form)).toBe(false);

    await waitFor(() => {
      expect(last(links).server.createRoom).toHaveBeenCalledWith({ game: "klejnoty", seats: 4, variant: null, name: "Ala" });
    });
    expect(localStorage.getItem("balance-lab:name")).toBe("Ala");
  });

  it("keeps the smallest table as an explicit choice", async () => {
    const { links, user } = await lobby();
    const form = screen.getByRole("form", { name: "Nowy pokój" });
    await user.type(within(form).getByLabelText("Twoje imię"), "Ala");
    await user.click(within(form).getByRole("radio", { name: "Klejnoty" }));
    await user.selectOptions(within(form).getByLabelText("Miejsca"), "3");
    await user.selectOptions(within(form).getByLabelText("Miejsca"), "2");
    await user.click(within(form).getByRole("button", { name: "Załóż pokój" }));

    await waitFor(() => {
      expect(last(links).server.createRoom).toHaveBeenCalledWith({ game: "klejnoty", seats: 2, variant: null, name: "Ala" });
    });
  });

  it("does not accept a name of spaces", async () => {
    const { user } = await lobby();
    const form = screen.getByRole("form", { name: "Nowy pokój" });

    await user.type(within(form).getByLabelText("Twoje imię"), "   ");

    expect(within(form).getByRole("button", { name: "Załóż pokój" })).toBeDisabled();
  });

  it("clears an old error when creating works the next time", async () => {
    let fail = true;
    const { user } = await lobby([], (s) =>
      s.createRoom.mockImplementation(() =>
        fail ? Promise.reject(new Error("HubException: Za duzo pokoi.")) : Promise.resolve({ room: "r", seat: 0, token: "t" }),
      ),
    );
    const form = screen.getByRole("form", { name: "Nowy pokój" });
    await user.type(within(form).getByLabelText("Twoje imię"), "Ala");
    await user.click(within(form).getByRole("button", { name: "Załóż pokój" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Za duzo pokoi.");

    fail = false;
    await user.click(within(form).getByRole("button", { name: "Załóż pokój" }));

    await waitFor(() => {
      expect(screen.queryByRole("alert")).toBeNull();
    });
  });

  it("adds rooms announced while the list is still loading", async () => {
    let answer: (rooms: RoomInfo[]) => void = () => undefined;
    const { connect, links } = fakeConnect((s) => s.watchLobby.mockReturnValue(new Promise((r) => (answer = r))));
    render(<LobbyPage connect={connect} tickets={memoryTickets()} navigate={vi.fn()} loadGames={loadGames} />);
    await waitFor(() => {
      expect(last(links).server.watchLobby).toHaveBeenCalled();
    });

    act(() => {
      last(links).emit.lobby(room({ id: "new", gameName: "Nowy", version: 3 }));
    });
    await act(async () => {
      answer([room({ id: "new", gameName: "Stary", version: 1 }), room({ id: "other", gameName: "Inny" })]);
      await Promise.resolve();
    });

    expect(screen.getByRole("listitem", { name: "Pokój Nowy" })).toBeInTheDocument();
    expect(screen.getByRole("listitem", { name: "Pokój Inny" })).toBeInTheDocument();
    expect(screen.queryByRole("listitem", { name: "Pokój Stary" })).toBeNull();
  });
});
