import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { tileName } from "./model";
import { Tile } from "./Tile";

describe("Tile", () => {
  it("names the colour and shows the count only for several tiles", () => {
    const name = tileName(2);
    render(
      <>
        <Tile tile={2} />
        <Tile tile={2} count={1} />
        <Tile tile={2} count={3} />
      </>,
    );

    const [plain, one, three] = screen.getAllByRole("img");
    expect(plain).toHaveAccessibleName(name);
    expect(plain).toHaveClass("tile", "tile-2");
    expect(plain).not.toHaveClass("faint");
    expect(plain).toBeEmptyDOMElement();
    expect(one).toHaveAccessibleName(`${name}: 1`);
    expect(one).toBeEmptyDOMElement();
    expect(three).toHaveAccessibleName(`${name}: 3`);
    expect(three).toHaveTextContent(/^3$/u);
  });

  it("dims an empty wall field and hides a tile that belongs to a named group", () => {
    const { container } = render(
      <>
        <Tile tile={0} faint />
        <Tile tile={4} decorative />
      </>,
    );

    expect(screen.getByRole("img")).toHaveClass("tile", "tile-0", "faint");
    const hidden = container.querySelector(".tile-4");
    expect(hidden).toHaveAttribute("aria-hidden", "true");
    expect(hidden).not.toHaveAttribute("role");
    expect(hidden?.className).toBe("tile tile-4");
  });
});
