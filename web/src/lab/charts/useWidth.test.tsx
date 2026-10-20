import { act, render, screen } from "@testing-library/react";
import { useRef } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useWidth } from "./useWidth";

function Probe({ fallback = 960 }: { fallback?: number }) {
  const ref = useRef<HTMLDivElement>(null);
  const width = useWidth(ref, fallback);
  return <div ref={ref}>szerokosc {width}</div>;
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("useWidth", () => {
  it("follows the element size and stops when unmounted", () => {
    let report: ResizeObserverCallback = () => undefined;
    const disconnect = vi.fn();
    vi.stubGlobal(
      "ResizeObserver",
      class {
        constructor(callback: ResizeObserverCallback) {
          report = callback;
        }

        observe = vi.fn();

        disconnect = disconnect;
      },
    );
    const { unmount } = render(<Probe />);
    expect(screen.getByText("szerokosc 960")).toBeInTheDocument();

    const resize = (width: number): void => {
      act(() => {
        report([{ contentRect: { width } } as ResizeObserverEntry], {} as ResizeObserver);
      });
    };
    resize(331.6);
    expect(screen.getByText("szerokosc 332")).toBeInTheDocument();
    resize(0); // ukryty element (np. zwinieta sekcja) nie zeruje wykresu
    expect(screen.getByText("szerokosc 332")).toBeInTheDocument();
    act(() => {
      report([], {} as ResizeObserver);
    });
    expect(screen.getByText("szerokosc 332")).toBeInTheDocument();

    unmount();
    expect(disconnect).toHaveBeenCalledOnce();
  });

  it("keeps the fallback without ResizeObserver", () => {
    vi.stubGlobal("ResizeObserver", undefined);

    render(<Probe fallback={640} />);

    expect(screen.getByText("szerokosc 640")).toBeInTheDocument();
  });
});
