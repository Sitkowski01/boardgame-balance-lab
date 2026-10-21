import fc from "fast-check";
import { describe, expect, it } from "vitest";
import { openingMoves, sheet } from "../test/boards";
import { canPick, cardMoves, describeMove, letters, parseMove, takeMove, toggleGem } from "./moves";

describe("parseMove", () => {
  it("reads every kind of move in the server notation", () => {
    expect(parseMove("take:WUG")).toEqual({ kind: "take", gems: [0, 1, 2] });
    expect(parseMove("take2:RR")).toEqual({ kind: "take2", gem: 3 });
    expect(parseMove("return:Z")).toEqual({ kind: "return", gem: 5 });
    expect(parseMove("reserve:17")).toEqual({ kind: "reserve", card: 17 });
    expect(parseMove("buy:3")).toEqual({ kind: "buy", card: 3 });
    expect(parseMove("buy-reserved:88")).toEqual({ kind: "buy-reserved", card: 88 });
    expect(parseMove("deck:2")).toEqual({ kind: "deck", tier: 2 });
    expect(parseMove("pass")).toEqual({ kind: "pass" });
  });

  it("rejects unknown moves and colors", () => {
    expect(() => parseMove("jump:1")).toThrow("Nieznany ruch 'jump:1'.");
    expect(() => parseMove("take:WX")).toThrow("Nieznany kolor 'X'.");
    expect(() => parseMove("return:")).toThrow("Nieznany kolor ''.");
    expect(() => parseMove("buy")).toThrow("Ruch 'buy' bez argumentu.");
    expect(() => parseMove("dziwny:1")).toThrow("Nieznany ruch 'dziwny:1'.");
  });
});

describe("selection of tokens", () => {
  it("writes letters in server order whatever the click order", () => {
    expect(letters([2, 0, 4])).toBe("WGK");
  });

  it("adds new colors, removes clicked ones and doubles a single color", () => {
    const legal = openingMoves();

    expect(toggleGem([], 1, legal)).toEqual([1]);
    expect(toggleGem([1], 3, legal)).toEqual([1, 3]);
    expect(toggleGem([1, 3], 1, legal)).toEqual([3]);
    expect(toggleGem([1], 1, legal)).toEqual([1, 1]);
    expect(toggleGem([1, 1], 1, legal)).toEqual([]);
    expect(toggleGem([1, 1], 2, legal)).toEqual([2]);
  });

  it("drops a single color clicked again when two of it may not be taken", () => {
    expect(toggleGem([1], 1, ["take:UGR"])).toEqual([]);
    expect(canPick([1], 1, ["take:UGR"])).toBe(true);
  });

  it("gives a move only when the whole selection is a legal move", () => {
    const legal = openingMoves();

    expect(takeMove([], legal)).toBeNull();
    expect(takeMove([0, 1], legal)).toBeNull();
    expect(takeMove([2, 0, 1], legal)).toBe("take:WUG");
    expect(takeMove([3, 3], legal)).toBe("take2:RR");
    expect(takeMove([3, 3], ["take:WUG"])).toBeNull();
  });

  it("lets a color be picked only on the way to a legal move", () => {
    const legal = ["take:WUG", "take2:UU"];

    expect(canPick([], 0, legal)).toBe(true);
    expect(canPick([], 3, legal)).toBe(false);
    expect(canPick([], 3, ["take2:RR"])).toBe(true);
    expect(canPick([0], 1, ["take2:UU"])).toBe(false);
    expect(canPick([0], 2, ["take:WR"])).toBe(false);
    expect(canPick([0, 1], 2, legal)).toBe(true);
    expect(canPick([1, 1], 0, legal)).toBe(true);
    expect(canPick([1, 1], 3, legal)).toBe(false);
  });

  it("always lets a picked color be clicked again", () => {
    expect(canPick([1], 1, ["take2:UU"])).toBe(true);
    expect(canPick([0, 1], 0, ["take:WUG"])).toBe(true);
    expect(canPick([1, 1], 1, ["take2:UU"])).toBe(true);
  });

  it("never builds a move that is not on the legal list, whatever is clicked", () => {
    const legalSets = fc.subarray(
      openingMoves().filter((m) => m.startsWith("take")),
      { minLength: 1 },
    );
    fc.assert(
      fc.property(legalSets, fc.array(fc.integer({ min: 0, max: 4 }), { maxLength: 12 }), (legal, clicks) => {
        let selection: number[] = [];
        for (const gem of clicks) {
          if (canPick(selection, gem, legal)) {
            selection = toggleGem(selection, gem, legal);
          }

          const move = takeMove(selection, legal);
          expect(move === null || legal.includes(move)).toBe(true);
        }
      }),
    );
  });

  it("reaches every legal take by clicking only pickable colors", () => {
    fc.assert(
      fc.property(fc.constantFrom(...openingMoves().filter((m) => m.startsWith("take"))), (move) => {
        const parsed = parseMove(move);
        const clicks = parsed.kind === "take" ? parsed.gems : parsed.kind === "take2" ? [parsed.gem, parsed.gem] : [];
        let selection: number[] = [];
        for (const gem of clicks) {
          expect(canPick(selection, gem, [move])).toBe(true);
          selection = toggleGem(selection, gem, [move]);
        }

        expect(takeMove(selection, [move])).toBe(move);
      }),
    );
  });
});

describe("cards", () => {
  it("offers buying from the market or the reserve and reserving", () => {
    expect(cardMoves(5, ["buy:5", "reserve:5"])).toEqual({ buy: "buy:5", reserve: "reserve:5" });
    expect(cardMoves(5, ["buy-reserved:5"])).toEqual({ buy: "buy-reserved:5", reserve: null });
    expect(cardMoves(5, ["buy:50", "reserve:6"])).toEqual({ buy: null, reserve: null });
  });
});

describe("describeMove", () => {
  it("says what happened in words, with the card when the sheet is known", () => {
    const cards = sheet().cards;

    expect(describeMove("take:WUG")).toBe("bierze biały, niebieski, zielony");
    expect(describeMove("take2:KK")).toBe("bierze dwa: czarny");
    expect(describeMove("return:Z")).toBe("oddaje złoto");
    expect(describeMove("buy:41", cards)).toBe("kupuje kartę (niebieski, 1 pkt)");
    expect(describeMove("buy:41")).toBe("kupuje kartę #41");
    expect(describeMove("buy-reserved:2", cards)).toBe("kupuje z rezerwy kartę (zielony, 0 pkt)");
    expect(describeMove("reserve:7")).toBe("rezerwuje kartę #7");
    expect(describeMove("deck:3")).toBe("rezerwuje w ciemno z talii 3");
    expect(describeMove("pass")).toBe("pasuje");
  });
});
