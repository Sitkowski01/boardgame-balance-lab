import { describe, expect, it } from "vitest";
import { experiment } from "../test/fakeLab";
import { cards, comparison, fields, item, league, tournament } from "../test/labReports";
import { edge, experimentInfo, finished, pairScore, percent, points, resultOf, timeAgo, verdictMark, verdictWord } from "./model";

describe("lab model", () => {
  it("reads the result with the schema of its kind", () => {
    expect(resultOf(experiment({ status: "Done", result: tournament() }))).toEqual({ kind: "Tournament", report: tournament() });
    expect(resultOf(experiment({ status: "Done", request: { ...experiment().request, kind: "League" }, result: league() }))?.kind).toBe(
      "League",
    );
    expect(
      resultOf(experiment({ status: "Done", request: { ...experiment().request, kind: "Comparison" }, result: comparison() }))?.kind,
    ).toBe("Comparison");
  });

  it("gives no result before the end or for a result of the wrong shape", () => {
    expect(resultOf(experiment({ status: "Running", result: tournament() }))).toBeNull();
    expect(resultOf(experiment({ status: "Done", result: league() }))).toBeNull();
    expect(resultOf(experiment({ status: "Done", request: { ...experiment().request, kind: "League" }, result: tournament() }))).toBeNull();
    expect(resultOf(experiment({ status: "Done", request: { ...experiment().request, kind: "Comparison" }, result: {} }))).toBeNull();
  });

  it("accepts what the server sends and refuses an unknown status", () => {
    expect(experimentInfo.safeParse(experiment()).success).toBe(true);
    expect(experimentInfo.safeParse({ ...experiment(), status: "Paused" }).success).toBe(false);
  });

  it("knows which statuses are final", () => {
    expect(["Queued", "Running", "Done", "Failed", "Cancelled"].map((s) => finished(s as never))).toEqual([false, false, true, true, true]);
  });

  it("formats rates and differences", () => {
    expect(percent(0.523)).toBe("52,3%");
    expect([points(0.041), points(-0.041), points(0)]).toEqual(["+4,1 pp", "−4,1 pp", "±0,0 pp"]);
  });

  it("reads a league cell from either side of the pair and skips pairs without games", () => {
    expect(pairScore(league(), "greedy", "random")).toBe(0.95);
    expect(pairScore(league(), "random", "greedy")).toBeCloseTo(0.05);
    expect(pairScore(league(), "ismcts:50", "random")).toBeNull();
    expect(pairScore(league(), "greedy", "greedy")).toBeNull();
  });

  it("measures an item against its own group", () => {
    const report = tournament();

    expect(edge(report, report.items[0] ?? item())).toBeCloseTo(0.8 - 0.4);
    expect(edge(report, item({ group: "nieznana" }))).toBe(0);
  });

  it("names verdicts in the gender of the item and marks them with a shape", () => {
    expect(["Strong", "Weak", "Dead", "None"].map((v) => verdictWord(v as never, cards))).toEqual(["za mocna", "za slaba", "martwa", ""]);
    expect(verdictWord("Dead", fields)).toBe("martwe");
    expect(["Strong", "Weak", "Dead", "None"].map((v) => verdictMark(v as never))).toEqual(["▲", "▼", "○", ""]);
  });

  it("says how long ago an experiment was ordered", () => {
    const now = Date.parse("2026-10-23T12:00:00Z");
    expect(timeAgo("2026-10-23T11:59:30Z", now)).toBe("przed chwilą");
    expect(timeAgo("2026-10-23T11:59:00Z", now)).toBe("1 min temu");
    expect(timeAgo("2026-10-23T11:01:00Z", now)).toBe("59 min temu");
    expect(timeAgo("2026-10-23T11:00:00Z", now)).toBe("1 godz. temu");
    expect(timeAgo("2026-10-22T12:01:00Z", now)).toBe("23 godz. temu");
    expect(timeAgo("2026-10-05T12:00:00", now)).toBe("05.10");
    expect(timeAgo("nie data", now)).toBe("");
  });
});
