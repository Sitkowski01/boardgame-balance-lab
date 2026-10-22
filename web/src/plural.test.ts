import { describe, expect, it } from "vitest";
import { plural } from "./plural";

const cards = (n: number): string => `${String(n)} ${plural(n, "karta", "karty", "kart")}`;

describe("plural", () => {
  it("picks the Polish form for one, a few and many", () => {
    expect([0, 1, 2, 3, 4, 5, 11].map(cards)).toEqual(["0 kart", "1 karta", "2 karty", "3 karty", "4 karty", "5 kart", "11 kart"]);
  });

  it("uses many for the teens and a few again after them", () => {
    expect([12, 13, 14, 15, 21, 22, 24, 25, 102, 112, 114, 122].map(cards)).toEqual([
      "12 kart",
      "13 kart",
      "14 kart",
      "15 kart",
      "21 kart",
      "22 karty",
      "24 karty",
      "25 kart",
      "102 karty",
      "112 kart",
      "114 kart",
      "122 karty",
    ]);
  });
});
