import { act, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { TurnBar } from "./TurnBar";

afterEach(() => {
  vi.unstubAllGlobals();
  document.documentElement.style.removeProperty("--topbar-height");
});

/** jsdom nie ma IntersectionObserver: podstawiamy wlasny i sami wolamy go z polozeniem paska. */
function observe() {
  const seen: { report: (ratio: number, top: number) => void; options?: IntersectionObserverInit | undefined; disconnect: () => void } = {
    report: () => undefined,
    disconnect: vi.fn(),
  };
  vi.stubGlobal(
    "IntersectionObserver",
    class {
      constructor(callback: IntersectionObserverCallback, options?: IntersectionObserverInit) {
        seen.options = options;
        seen.report = (ratio, top) => {
          act(() => {
            callback([{ intersectionRatio: ratio, boundingClientRect: { top } } as IntersectionObserverEntry], this as never);
          });
        };
      }
      observe = vi.fn();
      disconnect = seen.disconnect;
    },
  );
  return seen;
}

describe("TurnBar", () => {
  it("gets its background only once stuck under the header", () => {
    document.documentElement.style.setProperty("--topbar-height", "56px");
    const seen = observe();
    const { unmount } = render(<TurnBar>Twój ruch!</TurnBar>);
    const bar = screen.getByText("Twój ruch!");
    expect(seen.options).toEqual({ rootMargin: "-57px 0px 0px 0px", threshold: 1 });
    expect(bar).not.toHaveClass("stuck");

    seen.report(0.9, 56);
    expect(bar).toHaveClass("turn-bar stuck");
    seen.report(0.9, 57);
    expect(bar).toHaveClass("stuck");

    // Uciety od dolu (pod krawedzia ekranu) to nie przyklejenie.
    seen.report(0.5, 58);
    expect(bar).not.toHaveClass("stuck");
    seen.report(1, 56);
    expect(bar).not.toHaveClass("stuck");

    unmount();
    expect(seen.disconnect).toHaveBeenCalled();
  });

  it("works without the header size and without IntersectionObserver", () => {
    const seen = observe();
    render(<TurnBar>Na ruchu: Ola</TurnBar>);
    expect(seen.options?.rootMargin).toBe("-1px 0px 0px 0px");

    vi.stubGlobal("IntersectionObserver", undefined);
    render(<TurnBar>Czekamy…</TurnBar>);
    expect(screen.getByText("Czekamy…")).toHaveClass("turn-bar");
  });
});
