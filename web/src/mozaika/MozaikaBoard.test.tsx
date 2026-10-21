import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { mozaika, mozaikaMoves, mozaikaPlayer, mozaikaRules } from "../test/boards";
import { MozaikaBoard, type MozaikaBoardProps } from "./MozaikaBoard";

function board(patch: Partial<MozaikaBoardProps> = {}) {
  const onMove = vi.fn<(move: string) => void>();
  const props = { view: mozaika(), sheet: mozaikaRules(), legalMoves: [], seat: 0, names: ["Ala", "Ola"], onMove, ...patch };
  const result = render(<MozaikaBoard {...props} />);
  return {
    onMove,
    user: userEvent.setup(),
    rerender: (next: Partial<MozaikaBoardProps>) => {
      result.rerender(<MozaikaBoard {...props} {...next} />);
    },
  };
}

const tile = (name: string): HTMLElement => screen.getByRole("button", { name: new RegExp(`^${name}`, "u") });
const ala = (): HTMLElement => screen.getByLabelText("Gracz Ala");

describe("MozaikaBoard", () => {
  it("shows the factories, the center with the marker, the round and both players", () => {
    board({ view: mozaika({ players: [mozaikaPlayer({ score: 12 }), mozaikaPlayer()], center: [0, 0, 3, 0, 0], round: 3 }) });

    expect(within(screen.getByRole("region", { name: "Warsztaty" })).getAllByRole("group")).toHaveLength(6);
    expect(tile("Warsztat 2: karmin \\(4\\)")).toBeDisabled();
    expect(tile("Środek: karmin \\(3\\)")).toBeInTheDocument();
    expect(within(screen.getByRole("group", { name: "Środek" })).getByLabelText("Znacznik pierwszego gracza")).toBeInTheDocument();
    expect(screen.getByLabelText("Runda")).toHaveTextContent("Runda 3 · w worku 80 · w pudełku 0");
    expect(within(ala()).getByLabelText("Punkty")).toHaveTextContent("12 pkt");
    expect(within(ala()).getByText("ty")).toBeInTheDocument();
    expect(within(ala()).getByText("na ruchu")).toBeInTheDocument();
    expect(within(screen.getByLabelText("Gracz Ola")).queryByText("na ruchu")).not.toBeInTheDocument();
    expect(screen.getByText("Premie na koniec: rząd +2, kolumna +7, kolor +10.")).toBeInTheDocument();
  });

  it("draws the wall pattern with placed tiles and the pattern lines", () => {
    const me = mozaikaPlayer({
      wall: [0, 6],
      lines: [
        { color: null, count: 0 },
        { color: 2, count: 1 },
        { color: null, count: 0 },
        { color: null, count: 0 },
        { color: 4, count: 5 },
      ],
    });
    board({ view: mozaika({ players: [me, mozaikaPlayer()] }) });

    const cells = within(within(ala()).getByRole("grid", { name: "Ściana" })).getAllByRole("gridcell");
    expect(cells).toHaveLength(25);
    expect(cells.filter((c) => c.getAttribute("aria-selected") === "true").map((c) => cells.indexOf(c))).toEqual([0, 6]);
    expect(within(cells[1] ?? document.body).getByRole("img")).toHaveAccessibleName("ochra");
    expect(within(cells[5] ?? document.body).getByRole("img")).toHaveAccessibleName("perła");
    const lines = within(ala()).getAllByRole("listitem");
    expect(lines.map((l) => l.getAttribute("aria-label"))).toEqual([
      "Rząd 1: pusty",
      "Rząd 2: karmin 1/2",
      "Rząd 3: pusty",
      "Rząd 4: pusty",
      "Rząd 5: perła 5/5",
    ]);
    expect(within(lines[4] ?? document.body).getAllByRole("img")).toHaveLength(5);
  });

  it("shows the floor with its penalty and the marker", () => {
    board({ view: mozaika({ players: [mozaikaPlayer({ floor: [0, 2, 0, 0, 0], marker: true }), mozaikaPlayer()] }) });

    const floor = within(ala()).getByLabelText("Podłoga");
    expect(within(floor).getByLabelText("ochra: 2")).toBeInTheDocument();
    expect(within(floor).getByLabelText("Znacznik pierwszego gracza")).toBeInTheDocument();
    expect(floor).toHaveTextContent("−4 pkt");
    expect(within(screen.getByLabelText("Gracz Ola")).getByLabelText("Podłoga")).not.toHaveTextContent("pkt");
  });

  it("plays tiles picked in a factory on a chosen line", async () => {
    const { onMove, user } = board({ legalMoves: mozaikaMoves() });

    expect(within(ala()).queryByRole("button", { name: /^Połóż na rząd/u })).not.toBeInTheDocument();
    await user.click(tile("Warsztat 2: karmin"));
    expect(tile("Warsztat 2: karmin")).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByLabelText("Runda")).toHaveTextContent("wybrano: karmin");
    expect(within(ala()).getAllByRole("button", { name: /^Połóż na rząd/u })).toHaveLength(5);
    expect(within(screen.getByLabelText("Gracz Ola")).queryByRole("button", { name: /^Połóż na rząd/u })).not.toBeInTheDocument();
    await user.click(within(ala()).getAllByRole("button", { name: /^Połóż na rząd/u })[3] ?? document.body);

    expect(onMove).toHaveBeenCalledWith("f1:C:3");
    expect(within(ala()).queryByRole("button", { name: /^Połóż na rząd/u })).not.toBeInTheDocument();
  });

  it("offers only the lines the server allows and the floor", async () => {
    const { onMove, user } = board({ legalMoves: ["f0:T:2", "f0:T:floor", "f2:G:0"] });

    expect(tile("Warsztat 1: ochra")).toBeDisabled();
    await user.click(tile("Warsztat 1: turkus"));
    expect(within(ala()).getAllByRole("button", { name: /^Połóż na rząd/u })).toHaveLength(1);
    expect(
      within(within(ala()).getAllByRole("listitem")[2] ?? document.body).getByRole("button", { name: /^Połóż na rząd/u }),
    ).toBeInTheDocument();
    await user.click(within(ala()).getByRole("button", { name: "Połóż na podłogę" }));

    expect(onMove).toHaveBeenCalledWith("f0:T:floor");
  });

  it("drops a pick when clicked again, when another is picked or when the position changes", async () => {
    const { user, rerender } = board({ legalMoves: mozaikaMoves() });

    await user.click(tile("Warsztat 1: turkus"));
    await user.click(tile("Warsztat 1: turkus"));
    expect(tile("Warsztat 1: turkus")).toHaveAttribute("aria-pressed", "false");

    await user.click(tile("Warsztat 1: turkus"));
    await user.click(tile("Warsztat 4: grafit"));
    expect(tile("Warsztat 1: turkus")).toHaveAttribute("aria-pressed", "false");
    expect(tile("Warsztat 4: grafit")).toHaveAttribute("aria-pressed", "true");

    rerender({ legalMoves: [...mozaikaMoves()] });
    expect(tile("Warsztat 4: grafit")).toHaveAttribute("aria-pressed", "false");
  });

  it("takes from the center", async () => {
    const view = mozaika({ center: [0, 0, 0, 2, 0] });
    const { onMove, user } = board({ view, legalMoves: mozaikaMoves(view) });

    await user.click(tile("Środek: grafit"));
    await user.click(within(ala()).getAllByRole("button", { name: /^Połóż na rząd/u })[0] ?? document.body);
    expect(onMove).toHaveBeenCalledWith("c:G:0");
  });

  it("gives a spectator nothing to click", () => {
    board({ seat: null });

    expect(screen.getAllByRole("button").every((b) => (b as HTMLButtonElement).disabled)).toBe(true);
    expect(screen.queryByText("ty")).not.toBeInTheDocument();
  });

  it("names unnamed players and marks nobody on the move after the end", () => {
    board({ names: [], seat: null, view: mozaika({ over: true }) });

    expect(screen.getByLabelText("Gracz Gracz 1")).toBeInTheDocument();
    expect(screen.queryByText("na ruchu")).not.toBeInTheDocument();
    expect(screen.queryByText("ty")).not.toBeInTheDocument();
  });
});

describe("Mozaika floor and workshops drawing", () => {
  it("lays the floor out on its penalty slots, marker first", () => {
    board({ view: mozaika({ players: [mozaikaPlayer({ floor: [0, 2, 0, 1, 0], marker: true }), mozaikaPlayer()] }) });

    const slots = within(ala()).getByLabelText("Podłoga").querySelector(".floor-slots");
    const cells = Array.from(slots?.children ?? []).map((el) => el.className);
    const penalties = mozaikaRules().floorPenalties;
    expect(cells.slice(0, 3)).toEqual(["marker", "floor-group", "floor-group"]);
    expect(slots?.querySelectorAll(".tile")).toHaveLength(3);
    expect(slots?.querySelectorAll(".slot")).toHaveLength(penalties.length - 4);
    expect(Array.from(slots?.querySelectorAll(".penalty") ?? []).map((p) => p.textContent)).toEqual(penalties.map((n) => `−${String(n)}`));
    expect(slots).toHaveStyle({ "--slots": String(penalties.length) });
    expect(within(ala()).getByLabelText("grafit: 1")).toBeInTheDocument();
  });

  it("shows every tile in a workshop and a count in the center", () => {
    board({ view: mozaika({ factories: [[2, 1, 0, 1, 0]], center: [0, 0, 5, 0, 0] }) });

    const workshop = screen.getByRole("group", { name: "Warsztat 1" });
    expect(workshop.querySelectorAll(".tile")).toHaveLength(4);
    expect(within(workshop).queryAllByRole("img")).toHaveLength(0);
    expect(within(screen.getByRole("group", { name: "Środek" })).getByRole("img", { name: "karmin: 5" })).toHaveTextContent("5");
  });
});
