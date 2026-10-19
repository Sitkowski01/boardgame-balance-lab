import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { botsFor, describe as describeMove, GameBoard } from "./games";
import { connectFour, klejnoty, mozaika, mozaikaRules, sheet } from "./test/boards";

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

  it("loads the rules of the variant before drawing Mozaika and checks them", async () => {
    const loadSheet = vi.fn().mockResolvedValue(mozaikaRules());
    const { unmount } = render(<GameBoard {...common} game="mozaika" variant="column=5" view={mozaika()} loadSheet={loadSheet} />);

    expect(await screen.findByRole("region", { name: "Warsztaty" })).toBeInTheDocument();
    expect(loadSheet).toHaveBeenCalledWith("mozaika", "column=5");
    unmount();

    render(<GameBoard {...common} game="mozaika" view={mozaika()} loadSheet={() => Promise.resolve({ wall: [] })} />);
    expect(await screen.findByRole("alert")).toHaveTextContent("zasady gry, których ta wersja strony nie rozumie");
  });

  it("never draws a board with the rules of another game or variant", async () => {
    const loadSheet = vi.fn((game: string) => Promise.resolve(game === "mozaika" ? mozaikaRules() : sheet()));
    const { rerender } = render(<GameBoard {...common} game="klejnoty" view={klejnoty()} loadSheet={loadSheet} />);
    expect(await screen.findByRole("region", { name: "Rynek" })).toBeInTheDocument();

    rerender(<GameBoard {...common} game="mozaika" view={mozaika()} loadSheet={loadSheet} />);
    expect(screen.getByText("Wczytywanie zasad…")).toBeInTheDocument();
    expect(await screen.findByRole("region", { name: "Warsztaty" })).toBeInTheDocument();

    rerender(<GameBoard {...common} game="mozaika" variant="row=3" view={mozaika()} loadSheet={loadSheet} />);
    expect(screen.getByText("Wczytywanie zasad…")).toBeInTheDocument();
    expect(await screen.findByRole("region", { name: "Warsztaty" })).toBeInTheDocument();
    expect(loadSheet).toHaveBeenLastCalledWith("mozaika", "row=3");
  });

  it("refuses a Mozaika board of the wrong shape", async () => {
    render(<GameBoard {...common} game="mozaika" view={klejnoty()} loadSheet={() => Promise.resolve(mozaikaRules())} />);

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
    expect(botsFor("mozaika").map((b) => b.label)).toEqual(["Zachłanny (mocny)", "ISMCTS", "Losowy"]);
  });

  it("offers bots that the game understands", () => {
    expect(botsFor("klejnoty").map((b) => b.spec)).toEqual(["ismcts:300", "greedy", "random"]);
    expect(botsFor("connectfour").map((b) => b.spec)).toEqual(["mcts:500", "random"]);
    expect(botsFor("mozaika").map((b) => b.spec)).toEqual(["greedy", "ismcts:300", "random"]);
  });

  it("describes moves of each game and falls back to the raw move", () => {
    expect(describeMove("klejnoty", "pass")).toBe("pasuje");
    expect(describeMove("klejnoty", "buy:41", sheet().cards)).toBe("kupuje karte #41 (niebieski, 1 pkt)");
    expect(describeMove("connectfour", "6")).toBe("wrzuca do kolumny 7");
    expect(describeMove("klejnoty", "dziwny")).toBe("dziwny");
    expect(describeMove("mozaika", "f0:O:floor")).toBe("bierze ochra z warsztatu 1 na podłogę");
    expect(describeMove("mozaika", "dziwny")).toBe("dziwny");
  });
});
