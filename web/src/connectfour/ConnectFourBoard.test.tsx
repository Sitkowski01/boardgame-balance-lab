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
