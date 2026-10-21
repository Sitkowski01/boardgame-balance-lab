import { describe, expect, it } from "vitest";
import { newDiscs, sameRows, winningCells } from "./model";

type Cell = number | null;
const empty = (): Cell[][] => Array.from({ length: 6 }, () => Array.from({ length: 7 }, (): Cell => null));
const put = (rows: Cell[][], cells: readonly [number, number][], player: number): Cell[][] => {
  const next = rows.map((r) => [...r]);
  for (const [r, c] of cells) {
    const row = next[r];
    if (row !== undefined) row[c] = player;
  }
  return next;
};
const sorted = (s: Set<number>): number[] => [...s].sort((a, b) => a - b);

describe("winning four", () => {
  it("finds a row, a column and both diagonals", () => {
    expect(
      sorted(
        winningCells(
          put(
            empty(),
            [
              [5, 1],
              [5, 2],
              [5, 3],
              [5, 4],
            ],
            0,
          ),
        ),
      ),
    ).toEqual([36, 37, 38, 39]);
    expect(
      sorted(
        winningCells(
          put(
            empty(),
            [
              [2, 6],
              [3, 6],
              [4, 6],
              [5, 6],
            ],
            1,
          ),
        ),
      ),
    ).toEqual([20, 27, 34, 41]);
    expect(
      sorted(
        winningCells(
          put(
            empty(),
            [
              [2, 0],
              [3, 1],
              [4, 2],
              [5, 3],
            ],
            0,
          ),
        ),
      ),
    ).toEqual([14, 22, 30, 38]);
    expect(
      sorted(
        winningCells(
          put(
            empty(),
            [
              [5, 3],
              [4, 4],
              [3, 5],
              [2, 6],
            ],
            1,
          ),
        ),
      ),
    ).toEqual([20, 26, 32, 38]);
  });

  it("needs four of one player in a line inside the board", () => {
    expect(
      winningCells(
        put(
          empty(),
          [
            [5, 0],
            [5, 1],
            [5, 2],
          ],
          0,
        ),
      ).size,
    ).toBe(0);
    const mixed = put(
      put(
        empty(),
        [
          [5, 0],
          [5, 1],
          [5, 2],
        ],
        0,
      ),
      [[5, 3]],
      1,
    );
    expect(winningCells(mixed).size).toBe(0);
    // Trzy na koncu wiersza nie lacza sie z poczatkiem nastepnego.
    expect(
      winningCells(
        put(
          empty(),
          [
            [4, 5],
            [4, 6],
            [5, 0],
            [5, 1],
          ],
          0,
        ),
      ).size,
    ).toBe(0);
    expect(
      winningCells(
        put(
          empty(),
          [
            [2, 1],
            [3, 0],
            [4, 6],
            [5, 5],
          ],
          0,
        ),
      ).size,
    ).toBe(0);
    expect(winningCells([]).size).toBe(0);
  });

  it("marks every cell of a line longer than four", () => {
    expect(
      sorted(
        winningCells(
          put(
            empty(),
            [
              [5, 0],
              [5, 1],
              [5, 2],
              [5, 3],
              [5, 4],
            ],
            1,
          ),
        ),
      ),
    ).toEqual([35, 36, 37, 38, 39]);
  });
});

describe("last move", () => {
  it("is the one new disc since the previous position", () => {
    const before = put(empty(), [[5, 0]], 0);
    expect(sorted(newDiscs(before, put(before, [[5, 3]], 1)))).toEqual([38]);
  });

  it("is the reply when a move and the answer arrive together", () => {
    const one = put(empty(), [[5, 0]], 0);
    expect(sorted(newDiscs(empty(), put(one, [[5, 1]], 1)))).toEqual([36]);
    const three = put(put(one, [[5, 1]], 1), [[4, 0]], 0);
    expect(sorted(newDiscs(one, three))).toEqual([28]);
  });

  it("is nothing at the start, after going back or after a jump of several moves", () => {
    const one = put(empty(), [[5, 0]], 0);
    expect(newDiscs(null, one).size).toBe(0);
    expect(newDiscs(one, empty()).size).toBe(0);
    expect(newDiscs(one, one).size).toBe(0);
    const four = put(
      put(
        one,
        [
          [5, 1],
          [5, 2],
        ],
        1,
      ),
      [[4, 0]],
      0,
    );
    expect(newDiscs(one, four).size).toBe(0);
  });

  it("counts discs, not empty cells, to know who moved", () => {
    // Na planszy o nieparzystej liczbie pol puste pola daja inna parzystosc niz krazki.
    expect(sorted(newDiscs([[null, null, null]], [[0, null, null]]))).toEqual([0]);
    // Krazek gracza 1, ktory juz lezal, nie jest nowy.
    const two = put(put(empty(), [[5, 0]], 0), [[5, 1]], 1);
    const four = put(put(two, [[5, 2]], 0), [[4, 0]], 1);
    expect(sorted(newDiscs(two, four))).toEqual([28]);
  });

  it("compares positions by their cells", () => {
    const one = put(empty(), [[5, 0]], 0);
    expect(sameRows(one, put(empty(), [[5, 0]], 0))).toBe(true);
    expect(sameRows(one, put(empty(), [[5, 0]], 1))).toBe(false);
    expect(sameRows(one, empty())).toBe(false);
    expect(sameRows(one, one.slice(1))).toBe(false);
    expect(sameRows([[0, null]], [[0]])).toBe(false);
    expect(sameRows([[0]], [[0, null]])).toBe(false);
    expect(sameRows([], [[0]])).toBe(false);
    expect(sameRows([], [])).toBe(true);
  });
});
