import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { botsFor, describe as describeMove, GameBoard } from "./games";
import { connectFour, klejnoty, sheet } from "./test/boards";

describe("GameBoard", () => {
  const common = { variant: null, legalMoves: [], seat: 0, names: ["Ala", "Ola"], onMove: vi.fn() };

  it("draws Connect Four and refuses a board it does not understand", () => {
    const { rerender } = render(<GameBoard {...common} game="connectfour" view={connectFour()} />);
    expect(screen.getByRole("group", { name: "Plansza" })).toBeInTheDocument();

    rerender(<GameBoard {...common} game="connectfour" view={{ rows: "x" }} />);
    expect(screen.getByRole("alert")).toHaveTextContent("odśwież stronę");
  });

  it("loads the card table of the variant before drawing Klejnoty", async () => {
    const loadSheet = vi.fn().mockResolvedValue(sheet());
    render(<GameBoard {...common} game="klejnoty" variant="win=9" view={klejnoty()} loadSheet={loadSheet} />);

    expect(screen.getByText("Wczytywanie zasad…")).toBeInTheDocument();
    expect(await screen.findByRole("region", { name: "Rynek" })).toBeInTheDocument();
    expect(loadSheet).toHaveBeenCalledWith("klejnoty", "win=9");
  });

  it("says when the card table cannot be loaded or the board is broken", async () => {
    const { unmount } = render(
      <GameBoard {...common} game="klejnoty" view={klejnoty()} loadSheet={() => Promise.reject(new Error("Zly wariant."))} />,
    );
    expect(await screen.findByRole("alert")).toHaveTextContent("Zly wariant.");
    unmount();

    const other = render(<GameBoard {...common} game="klejnoty" view={klejnoty()} loadSheet={() => Promise.reject(new Error(""))} />);
    expect(await screen.findByRole("alert")).toHaveTextContent("Nie udało się pobrać zasad gry.");
    other.unmount();

    render(
      <GameBoard
        {...common}
        game="klejnoty"
        view={klejnoty()}
        loadSheet={() => Promise.reject(Object.assign(new Error(), { message: "" }))}
      />,
    );
    expect(await screen.findByRole("alert")).toHaveTextContent("Nie udało się pobrać zasad gry.");
  });

  it("refuses a Klejnoty board of the wrong shape", async () => {
    render(<GameBoard {...common} game="klejnoty" view={{}} loadSheet={() => Promise.resolve(sheet())} />);

    expect(await screen.findByRole("alert")).toHaveTextContent("odśwież stronę");
  });

  it("ignores a card table that arrives after leaving the board", async () => {
    let resolve: (value: ReturnType<typeof sheet>) => void = () => undefined;
    const { unmount } = render(
      <GameBoard {...common} game="klejnoty" view={klejnoty()} loadSheet={() => new Promise((r) => (resolve = r))} />,
    );
    unmount();
    resolve(sheet());
    await Promise.resolve();

    expect(screen.queryByRole("region", { name: "Rynek" })).not.toBeInTheDocument();
  });
});

describe("games", () => {
  it("names the bots for people", () => {
    expect(botsFor("klejnoty").map((b) => b.label)).toEqual(["ISMCTS (mocny)", "Zachłanny", "Losowy"]);
    expect(botsFor("connectfour").map((b) => b.label)).toEqual(["MCTS (mocny)", "Losowy"]);
  });

  it("offers bots that the game understands", () => {
    expect(botsFor("klejnoty").map((b) => b.spec)).toEqual(["ismcts:300", "greedy", "random"]);
    expect(botsFor("connectfour").map((b) => b.spec)).toEqual(["mcts:500", "random"]);
  });

  it("describes moves of each game and falls back to the raw move", () => {
    expect(describeMove("klejnoty", "pass")).toBe("pasuje");
    expect(describeMove("klejnoty", "buy:41", sheet().cards)).toBe("kupuje karte #41 (niebieski, 1 pkt)");
    expect(describeMove("connectfour", "6")).toBe("wrzuca do kolumny 7");
    expect(describeMove("klejnoty", "dziwny")).toBe("dziwny");
  });
});
