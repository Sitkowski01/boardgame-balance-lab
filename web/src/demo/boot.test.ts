import { afterEach, describe, expect, it, vi } from "vitest";
import { isLabPath, startDemo } from "./boot";

describe("isLabPath", () => {
  it("sends the experiments to the lab worker and everything else to the table", () => {
    expect(isLabPath("/api/experiments")).toBe(true);
    expect(isLabPath("/api/experiments/limits")).toBe(true);
    expect(isLabPath("/api/experiments/1")).toBe(true);
    expect(isLabPath("/api/experimentsy")).toBe(false);
    expect(isLabPath("/api/rooms")).toBe(false);
  });
});

describe("startDemo", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("starts a table worker and a lab worker and routes requests between them", async () => {
    const workers: { name: string; url: string; posted: unknown[] }[] = [];
    vi.stubGlobal(
      "Worker",
      class {
        readonly posted: unknown[] = [];
        constructor(url: URL, options: { name: string }) {
          workers.push({ name: options.name, url: url.pathname, posted: this.posted });
        }

        postMessage(message: unknown): void {
          this.posted.push(message);
        }

        addEventListener(): void {
          // odpowiedzi nie sa potrzebne: test patrzy, dokad poszly prosby
        }
      },
    );

    const demo = startDemo(vi.fn<typeof fetch>());
    void demo.fetch("/api/experiments");
    void demo.fetch("/api/games");
    await Promise.resolve();

    expect(workers.map((w) => w.name)).toEqual(["game", "lab"]);
    expect(workers.every((w) => w.url.endsWith("/demo/worker.ts"))).toBe(true);
    expect(workers[1]?.posted).toEqual([{ id: 1, op: "fetch", method: "GET", path: "/api/experiments", body: null }]);
    expect(workers[0]?.posted).toEqual([{ id: 1, op: "fetch", method: "GET", path: "/api/games", body: null }]);
  });
});
