import fc from "fast-check";
import { describe, expect, it } from "vitest";
import { mozaika, mozaikaMoves } from "../test/boards";
import { canTake, describeMove, formatMove, parseMove, placements, type MozaikaMove } from "./moves";

describe("parseMove", () => {
  it("reads factories, the center, lines and the floor in the server notation", () => {
    expect(parseMove("f2:C:3")).toEqual({ source: 2, color: 2, line: 3 });
    expect(parseMove("f10:T:0")).toEqual({ source: 10, color: 0, line: 0 });
    expect(parseMove("c:I:floor")).toEqual({ source: "center", color: 4, line: "floor" });
  });

  it.each([
    ["", "nie ma postaci"],
    ["f1:C", "nie ma postaci"],
    ["f1:C:2:3", "nie ma postaci"],
    ["f1:CC:2", "nie ma postaci"],
    ["f1:X:2", "nie ma postaci"],
    ["f1::2", "nie ma postaci"],
    ["x1:C:2", "zrodlo to fN albo c"],
    ["f:C:2", "zrodlo to fN albo c"],
    ["fa:C:2", "zrodlo to fN albo c"],
    ["cc:C:2", "zrodlo to fN albo c"],
    ["f1:C:5x", "rzad to 0-4 albo floor"],
    ["f1:C:12", "rzad to 0-4 albo floor"],
    ["f1:C:Floor", "rzad to 0-4 albo floor"],
  ])("rejects %j", (text, message) => {
    expect(() => parseMove(text)).toThrow(message);
  });

  it("writes back exactly what it reads", () => {
    const move = fc.record<MozaikaMove>({
      source: fc.oneof(fc.nat(8), fc.constant("center" as const)),
      color: fc.nat(4),
      line: fc.oneof(fc.nat(4), fc.constant("floor" as const)),
    });
    fc.assert(
      fc.property(move, (m) => {
        expect(parseMove(formatMove(m))).toEqual(m);
      }),
    );
    expect(formatMove({ source: 0, color: 9, line: 0 })).toBe("f0:?:0");
  });
});

describe("choosing tiles and a line", () => {
  it("allows only colors that some legal move takes from that source", () => {
    const legal = ["f0:T:1", "f0:T:floor", "c:G:floor"];

    expect(canTake(0, 0, legal)).toBe(true);
    expect(canTake(0, 3, legal)).toBe(false);
    expect(canTake(1, 0, legal)).toBe(false);
    expect(canTake("center", 3, legal)).toBe(true);
    expect(canTake("center", 0, legal)).toBe(false);
  });

  it("lists the legal lines for the picked tiles", () => {
    const legal = ["f0:T:1", "f0:T:floor", "f0:O:2", "f1:T:3", "c:T:4"];

    expect([...placements(0, 0, legal)]).toEqual([
      [1, "f0:T:1"],
      ["floor", "f0:T:floor"],
    ]);
    expect(placements("center", 0, legal).get(4)).toBe("c:T:4");
    expect(placements(2, 0, legal).size).toBe(0);
  });

  it("every placement offered is itself a legal move", () => {
    const legal = mozaikaMoves(mozaika());
    fc.assert(
      fc.property(fc.oneof(fc.nat(5), fc.constant("center" as const)), fc.nat(4), (source, color) => {
        for (const move of placements(source, color, legal).values()) {
          expect(legal).toContain(move);
        }
        expect(placements(source, color, legal).size > 0).toBe(canTake(source, color, legal));
      }),
    );
  });
});

describe("describeMove", () => {
  it("says what was taken, from where and where it went, counting from 1", () => {
    expect(describeMove("f2:C:3")).toBe("bierze karmin z warsztatu 3 na rząd 4");
    expect(describeMove("c:I:floor")).toBe("bierze kość ze środka na podłogę");
    expect(() => describeMove("dziwny")).toThrow();
  });
});
