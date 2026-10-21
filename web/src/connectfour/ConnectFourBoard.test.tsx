import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { connectFour } from "../test/boards";
import { ConnectFourBoard } from "./ConnectFourBoard";
import { connectFourView, describeColumn } from "./model";

describe("Connect Four model", () => {
  it("accepts the board the server sends and names a column", () => {
    expect(connectFourView.parse(connectFour())).toEqual(connectFour());
    expect(describeColumn("0")).toBe("wrzuca do kolumny 1");
  });
});

describe("ConnectFourBoard", () => {
  it("drops into a legal column and draws the discs", async () => {
    const rows = connectFour().rows.map((row, r) => (r === 5 ? [0, 1, null, null, null, null, null] : row));
    const onMove = vi.fn<(move: string) => void>();
    render(<ConnectFourBoard view={connectFour({ rows })} legalMoves={["0", "3"]} names={["Ala", "Bot"]} onMove={onMove} />);

    expect(screen.getByRole("button", { name: "Kolumna 2" })).toBeDisabled();
    await userEvent.click(screen.getByRole("button", { name: "Kolumna 4" }));

    expect(onMove).toHaveBeenCalledWith("3");
    expect(screen.getAllByRole("img", { name: "Ala" })).toHaveLength(1);
    expect(screen.getAllByRole("img", { name: "Bot" })).toHaveLength(1);
    expect(screen.getAllByRole("img", { name: "puste" })).toHaveLength(40);
  });

  it("names the winner or a draw", () => {
    const { rerender } = render(
      <ConnectFourBoard view={connectFour({ over: true, winner: 1 })} legalMoves={[]} names={["Ala", "Bot"]} onMove={vi.fn()} />,
    );
    expect(screen.getByText("Wygrywa Bot.")).toBeInTheDocument();

    rerender(<ConnectFourBoard view={connectFour({ over: true, winner: 1 })} legalMoves={[]} names={[]} onMove={vi.fn()} />);
    expect(screen.getByText("Wygrywa gracz 2.")).toBeInTheDocument();

    rerender(<ConnectFourBoard view={connectFour({ over: true })} legalMoves={[]} names={[]} onMove={vi.fn()} />);
    expect(screen.getByText("Remis.")).toBeInTheDocument();
  });

  it("names a disc of an unnamed player", () => {
    const rows = connectFour().rows.map((row, r) => (r === 5 ? [1, null, null, null, null, null, null] : row));
    render(<ConnectFourBoard view={connectFour({ rows })} legalMoves={[]} names={[]} onMove={vi.fn()} />);

    expect(screen.getByRole("img", { name: "gracz 2" })).toBeInTheDocument();
  });
});

describe("Connect Four drawing", () => {
  it("lays out the columns and colors discs by player", () => {
    const rows = connectFour().rows.map((row, r) => (r === 5 ? [0, 1, null, null, null, null, null] : row));
    render(<ConnectFourBoard view={connectFour({ rows })} legalMoves={[]} names={["Ala", "Ola"]} onMove={vi.fn()} />);

    expect(screen.getByRole("group", { name: "Plansza" })).toHaveStyle({ gridTemplateColumns: "repeat(7, 1fr)" });
    expect(screen.getByRole("img", { name: "Ala" })).toHaveClass("cell", "p0");
    expect(screen.getByRole("img", { name: "Ola" })).toHaveClass("cell", "p1");
    expect(screen.getAllByRole("img", { name: "puste" })[0]).toHaveAttribute("class", "cell");
    expect(fireEvent.click(screen.getByRole("button", { name: "Kolumna 1" }))).toBe(true);
  });
});

describe("Connect Four last move and win", () => {
  const bottom = (cells: (number | null)[]) => connectFour().rows.map((row, r) => (r === 5 ? cells : row));

  it("rings the disc that has just fallen and drops it from its row", () => {
    const first = connectFour({ rows: bottom([0, null, null, null, null, null, null]) });
    const { rerender } = render(<ConnectFourBoard view={first} legalMoves={[]} names={["Ala", "Ola"]} onMove={vi.fn()} />);
    expect(screen.getByRole("img", { name: "Ala" })).not.toHaveClass("last");

    rerender(
      <ConnectFourBoard
        view={connectFour({ rows: bottom([0, 1, null, null, null, null, null]), toMove: 1 })}
        legalMoves={[]}
        names={["Ala", "Ola"]}
        onMove={vi.fn()}
      />,
    );
    expect(screen.getByRole("img", { name: "Ola" })).toHaveClass("last");
    expect(screen.getByRole("img", { name: "Ola" })).toHaveStyle({ "--drop": "6" });
    expect(screen.getByRole("img", { name: "Ala" })).not.toHaveClass("last");
    expect(screen.getByRole("img", { name: "Ala" }).getAttribute("style")).toBeNull();
    expect(screen.getByRole("group", { name: "Plansza" }).parentElement?.parentElement).toHaveClass("connectfour", "turn-p1");
  });

  it("lights up the winning four only when the game is won", () => {
    const rows = bottom([0, 0, 0, 0, 1, 1, 1]);
    const { rerender } = render(<ConnectFourBoard view={connectFour({ rows })} legalMoves={[]} names={["Ala", "Ola"]} onMove={vi.fn()} />);
    expect(document.querySelectorAll(".win")).toHaveLength(0);

    rerender(
      <ConnectFourBoard view={connectFour({ rows, over: true, winner: 0 })} legalMoves={[]} names={["Ala", "Ola"]} onMove={vi.fn()} />,
    );
    expect(screen.getAllByRole("img", { name: "Ala" }).every((d) => d.classList.contains("win"))).toBe(true);
    expect(screen.getAllByRole("img", { name: "Ola" }).some((d) => d.classList.contains("win"))).toBe(false);

    rerender(<ConnectFourBoard view={connectFour({ rows, over: true })} legalMoves={[]} names={["Ala", "Ola"]} onMove={vi.fn()} />);
    expect(document.querySelectorAll(".win")).toHaveLength(0);
  });

  it("names the colors of both players under the board", () => {
    render(<ConnectFourBoard view={connectFour()} legalMoves={[]} names={["Ala"]} onMove={vi.fn()} />);
    expect(screen.getByText(/Ala/u).closest("p")).toHaveTextContent("Ala gracz 2");
  });
});

describe("Connect Four last move across renders", () => {
  it("keeps the ring when the same position arrives again as new arrays", () => {
    const rows = (cells: (number | null)[]) => connectFour().rows.map((row, r) => (r === 5 ? [...cells] : [...row]));
    const props = { legalMoves: [], names: ["Ala", "Ola"], onMove: vi.fn() };
    const { rerender } = render(
      <ConnectFourBoard view={connectFour({ rows: rows([0, null, null, null, null, null, null]) })} {...props} />,
    );
    rerender(<ConnectFourBoard view={connectFour({ rows: rows([0, 1, null, null, null, null, null]) })} {...props} />);
    rerender(<ConnectFourBoard view={connectFour({ rows: rows([0, 1, null, null, null, null, null]) })} {...props} />);
    expect(screen.getByRole("img", { name: "Ola" })).toHaveClass("last");
  });
});
