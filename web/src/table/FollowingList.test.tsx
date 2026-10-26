import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { FollowingList } from "./FollowingList";

/** jsdom nie liczy ukladu: wysokosc listy i przewijanie podstawiamy recznie. */
function overflowing(list: HTMLElement, scrollHeight: number) {
  Object.defineProperty(list, "scrollHeight", { configurable: true, value: scrollHeight });
}

describe("FollowingList", () => {
  it("scrolls to the newest move after each one and fades the hidden top", () => {
    const items = (n: number) => Array.from({ length: n }, (_, i) => <li key={i}>ruch {i + 1}</li>);
    const { rerender } = render(<FollowingList count={1}>{items(1)}</FollowingList>);
    const list = screen.getByRole("list");
    expect(list).not.toHaveClass("scrolled");

    overflowing(list, 900);
    rerender(<FollowingList count={30}>{items(30)}</FollowingList>);
    expect(list.scrollTop).toBe(900);
    expect(list).toHaveClass("scrolled");

    list.scrollTop = 0;
    fireEvent.scroll(list);
    expect(list).not.toHaveClass("scrolled");
  });
});
