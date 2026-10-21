import { describe, expect, it } from "vitest";
import { mozaika, mozaikaPlayer, mozaikaRules } from "../test/boards";
import { floorCount, floorPenalty, mozaikaSheet, mozaikaView, tileName, tileObject, total } from "./model";

describe("Mozaika model", () => {
  it("accepts the view and the rules the server sends", () => {
    expect(mozaikaView.safeParse(mozaika()).success).toBe(true);
    expect(mozaikaSheet.safeParse(mozaikaRules()).success).toBe(true);
  });

  it("refuses views of the wrong shape", () => {
    const line = (color: number | null, count: number) => mozaikaPlayer({ lines: Array.from({ length: 5 }, () => ({ color, count })) });

    expect(mozaikaView.safeParse(mozaika({ center: [1, 2] })).success).toBe(false);
    expect(mozaikaView.safeParse(mozaika({ players: [line(5, 1)] })).success).toBe(false);
    expect(mozaikaView.safeParse(mozaika({ players: [line(null, -1)] })).success).toBe(false);
    expect(mozaikaView.safeParse(mozaika({ players: [mozaikaPlayer({ wall: [25] })] })).success).toBe(false);
    expect(mozaikaView.safeParse(mozaika({ players: [mozaikaPlayer({ lines: [] })] })).success).toBe(false);
    expect(mozaikaSheet.safeParse({ ...mozaikaRules(), wall: [[0, 1, 2, 3, 4]] }).success).toBe(false);
  });

  it("names colors and counts tiles", () => {
    expect([0, 1, 2, 3, 4, 7].map(tileName)).toEqual(["turkus", "ochra", "karmin", "grafit", "perła", "?"]);
    expect([0, 1, 4, 7].map(tileObject)).toEqual(["turkus", "ochrę", "perłę", "?"]);
    expect(total([1, 2, 0, 3, 0])).toBe(6);
  });

  it("charges the floor up to its last field, the marker included", () => {
    const rules = mozaikaRules();

    expect(floorPenalty(mozaikaPlayer(), rules)).toBe(0);
    expect(floorCount(mozaikaPlayer({ floor: [1, 0, 2, 0, 0], marker: true }))).toBe(4);
    expect(floorPenalty(mozaikaPlayer({ floor: [1, 0, 2, 0, 0], marker: true }), rules)).toBe(6);
    expect(floorPenalty(mozaikaPlayer({ floor: [9, 0, 0, 0, 0] }), rules)).toBe(14);
  });
});
