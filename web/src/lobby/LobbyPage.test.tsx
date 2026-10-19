import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { GameInfo } from "../api/schemas";
import { fakeConnect, last, memoryTickets, type FakeLink } from "../test/fakeServer";
import { matchId, room, roomId, seat } from "../test/fixtures";
import { LobbyPage } from "./LobbyPage";

const games: GameInfo[] = [
  { key: "connectfour", name: "ConnectFour", minPlayers: 2, maxPlayers: 2, hasVariants: false },
  { key: "klejnoty", name: "Klejnoty", minPlayers: 2, maxPlayers: 4, hasVariants: true },
];

function page(setup?: (server: FakeLink["server"]) => void, loadGames = () => Promise.resolve(games)) {
  const tickets = memoryTickets();
  const { connect, links } = fakeConnect(setup);
  const navigate = vi.fn<(path: string) => void>();
  const view = render(<LobbyPage connect={connect} tickets={tickets} navigate={navigate} loadGames={loadGames} />);
  return { tickets, links, navigate, view, user: userEvent.setup() };
}

beforeEach(() => {
  localStorage.clear();
});

describe("LobbyPage", () => {
  it("lists rooms from the server and their later changes", async () => {
    const { links } = page((s) =>
      s.watchLobby.mockResolvedValue([room({ id: "a", seats: [seat(0, { name: "Ala" }), seat(1, { kind: "Empty", name: null })] })]),
    );

    const row = await screen.findByRole("listitem", { name: "Pokój ConnectFour" });
    expect(row).toHaveTextContent("Ala · 1/2");
    expect(within(row).getByRole("link", { name: "Dołącz" })).toHaveAttribute("href", "/rooms/a");

    act(() => {
      last(links).emit.lobby(room({ id: "a", version: 2, status: "Playing", seats: [seat(0), seat(1)] }));
      last(links).emit.lobby(room({ id: "b", gameName: "Klejnoty", variant: "win=9", status: "Finished", matchId }));
    });

    expect(within(screen.getByRole("listitem", { name: "Pokój ConnectFour" })).getByRole("link", { name: "Oglądaj" })).toBeInTheDocument();
    const done = screen.getByRole("listitem", { name: "Pokój Klejnoty" });
    expect(done).toHaveTextContent("(win=9)");
    expect(within(done).getByRole("link", { name: "Powtórka" })).toHaveAttribute("href", `/matches/${matchId}`);
    expect(within(done).queryByRole("link", { name: "Oglądaj" })).not.toBeInTheDocument();
  });

  it("says when there are no rooms and goes to a room by link", async () => {
    const { links, navigate, user } = page();
    expect(await screen.findByText("Nie ma jeszcze żadnego pokoju — załóż pierwszy.")).toBeInTheDocument();

    act(() => {
      last(links).emit.lobby(room({ id: "a" }));
    });
    await user.click(screen.getByRole("link", { name: "Dołącz" }));

    expect(navigate).toHaveBeenCalledWith("/rooms/a");
  });

  it("creates a room with a variant, keeps the host ticket and opens the table", async () => {
    const { links, tickets, navigate, user } = page();
    const form = await screen.findByRole("form", { name: "Nowy pokój" });

    expect(within(form).getByRole("button", { name: "Załóż pokój" })).toBeDisabled();
    expect(within(form).queryByLabelText("Wariant zasad")).not.toBeInTheDocument();
    await user.type(within(form).getByLabelText("Twoje imię"), "Ala");
    await user.selectOptions(within(form).getByLabelText("Gra"), "klejnoty");
    expect(
      within(form)
        .getAllByRole("option", { name: /^[0-9]$/u })
        .map((o) => o.textContent),
    ).toEqual(["2", "3", "4"]);
    await user.selectOptions(within(form).getByLabelText("Miejsca"), "3");
    await user.type(within(form).getByLabelText("Wariant zasad"), " win=9 ");
    await user.click(within(form).getByRole("button", { name: "Załóż pokój" }));

    await waitFor(() => {
      expect(navigate).toHaveBeenCalledWith(`/rooms/${roomId}`);
    });
    const creator = last(links);
    expect(creator.server.createRoom).toHaveBeenCalledWith({ game: "klejnoty", seats: 3, variant: "win=9", name: "Ala" });
    expect(creator.server.close).toHaveBeenCalled();
    expect(tickets.find(roomId)?.token).toBe("host");
    expect(localStorage.getItem("balance-lab:name")).toBe("Ala");
  });

  it("sends no variant for an empty field or a game without variants", async () => {
    const { links, user } = page();
    const form = await screen.findByRole("form", { name: "Nowy pokój" });
    await user.type(within(form).getByLabelText("Twoje imię"), "Ala");
    await user.click(within(form).getByRole("button", { name: "Załóż pokój" }));
    await waitFor(() => {
      expect(last(links).server.createRoom).toHaveBeenCalledWith({ game: "connectfour", seats: 2, variant: null, name: "Ala" });
    });

    await user.selectOptions(within(form).getByLabelText("Gra"), "klejnoty");
    await user.type(within(form).getByLabelText("Wariant zasad"), "   ");
    await user.selectOptions(within(form).getByLabelText("Miejsca"), "4");
    await user.selectOptions(within(form).getByLabelText("Gra"), "connectfour");
    await user.click(within(form).getByRole("button", { name: "Załóż pokój" }));
    await waitFor(() => {
      expect(last(links).server.createRoom).toHaveBeenLastCalledWith({ game: "connectfour", seats: 2, variant: null, name: "Ala" });
    });
  });

  it("shows why a room could not be created and still closes the connection", async () => {
    const { links, navigate, user } = page((s) => s.createRoom.mockRejectedValue(new Error("HubException: Zly wariant.")));
    const form = await screen.findByRole("form", { name: "Nowy pokój" });
    await user.type(within(form).getByLabelText("Twoje imię"), "Ala");
    await user.click(within(form).getByRole("button", { name: "Załóż pokój" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Zly wariant.");
    expect(last(links).server.close).toHaveBeenCalled();
    expect(navigate).not.toHaveBeenCalled();
  });

  it("reports a lobby that cannot be loaded", async () => {
    page(
      (s) => s.watchLobby.mockRejectedValue(new Error("Serwer padl")),
      () => Promise.resolve([]),
    );

    expect(await screen.findByRole("alert")).toHaveTextContent("Serwer padl");
    expect(screen.queryByRole("form", { name: "Nowy pokój" })).not.toBeInTheDocument();
  });

  it("reports a game list that cannot be loaded", async () => {
    page(undefined, () => Promise.reject(new Error("Brak gier")));

    expect(await screen.findByRole("alert")).toHaveTextContent("Brak gier");
  });

  it("closes the lobby connection when leaving and ignores late answers", async () => {
    let answer: (rooms: ReturnType<typeof room>[]) => void = () => undefined;
    const { links, view } = page((s) => s.watchLobby.mockReturnValue(new Promise((r) => (answer = r))));
    await waitFor(() => {
      expect(last(links).server.watchLobby).toHaveBeenCalled();
    });

    view.unmount();
    answer([room()]);
    last(links).emit.lobby(room());

    expect(last(links).server.close).toHaveBeenCalled();
  });

  it("closes a connection that opens after leaving", async () => {
    const { links, view } = page();
    view.unmount();
    await waitFor(() => {
      expect(links).toHaveLength(1);
    });

    await waitFor(() => {
      expect(last(links).server.close).toHaveBeenCalled();
    });
    expect(last(links).server.watchLobby).not.toHaveBeenCalled();
  });
});
