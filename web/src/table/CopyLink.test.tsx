import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { CopyLink } from "./CopyLink";

afterEach(() => {
  vi.restoreAllMocks();
});

describe("CopyLink", () => {
  it("copies the table address for friends", async () => {
    const user = userEvent.setup();
    const write = vi.spyOn(navigator.clipboard, "writeText").mockResolvedValue();
    render(<CopyLink href={() => "http://stol/rooms/1"} />);

    await user.click(screen.getByRole("button", { name: "Kopiuj link do stołu" }));

    expect(write).toHaveBeenCalledWith("http://stol/rooms/1");
    expect(await screen.findByRole("button", { name: "Skopiowano ✓" })).toBeInTheDocument();
    expect(screen.queryByLabelText("Link do stołu")).not.toBeInTheDocument();
  });

  it("shows the address to copy by hand when the clipboard refuses", async () => {
    const user = userEvent.setup();
    vi.spyOn(navigator.clipboard, "writeText").mockRejectedValue(new Error("nie wolno"));
    render(<CopyLink href={() => "http://stol/rooms/2"} />);

    await user.click(screen.getByRole("button", { name: "Kopiuj link do stołu" }));

    const field = await screen.findByLabelText("Link do stołu");
    expect(field).toHaveValue("http://stol/rooms/2");
    await user.click(field);
    expect((field as HTMLInputElement).selectionEnd).toBe("http://stol/rooms/2".length);
    expect(screen.getByRole("button", { name: "Kopiuj link do stołu" })).toBeInTheDocument();
  });

  it("copies the current page by default", async () => {
    const user = userEvent.setup();
    const write = vi.spyOn(navigator.clipboard, "writeText").mockResolvedValue();
    render(<CopyLink />);
    await user.click(screen.getByRole("button", { name: "Kopiuj link do stołu" }));
    expect(write).toHaveBeenCalledWith(window.location.href);
  });
});

describe("CopyLink without a clipboard", () => {
  it("falls back to the address field on plain http", async () => {
    const user = userEvent.setup();
    const clipboard = Object.getOwnPropertyDescriptor(navigator, "clipboard");
    Object.defineProperty(navigator, "clipboard", { value: undefined, configurable: true });
    try {
      render(<CopyLink href={() => "http://stol/rooms/3"} />);
      await user.click(screen.getByRole("button", { name: "Kopiuj link do stołu" }));
      expect(await screen.findByLabelText("Link do stołu")).toHaveValue("http://stol/rooms/3");
    } finally {
      if (clipboard !== undefined) Object.defineProperty(navigator, "clipboard", clipboard);
    }
  });
});

describe("CopyLink after copying", () => {
  it("goes back to the plain label so the link can be copied again", async () => {
    const user = userEvent.setup();
    vi.spyOn(navigator.clipboard, "writeText").mockResolvedValue();
    render(<CopyLink href={() => "http://stol"} reset={30} />);
    await user.click(screen.getByRole("button", { name: "Kopiuj link do stołu" }));
    expect(await screen.findByRole("button", { name: "Skopiowano ✓" })).toBeInTheDocument();
    expect(await screen.findByRole("button", { name: "Kopiuj link do stołu" })).toBeInTheDocument();
  });
});
