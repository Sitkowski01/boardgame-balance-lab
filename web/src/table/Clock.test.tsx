import { act, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Clock } from "./Clock";

afterEach(() => {
  vi.useRealTimers();
});

describe("Clock", () => {
  it("counts a new deadline from this moment, not from the last tick", () => {
    vi.useFakeTimers();
    let now = Date.parse("2026-10-26T10:00:00.000Z");
    const clock = () => now;
    const { rerender } = render(<Clock deadline="2026-10-26T10:01:00.000Z" now={clock} />);
    expect(screen.getByLabelText("Czas na ruch")).toHaveTextContent("60 s");

    // Pol sekundy po ostatnim tyku przychodzi nowy termin: minuta od teraz. Stary czas dalby 60,4 s -> "61 s".
    now += 400;
    rerender(<Clock deadline="2026-10-26T10:01:00.400Z" now={clock} />);
    expect(screen.getByLabelText("Czas na ruch")).toHaveTextContent("60 s");

    now += 30_000;
    act(() => {
      vi.advanceTimersByTime(500);
    });
    expect(screen.getByLabelText("Czas na ruch")).toHaveTextContent("30 s");
  });

  it("shows nothing without a deadline", () => {
    const { container } = render(<Clock deadline={null} now={() => 0} />);
    expect(container).toBeEmptyDOMElement();
  });
});
