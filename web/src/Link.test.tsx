import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { Link } from "./Link";

function link() {
  const navigate = vi.fn<(path: string) => void>();
  render(
    <Link to="/lab" navigate={navigate} className="brand">
      Laboratorium
    </Link>,
  );
  return { navigate, anchor: screen.getByRole("link", { name: "Laboratorium" }) };
}

describe("Link", () => {
  it("changes the page without reloading on a plain click", () => {
    const { navigate, anchor } = link();

    expect(anchor).toHaveAttribute("href", "/lab");
    expect(anchor).toHaveClass("brand");
    // fireEvent zwraca false, gdy ktos zablokowal domyslna akcje przegladarki.
    expect(fireEvent.click(anchor, { button: 0 })).toBe(false);
    expect(navigate).toHaveBeenCalledWith("/lab");
  });

  it.each([
    ["Ctrl", { ctrlKey: true }],
    ["Cmd", { metaKey: true }],
    ["Shift", { shiftKey: true }],
    ["Alt", { altKey: true }],
    ["the middle button", { button: 1 }],
  ])("leaves a click with %s to the browser (new tab, new window)", (_, modifier) => {
    const { navigate, anchor } = link();

    expect(fireEvent.click(anchor, modifier)).toBe(true);
    expect(navigate).not.toHaveBeenCalled();
  });
});
