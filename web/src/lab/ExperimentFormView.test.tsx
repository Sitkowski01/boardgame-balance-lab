import { act, fireEvent, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import type { GameInfo } from "../api/schemas";
import { games } from "../test/labReports";
import { ExperimentFormView } from "./ExperimentFormView";
import type { ExperimentRequest } from "./model";

type Submit = (request: ExperimentRequest) => Promise<void>;

function view(onSubmit = vi.fn<Submit>(() => Promise.resolve()), catalog: readonly GameInfo[] = games) {
  render(<ExperimentFormView games={catalog} onSubmit={onSubmit} />);
  return { onSubmit, user: userEvent.setup(), form: screen.getByRole("form", { name: "Nowy eksperyment" }) };
}

const run = (form: HTMLElement): HTMLElement => within(form).getByRole("button", { name: "Uruchom eksperyment" });

describe("ExperimentFormView", () => {
  it("marks the chosen kind in one group of radios and explains it", async () => {
    const { user, form } = view();
    const radios = within(form).getAllByRole<HTMLInputElement>("radio");
    const state = () => radios.map((r) => [r.checked, r.closest("label")?.className]);

    expect(state()).toEqual([
      [true, "chosen"],
      [false, ""],
      [false, ""],
    ]);
    expect(new Set(radios.map((r) => r.name)).size).toBe(1);
    expect(radios[0]?.name).toMatch(/.+-kind$/u);
    expect(within(form).getByText(/^Boty grają przy jednym stole/u)).toHaveClass("hint");

    await user.click(within(form).getByLabelText("Liga"));

    expect(state()).toEqual([
      [false, ""],
      [true, "chosen"],
      [false, ""],
    ]);
    expect(within(form).getByText(/^Każdy bot z każdym w partiach dwuosobowych/u)).toBeInTheDocument();
    expect(within(form).getByLabelText("Turniej")).not.toBeChecked();
  });

  it("lists the games under their names from the lobby", () => {
    const { form } = view();

    const options = within(within(form).getByLabelText("Gra")).getAllByRole("option");
    expect(options.map((o) => [o.getAttribute("value"), o.textContent])).toEqual([
      ["connectfour", "Czwórki"],
      ["klejnoty", "Klejnoty"],
      ["mozaika", "Mozaika"],
    ]);
  });

  it("drops spaces typed around the numbers", async () => {
    const { user, form } = view();

    await user.clear(within(form).getByLabelText("Partie"));
    await user.type(within(form).getByLabelText("Partie"), " 60 ");
    await user.clear(within(form).getByLabelText("Ziarno"));
    await user.type(within(form).getByLabelText("Ziarno"), " 7 ");

    expect(within(form).getByLabelText("Partie")).toHaveValue("60");
    expect(within(form).getByLabelText("Ziarno")).toHaveValue("7");
    expect(run(form)).toBeEnabled();
    expect(within(form).queryByRole("list", { name: "Do poprawienia" })).toBeNull();
  });

  it("names the variant field after the kind and shows the syntax only when the game gives one", async () => {
    const { user, form } = view();
    await user.selectOptions(within(form).getByLabelText("Gra"), "klejnoty");

    const optional = within(form).getByLabelText(/^Wariant zasad \(opcjonalnie\)/u);
    expect(optional).toHaveAttribute("placeholder", "puste = oryginał");
    await user.click(within(form).getByLabelText("Co jeśli"));
    expect(within(form).getByLabelText(/^Wariant \(co jeśli\)/u)).toHaveAttribute("placeholder", "zmiana zasad — składnia poniżej");
    expect(form.querySelector(".variant .hint")).toHaveTextContent(/^Zmiany oddzielaj średnikami: win=N$/u);
  });

  it("leaves out the syntax hint of a game without one", () => {
    const { form } = view(undefined, [{ ...(games[1] as GameInfo), variantSyntax: null }]);

    expect(form.querySelector(".variant")).not.toBeNull();
    expect(form.querySelector(".variant .hint")).toBeNull();
  });

  it("sends once, says so until the server answers and never reloads the page", async () => {
    let answer: () => void = () => undefined;
    const onSubmit = vi.fn<Submit>(
      () =>
        new Promise<void>((resolve) => {
          answer = resolve;
        }),
    );
    const { form } = view(onSubmit);

    expect(fireEvent.submit(form)).toBe(false);
    expect(within(form).getByRole("button", { name: "Wysyłanie…" })).toBeDisabled();
    fireEvent.submit(form);
    expect(onSubmit).toHaveBeenCalledTimes(1);

    await act(async () => {
      answer();
      await Promise.resolve();
    });
    expect(run(form)).toBeEnabled();
  });

  it("does not send a form with problems", async () => {
    const { user, form, onSubmit } = view();
    await user.clear(within(form).getByLabelText("Partie"));
    await user.type(within(form).getByLabelText("Partie"), "x");

    expect(fireEvent.submit(form)).toBe(false);
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("clears the server's refusal when the game or the kind changes", async () => {
    const { user, form } = view(vi.fn<Submit>(() => Promise.reject(new Error("Kolejka pelna."))));

    await user.click(run(form));
    expect(await within(form).findByRole("alert")).toHaveTextContent("Kolejka pelna.");
    await user.selectOptions(within(form).getByLabelText("Gra"), "klejnoty");
    expect(within(form).queryByRole("alert")).toBeNull();

    await user.click(run(form));
    expect(await within(form).findByRole("alert")).toHaveTextContent("Kolejka pelna.");
    await user.click(within(form).getByLabelText("Liga"));
    expect(within(form).queryByRole("alert")).toBeNull();
  });
});
