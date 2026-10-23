import { act, render } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { CountUp, CountUpMs, partial } from "./CountUp";

function motion(reduced: boolean): void {
  vi.stubGlobal("matchMedia", (query: string) => ({ matches: reduced && query === "(prefers-reduced-motion: reduce)" }));
}

afterEach(() => {
  vi.unstubAllGlobals();
  vi.useRealTimers();
});

describe("partial", () => {
  it("moves only the leading number and keeps its decimals, sign and the rest of the text", () => {
    expect(partial("53,5% partii", 0)).toBe("0,0% partii");
    expect(partial("53,5% partii", 0.5)).toBe("26,8% partii");
    expect(partial("53,5% partii", 1)).toBe("53,5% partii");
    expect(partial("−6,5 ruchu", 0.5)).toBe("−3,3 ruchu");
    expect(partial("+12 pkt", 0.25)).toBe("+3 pkt");
    expect(partial("200 z 200", 0.5)).toBe("100 z 200");
    expect(partial("Zachłanny", 0.5)).toBe("Zachłanny");
  });
});

describe("CountUp", () => {
  it("counts up to the value on screen and gives the final value to screen readers at once", () => {
    vi.useFakeTimers({ toFake: ["requestAnimationFrame", "cancelAnimationFrame", "performance"] });
    motion(false);
    const { container, unmount } = render(<CountUp text="61,2 ruchu" />);

    const [shown, spoken] = [...container.querySelectorAll("span")];
    expect(shown).toHaveAttribute("aria-hidden", "true");
    expect(spoken).toHaveClass("visually-hidden");
    expect(spoken).toHaveTextContent("61,2 ruchu");
    expect(shown).toHaveTextContent("0,0 ruchu");

    act(() => {
      vi.advanceTimersByTime(CountUpMs / 2);
    });
    const halfway = Number((shown?.textContent ?? "").split(" ")[0]?.replace(",", "."));
    expect(halfway).toBeGreaterThan(30.6);
    expect(halfway).toBeLessThan(61.2);

    act(() => {
      vi.advanceTimersByTime(CountUpMs);
    });
    expect(shown).toHaveTextContent("61,2 ruchu");
    unmount();
  });

  it("shows the value at once when motion is reduced, without matchMedia or without a number", () => {
    motion(true);
    const reduced = render(<CountUp text="53,5%" />);
    expect(reduced.container.innerHTML).toBe("53,5%");
    reduced.unmount();

    motion(false);
    expect(render(<CountUp text="Zachłanny" />).container.innerHTML).toBe("Zachłanny");

    vi.stubGlobal("matchMedia", undefined);
    expect(render(<CountUp text="7" />).container.innerHTML).toBe("7");
  });
});
