import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { league } from "../../test/labReports";
import { inkOn, PairMatrix } from "./PairMatrix";

describe("PairMatrix", () => {
  it("fills every cell from the row's point of view, with a dash on the diagonal", () => {
    render(<PairMatrix report={league()} />);

    const table = screen.getByRole("table", { name: "Kto bije kogo" });
    expect(
      within(table)
        .getAllByRole("columnheader")
        .map((h) => h.textContent),
    ).toEqual(["wiersz przeciw kolumnie", "greedy", "ismcts:50", "random"]);
    const rows = within(table).getAllByRole("row").slice(1);
    expect(rows.map((r) => r.textContent)).toEqual(["greedy—75,0%95,0%", "ismcts:5025,0%—brak partii", "random5,0%brak partii—"]);
    const cell = within(rows[0] as HTMLElement).getByText("75,0%");
    expect(cell).toHaveAttribute("title", "greedy przeciw ismcts:50");
    expect(cell).toHaveClass("cell ink-dark");
    expect(cell.style.background).toContain("var(--win) 50%");
    expect(within(rows[0] as HTMLElement).getByText("—")).toHaveClass("empty");
    expect(screen.getByText(/40 partii na parę/u)).toBeInTheDocument();
  });

  it("lets a column header break only after the colon of a bot name", () => {
    render(<PairMatrix report={league()} />);

    const headers = within(screen.getByRole("table", { name: "Kto bije kogo" })).getAllByRole("columnheader");
    expect(headers.map((h) => h.innerHTML)).toEqual([expect.any(String) as string, "greedy", "ismcts:<wbr>50", "random"]);
  });

  it("says how many games each pair played, in the right Polish form", () => {
    const { rerender } = render(<PairMatrix report={league({ gamesPerPair: 1 })} />);
    expect(screen.getByText(/\(1 partia na parę, remis = pół\)/u)).toBeInTheDocument();

    rerender(<PairMatrix report={league({ gamesPerPair: 4 })} />);
    expect(screen.getByText(/\(4 partie na parę, remis = pół\)/u)).toBeInTheDocument();
  });

  it("switches to dark ink exactly where the color gets strong", () => {
    expect(inkOn(0.325)).toBe("dark");
    expect(inkOn(0.5)).toBe("theme");
  });
});
