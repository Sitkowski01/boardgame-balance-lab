import { render } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { Bump } from "./Bump";

describe("Bump", () => {
  it("stays still at first and bumps a fresh element on every change", () => {
    const { container, rerender } = render(<Bump value={3}>3 pkt</Bump>);
    const first = container.firstElementChild;
    expect(first).not.toHaveClass("bump");
    expect(first).toHaveTextContent("3 pkt");

    rerender(<Bump value={3}>3 pkt</Bump>);
    expect(container.firstElementChild).toBe(first);

    rerender(<Bump value={5}>5 pkt</Bump>);
    const second = container.firstElementChild;
    expect(second).toHaveClass("bump");
    expect(second).not.toBe(first);

    rerender(<Bump value={3}>3 pkt</Bump>);
    expect(container.firstElementChild).toHaveClass("bump");
    expect(container.firstElementChild).not.toBe(second);
  });
});
