import { act, render } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { burst, Confetti, ConfettiMs } from "./Confetti";

function motion(reduced: boolean): void {
  vi.stubGlobal("matchMedia", (query: string) => ({ matches: reduced && query === "(prefers-reduced-motion: reduce)" }));
}

/** Plotno jsdom nie rysuje; atrapa kontekstu liczy wywolania. */
function fakeCanvas() {
  const ctx = {
    clearRect: vi.fn(),
    save: vi.fn(),
    restore: vi.fn(),
    translate: vi.fn(),
    rotate: vi.fn(),
    fillRect: vi.fn(),
    fillStyle: "",
    globalAlpha: 1,
  };
  vi.spyOn(HTMLCanvasElement.prototype, "getContext").mockReturnValue(ctx as unknown as CanvasRenderingContext2D);
  return ctx;
}

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
  vi.useRealTimers();
});

describe("burst", () => {
  it("shoots every piece up from the top middle in the colors of the games", () => {
    const pieces = burst(400, 7, () => 0.5);
    expect(pieces).toHaveLength(7);
    expect(pieces.every((p) => p.x === 200 && p.y === 12 && p.vx === 0 && p.vy === -5.5 && p.size === 7.5)).toBe(true);
    expect(pieces.map((p) => p.color)).toEqual(["#d8403f", "#eec53b", "#2f6fd1", "#26864a", "#e0b041", "#5cc29d", "#d8403f"]);
    expect(burst(400, 1, () => 0)[0]).toMatchObject({ vx: -4.5, vy: -3, size: 5, spin: 0 });
  });
});

describe("Confetti", () => {
  it("bursts once over the result, fades out and goes away", () => {
    vi.useFakeTimers({ toFake: ["requestAnimationFrame", "cancelAnimationFrame", "performance"] });
    motion(false);
    const ctx = fakeCanvas();
    const { container } = render(<Confetti />);

    const canvas = container.querySelector("canvas");
    expect(canvas).toHaveClass("confetti");
    expect(canvas).toHaveAttribute("aria-hidden", "true");
    act(() => {
      vi.advanceTimersByTime(ConfettiMs / 2);
    });
    expect(ctx.fillRect).toHaveBeenCalled();
    expect(ctx.globalAlpha).toBeGreaterThan(0);
    expect(ctx.globalAlpha).toBeLessThan(1);

    act(() => {
      vi.advanceTimersByTime(ConfettiMs);
    });
    expect(container.querySelector("canvas")).toBeNull();
  });

  it("stays away when motion is reduced, without matchMedia or without a canvas", () => {
    motion(true);
    expect(render(<Confetti />).container.innerHTML).toBe("");

    vi.stubGlobal("matchMedia", undefined);
    expect(render(<Confetti />).container.innerHTML).toBe("");

    motion(false);
    vi.spyOn(HTMLCanvasElement.prototype, "getContext").mockReturnValue(null);
    expect(render(<Confetti />).container.innerHTML).toBe("");
  });
});
