import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import type { MatchReplay, ReplayFrame } from "../api/schemas";
import { matchId } from "../test/fixtures";
import { connectFour } from "../test/boards";
import { ReplayPage, type ReplayPageProps } from "./ReplayPage";

function replay(patch: Partial<MatchReplay> = {}): MatchReplay {
  return {
    id: matchId,
    game: "connectfour",
    gameName: "ConnectFour",
    variant: null,
    players: ["Ala", "Ola"],
    status: "Finished",
    moves: ["0", "1", "2"],
    movers: [0, 1, 0],
    scores: [0.5, 0.5],
    verified: true,
    problem: null,
    ...patch,
  };
}

function frame(moves: number): ReplayFrame {
  const rows = connectFour().rows.map((row, r) => (r === 5 ? row.map((_, c) => (c < moves ? c % 2 : null)) : row));
  return { match: matchId, moves, view: connectFour({ rows }) };
}

function page(patch: Partial<ReplayPageProps> = {}) {
  const loadFrame = vi.fn((_: string, moves: number) => Promise.resolve(frame(moves)));
  const navigate = vi.fn<(path: string) => void>();
  render(
    <ReplayPage
      match={matchId}
      navigate={navigate}
      loadMatch={() => Promise.resolve(replay())}
      loadFrame={loadFrame}
      pace={20}
      settle={0}
      {...patch}
    />,
  );
  return { loadFrame, navigate, user: userEvent.setup() };
}

const discs = (): number => screen.queryAllByRole("img", { name: /^(Ala|Ola)$/u }).length;
const slider = (): HTMLElement => screen.getByRole("slider", { name: "Ruch" });

describe("replay details", () => {
  it("names the winners only at the end and marks the current move", async () => {
    const { user } = page();
    await screen.findByRole("heading", { name: "Powtórka: Czwórki" });

    expect(screen.queryByText(/Wygrywa/u)).toBeNull();
    expect(screen.queryByText("partia trwa")).toBeNull();
    expect(screen.queryByRole("alert")).toBeNull();
    await user.click(screen.getByRole("button", { name: "Koniec ⏭" }));

    expect(screen.getByText("Wygrywa: Ala, Ola")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Koniec ⏭" })).toBeDisabled();
    const items = within(screen.getByRole("list", { name: "Ruchy partii" })).getAllByRole("listitem");
    expect(items.map((li) => li.className)).toEqual(["", "", "current"]);
  });

  it("shows no problem for a verified match or one without a message", async () => {
    page({ loadMatch: () => Promise.resolve(replay({ verified: false, problem: null })) });
    await screen.findByRole("heading", { name: "Powtórka: Czwórki" });

    expect(document.querySelector(".banner")).toBeNull();
  });

  it("does not let a late frame of an old position replace the current one", async () => {
    let late: (frame: ReplayFrame) => void = () => undefined;
    const loadFrame = vi.fn((_: string, moves: number) =>
      moves === 1 ? new Promise<ReplayFrame>((r) => (late = r)) : Promise.resolve(frame(moves)),
    );
    page({ loadFrame });
    await screen.findByRole("heading", { name: "Powtórka: Czwórki" });

    fireEvent.change(slider(), { target: { value: "1" } });
    fireEvent.change(slider(), { target: { value: "3" } });
    await waitFor(() => {
      expect(discs()).toBe(3);
    });
    await act(async () => {
      late(frame(1));
      await Promise.resolve();
    });

    expect(discs()).toBe(3);
  });

  it("waits until the slider stops before asking for a frame", async () => {
    const { loadFrame } = page({ settle: 60 });
    await screen.findByRole("heading", { name: "Powtórka: Czwórki" });
    await waitFor(() => {
      expect(loadFrame).toHaveBeenCalledWith(matchId, 0);
    });
    loadFrame.mockClear();

    fireEvent.change(slider(), { target: { value: "2" } });
    fireEvent.change(slider(), { target: { value: "3" } });
    await waitFor(() => {
      expect(discs()).toBe(3);
    });

    // Pierwsze pytanie jest o pozycje, na ktorej suwak stanal; 2 przychodzi dopiero jako sasiad do zapamietania.
    expect(loadFrame.mock.calls[0]?.[1]).toBe(3);
  });

  it("prefetches the next position after showing one", async () => {
    const { loadFrame } = page();
    await screen.findByRole("heading", { name: "Powtórka: Czwórki" });

    await waitFor(() => {
      expect(loadFrame.mock.calls.map(([, n]) => n)).toEqual([0, 1]);
    });
  });

  it("keeps the replay on screen when a frame fails and recovers on the next one", async () => {
    const loadFrame = vi.fn((_: string, moves: number) =>
      moves === 2 ? Promise.reject(new Error("Serwer odpowiedzial 429.")) : Promise.resolve(frame(moves)),
    );
    page({ loadFrame });
    await screen.findByRole("heading", { name: "Powtórka: Czwórki" });

    fireEvent.change(slider(), { target: { value: "2" } });
    expect(await screen.findByRole("alert")).toHaveTextContent("Nie udało się pobrać pozycji: Serwer odpowiedzial 429.");
    expect(screen.getByRole("heading", { name: "Powtórka: Czwórki" })).toBeInTheDocument();

    fireEvent.change(slider(), { target: { value: "3" } });
    await waitFor(() => {
      expect(screen.queryByRole("alert")).toBeNull();
    });
  });

  it("stops moving as soon as it is paused", async () => {
    const { user } = page({ pace: 80 });
    await screen.findByRole("heading", { name: "Powtórka: Czwórki" });

    await user.click(screen.getByRole("button", { name: "▶ Odtwarzaj" }));
    await user.click(screen.getByRole("button", { name: "⏸ Pauza" }));
    // Pod obciazeniem jeden krok moze zdazyc przed pauza; potem pozycja ma juz stac.
    const paused = screen.getByLabelText("Pozycja").textContent;
    await act(() => new Promise((r) => setTimeout(r, 250)));

    expect(screen.getByLabelText("Pozycja")).toHaveTextContent(paused);
    expect(screen.getByRole("button", { name: "▶ Odtwarzaj" })).toBeEnabled();
  });

  it("goes to the lobby without reloading", async () => {
    const { navigate } = page();

    expect(fireEvent.click(await screen.findByRole("link", { name: "← Lobby" }))).toBe(false);
    expect(navigate).toHaveBeenCalledWith("/");
  });
});
