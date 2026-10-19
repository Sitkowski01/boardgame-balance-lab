import { describe, expect, it } from "vitest";
import { klejnoty, player, sheet } from "../test/boards";
import { gemName, klejnotySheet, klejnotyView, marketRows, totalTokens } from "./model";

describe("Klejnoty model", () => {
  it("accepts the board and the card table the server sends", () => {
    expect(klejnotyView.parse(klejnoty())).toEqual(klejnoty());
    expect(klejnotySheet.parse(sheet()).cards).toHaveLength(90);
  });

  it("reject a board of the wrong shape", () => {
    expect(klejnotyView.safeParse({ ...klejnoty(), bank: [1, 2] }).success).toBe(false);
    expect(klejnotyView.safeParse({ ...klejnoty(), market: [1] }).success).toBe(false);
    expect(klejnotyView.safeParse({ ...klejnoty(), players: [player({ tokens: [-1, 0, 0, 0, 0, 0] })] }).success).toBe(false);
  });

  it("splits the market into tiers and counts tokens", () => {
    expect(marketRows(klejnoty().market)).toEqual([
      [0, 1, 2, 3],
      [40, 41, 42, 43],
      [70, 71, 72, 73],
    ]);
    expect(totalTokens(player({ tokens: [1, 2, 0, 0, 3, 1] }))).toBe(7);
  });

  it("an unknown color number still has a readable name", () => {
    expect(gemName(9)).toBe("?");
    expect(gemName(4)).toBe("czarny");
  });
});
