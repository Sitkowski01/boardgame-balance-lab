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
    players: ["Ala", "random"],
    status: "Finished",
    moves: ["3", "4", "3"],
    movers: [0, 1, 0],
    scores: [1, 0],
    verified: true,
    problem: null,
    ...patch,
  };
}

/** Klatka po n ruchach: n krazkow w dolnym wierszu, zeby bylo widac, ktora pozycja jest na ekranie. */
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
      pace={10}
      {...patch}
    />,
  );
  return { loadFrame, navigate, user: userEvent.setup() };
}

const discs = (): number => screen.queryAllByRole("img", { name: /Ala|Losowy/u }).length;
const position = (): string | null => screen.getByLabelText("Pozycja").textContent;

describe("ReplayPage", () => {
  it("starts at the deal and steps through the moves", async () => {
    const { loadFrame, user } = page();

    expect(screen.getByText("Wczytywanie partii…")).toBeInTheDocument();
    expect(await screen.findByRole("heading", { name: "Powtórka: Czwórki" })).toBeInTheDocument();
    expect(screen.getByText("Ala vs Losowy")).toBeInTheDocument();
    expect(screen.getByText("sprawdzona z dziennika")).toBeInTheDocument();
    await waitFor(() => {
      expect(position()).toBe("ruch 0 z 3");
    });
    expect(screen.getByRole("button", { name: "⏮ Początek" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "‹ Poprzedni" })).toBeDisabled();

    await user.click(screen.getByRole("button", { name: "Następny ›" }));
    await waitFor(() => {
      expect(discs()).toBe(1);
    });
    // Powtorke sie oglada: zadna kolumna planszy nie przyjmuje ruchu.
    const columns = screen.getAllByRole("button", { name: /^Kolumna \d$/u });
    expect(columns).toHaveLength(7);
    for (const column of columns) expect(column).toBeDisabled();
    await user.click(screen.getByRole("button", { name: "Koniec ⏭" }));
    await waitFor(() => {
      expect(discs()).toBe(3);
    });
    expect(screen.getByText("Wygrywa: Ala")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Następny ›" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "▶ Odtwarzaj" })).toBeDisabled();
    await user.click(screen.getByRole("button", { name: "‹ Poprzedni" }));
    await waitFor(() => {
      expect(position()).toBe("ruch 2 z 3");
    });
    await user.click(screen.getByRole("button", { name: "⏮ Początek" }));
    await waitFor(() => {
      expect(discs()).toBe(0);
    });

    expect(loadFrame.mock.calls.filter(([, n]) => n === 1)).toHaveLength(1);
  });

  it("jumps with the slider and with the move list", async () => {
    const { user } = page();
    const slider = await screen.findByRole("slider", { name: "Ruch" });

    fireEvent.change(slider, { target: { value: "2" } });
    await waitFor(() => {
      expect(discs()).toBe(2);
    });
    const list = screen.getByRole("list", { name: "Ruchy partii" });
    expect(
      within(list)
        .getAllByRole("button")
        .map((b) => b.textContent),
    ).toEqual(["Ala: wrzuca do kolumny 4", "Losowy: wrzuca do kolumny 5", "Ala: wrzuca do kolumny 4"]);
    expect(within(list).getAllByRole("listitem")[1]).toHaveClass("current");
    await user.click(within(list).getAllByRole("button")[0] as HTMLElement);
    await waitFor(() => {
      expect(position()).toBe("ruch 1 z 3");
    });
  });

  it("plays to the end by itself and can be paused", async () => {
    const { user } = page({ pace: 30 });
    await user.click(await screen.findByRole("button", { name: "▶ Odtwarzaj" }));
    expect(screen.getByRole("button", { name: "⏸ Pauza" })).toBeInTheDocument();

    await waitFor(() => {
      expect(position()).toBe("ruch 3 z 3");
    });
    expect(screen.getByRole("button", { name: "▶ Odtwarzaj" })).toBeDisabled();

    await user.click(screen.getByRole("button", { name: "⏮ Początek" }));
    await user.click(screen.getByRole("button", { name: "▶ Odtwarzaj" }));
    await user.click(screen.getByRole("button", { name: "⏸ Pauza" }));
    const paused = position();
    await act(() => new Promise((r) => setTimeout(r, 100)));
    expect(position()).toBe(paused);
  });

  it("marks a running match and a journal that does not match the rules", async () => {
    page({
      loadMatch: () =>
        Promise.resolve(
          replay({ status: "InProgress", movers: [], scores: [], verified: false, problem: "Ruch 2 nie zgadza sie z dziennikiem." }),
        ),
    });

    expect(await screen.findByText("partia trwa")).toBeInTheDocument();
    expect(screen.getByText("Ruch 2 nie zgadza sie z dziennikiem.")).toBeInTheDocument();
    expect(screen.queryByText("sprawdzona z dziennika")).not.toBeInTheDocument();
    // Bez sprawdzonego dziennika nie wiadomo, kto zagral — same ruchy.
    expect(
      within(screen.getByRole("list", { name: "Ruchy partii" }))
        .getAllByRole("button")
        .map((b) => b.textContent),
    ).toEqual(["wrzuca do kolumny 4", "wrzuca do kolumny 5", "wrzuca do kolumny 4"]);
  });

  it("shows why the match or a frame could not be loaded", async () => {
    const { navigate, user } = page({ loadMatch: () => Promise.reject(new Error("Nie ma takiej strony.")) });
    expect(await screen.findByRole("alert")).toHaveTextContent("Nie ma takiej strony.");
    await user.click(screen.getByRole("link", { name: "← Lobby" }));
    expect(navigate).toHaveBeenCalledWith("/");
  });

  it("shows a frame error and a non-Error rejection", async () => {
    page({ loadFrame: () => (Promise.reject as (reason: unknown) => Promise<never>)("zly dziennik") });

    expect(await screen.findByRole("alert")).toHaveTextContent("zly dziennik");
  });

  it("shows a match error given as text", async () => {
    page({ loadMatch: () => (Promise.reject as (reason: unknown) => Promise<never>)("brak bazy") });

    expect(await screen.findByRole("alert")).toHaveTextContent("brak bazy");
  });

  it("goes back to the lobby", async () => {
    const { navigate, user } = page();
    await user.click(await screen.findByRole("link", { name: "← Lobby" }));

    expect(navigate).toHaveBeenCalledWith("/");
  });
});
