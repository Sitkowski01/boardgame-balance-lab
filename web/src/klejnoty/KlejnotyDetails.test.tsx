import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { klejnoty, openingMoves, player, sheet } from "../test/boards";
import { Cost, Gem } from "./Gem";
import { KlejnotyBoard, type KlejnotyBoardProps } from "./KlejnotyBoard";
import { gemName } from "./model";

function board(patch: Partial<KlejnotyBoardProps> = {}) {
  const props: KlejnotyBoardProps = {
    view: klejnoty(),
    sheet: sheet(),
    legalMoves: [],
    seat: 0,
    names: ["Ala", "Ola"],
    onMove: vi.fn(),
    ...patch,
  };
  const result = render(<KlejnotyBoard {...props} />);
  return { ...result, props, user: userEvent.setup() };
}

const bank = (color: string): HTMLElement => screen.getByRole("button", { name: new RegExp(`^Bank: ${color}`, "u") });

describe("gems", () => {
  it("show a count and a name, or an own label", () => {
    render(
      <>
        <Gem gem={2} count={3} />
        <Gem gem={5} />
        <Gem gem={1} count={0} label="rabat" />
      </>,
    );

    expect(screen.getByLabelText("zielony: 3")).toHaveTextContent("3");
    expect(screen.getByLabelText("zielony: 3")).toHaveClass("gem", "gem-2");
    expect(screen.getByLabelText("zloto")).toHaveTextContent("");
    expect(screen.getByLabelText("zloto")).toHaveAttribute("title", "zloto");
    expect(screen.getByLabelText("rabat")).toHaveTextContent("0");
  });

  it("cost lists only the colors that are needed", () => {
    render(<Cost counts={[0, 2, 0, 1, 0]} />);

    expect(screen.getAllByLabelText(/: \d$/u).map((g) => g.getAttribute("aria-label"))).toEqual(["niebieski: 2", "czerwony: 1"]);
  });

  it("an unknown color number still has a readable name", () => {
    expect(gemName(9)).toBe("?");
    expect(gemName(4)).toBe("czarny");
  });
});

describe("board details", () => {
  it("shows bank counts and marks picked tokens", async () => {
    const { user } = board({ legalMoves: openingMoves() });

    expect(bank("bialy")).toHaveTextContent("4");
    expect(bank("zloto")).toHaveTextContent("5");
    await user.click(bank("bialy"));

    expect(bank("bialy")).toHaveClass("picked");
    expect(bank("niebieski")).not.toHaveClass("picked");
    expect(within(screen.getByLabelText("Wybrane zetony")).getAllByLabelText("bialy")).toHaveLength(1);
  });

  it("drops a single color clicked again when two of it may not be taken", async () => {
    const { user } = board({ legalMoves: ["take:WUG"] });

    await user.click(bank("bialy"));
    await user.click(bank("bialy"));

    expect(bank("bialy")).toHaveAttribute("aria-pressed", "false");
  });

  it("starts the choice again when the position changes", async () => {
    const { user, rerender, props } = board({ legalMoves: openingMoves() });
    await user.click(bank("bialy"));

    rerender(<KlejnotyBoard {...props} legalMoves={openingMoves()} />);

    expect(bank("bialy")).toHaveAttribute("aria-pressed", "false");
    expect(screen.queryByRole("button", { name: "Wyczyść" })).toBeNull();
  });

  it("shows only owned tokens, bonuses and patrons", () => {
    const players = [player({ tokens: [2, 0, 0, 0, 0, 1], bonuses: [0, 0, 3, 0, 0] }), player()];
    board({ view: klejnoty({ players }) });

    const ala = screen.getByLabelText("Gracz Ala");
    expect(
      within(within(ala).getByLabelText("Żetony"))
        .getAllByLabelText(/^(bialy|zloto): \d$/u)
        .map((g) => g.getAttribute("aria-label")),
    ).toEqual(["bialy: 2", "zloto: 1"]);
    expect(within(within(ala).getByLabelText("Żetony")).getByText("(3/10)")).toBeInTheDocument();
    expect(
      within(within(ala).getByLabelText("Rabaty"))
        .getAllByLabelText(/^rabat/u)
        .map((g) => g.getAttribute("aria-label")),
    ).toEqual(["rabat zielony: 3"]);
    expect(within(ala).queryByText(/patroni/u)).toBeNull();
  });

  it("marks me and the player to move", () => {
    board({ view: klejnoty({ toMove: 1 }) });

    expect(screen.getByLabelText("Gracz Ala")).toHaveClass("player", "me");
    expect(screen.getByLabelText("Gracz Ala")).not.toHaveClass("to-move");
    expect(screen.getByLabelText("Gracz Ola")).toHaveClass("to-move");
    expect(screen.getByLabelText("Gracz Ola")).not.toHaveClass("me");
    expect(within(screen.getByLabelText("Gracz Ola")).getByText("na ruchu")).toBeInTheDocument();
  });

  it("cards show their tier and points only when they give any", () => {
    board();

    expect(screen.getByLabelText("Karta #0")).toHaveClass("card", "tier-1");
    expect(within(screen.getByLabelText("Karta #0")).getByRole("strong")).toBeEmptyDOMElement();
    expect(within(screen.getByLabelText("Karta #70")).getByRole("strong")).toHaveTextContent("2");
    expect(within(screen.getByLabelText("Karta #0")).getAllByLabelText(/: \d$/u)).toHaveLength(1);
  });

  it("a card missing from the table is drawn without details", () => {
    board({ view: klejnoty({ market: [...klejnoty().market.slice(0, 11), 999] }) });

    expect(screen.getByLabelText("Karta #999")).toHaveClass("card", "tier-0");
  });

  it("hidden reserved cards show their tier", () => {
    const players = [player(), player({ reserved: [{ card: null, tier: 3, hidden: true }] })];
    board({ view: klejnoty({ players }) });

    expect(screen.getByLabelText("Zakryta karta poziomu 3")).toHaveClass("card", "back", "tier-3");
  });
});
