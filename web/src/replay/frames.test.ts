import { describe, expect, it, vi } from "vitest";
import { clampPosition, frameCache, scrollToShow } from "./frames";

describe("frames", () => {
  it("loads each frame once and prefetches the neighbours inside the match", async () => {
    const load = vi.fn((n: number) => Promise.resolve(`klatka ${String(n)}`));
    const frames = frameCache(load);

    await expect(frames.get(3)).resolves.toBe("klatka 3");
    await frames.get(3);
    frames.prefetch(3, 4);
    frames.prefetch(0, 1);
    frames.prefetch(4, 4);

    expect(load.mock.calls.map(([n]) => n)).toEqual([3, 4, 2, 1]);
  });

  it("tries a failed frame again", async () => {
    const load = vi.fn<(n: number) => Promise<string>>().mockRejectedValueOnce(new Error("siec")).mockResolvedValue("ok");
    const frames = frameCache(load);

    await expect(frames.get(1)).rejects.toThrow("siec");
    await expect(frames.get(1)).resolves.toBe("ok");
  });

  it("scrolls the move list only when the current move is out of sight, to its middle", () => {
    const list = { scrollTop: 100, height: 200 };
    expect(scrollToShow({ top: 100, height: 20 }, list)).toBeNull();
    expect(scrollToShow({ top: 280, height: 20 }, list)).toBeNull();
    expect(scrollToShow({ top: 281, height: 20 }, list)).toBe(191);
    expect(scrollToShow({ top: 99, height: 20 }, list)).toBe(9);
    expect(scrollToShow({ top: 10, height: 20 }, list)).toBe(0);
  });

  it("keeps the slider between the deal and the last move", () => {
    expect(clampPosition(-3, 10)).toBe(0);
    expect(clampPosition(4.7, 10)).toBe(4);
    expect(clampPosition(12, 10)).toBe(10);
  });

  it("prefetches the deal from the first move", async () => {
    const load = vi.fn((n: number) => Promise.resolve(n));
    const frames = frameCache(load);

    frames.prefetch(1, 3);
    await Promise.resolve();

    expect(load.mock.calls.map(([n]) => n)).toEqual([2, 0]);
  });
});
