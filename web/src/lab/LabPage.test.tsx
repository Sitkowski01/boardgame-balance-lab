import { act, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "../api/rest";
import { experiment, fakeLab } from "../test/fakeLab";
import { games, tournament } from "../test/labReports";
import { LabPage } from "./LabPage";
import { PollEvery } from "./useExperiment";

afterEach(() => {
  vi.useRealTimers();
});

function page(client = fakeLab(), experimentId: string | null = null, loadGames = () => Promise.resolve(games)) {
  const navigate = vi.fn<(path: string) => void>();
  const view = render(<LabPage client={client} loadGames={loadGames} experiment={experimentId} navigate={navigate} />);
  return { client, navigate, view, user: userEvent.setup() };
}

const id = experiment().id;

describe("LabPage", () => {
  it("orders an experiment from the form and opens its address", async () => {
    const { client, navigate, user } = page();

    const form = await screen.findByRole("form", { name: "Nowy eksperyment" });
    await user.selectOptions(within(form).getByLabelText("Gra"), "mozaika");
    await user.click(within(form).getByRole("button", { name: "Uruchom eksperyment" }));

    expect(client.submit).toHaveBeenCalledWith({
      game: "mozaika",
      kind: "Tournament",
      bots: ["greedy", "greedy:kolumny"],
      games: 100,
      seed: "1",
      variant: null,
    });
    expect(navigate).toHaveBeenCalledWith(`/lab/${id}`);
  });

  it("builds a comparison with a variant and its own bots", async () => {
    const { client, user } = page();

    const form = await screen.findByRole("form", { name: "Nowy eksperyment" });
    await user.selectOptions(within(form).getByLabelText("Gra"), "klejnoty");
    await user.click(within(form).getByLabelText("Co jeśli"));
    expect(within(form).getByText(/Ten sam turniej na oryginale/u)).toBeInTheDocument();
    expect(within(form).getByRole("button", { name: "Uruchom eksperyment" })).toBeDisabled();
    expect(within(form).getByText("Co jeśli potrzebuje wariantu zasad — to on jest zmianą.")).toBeInTheDocument();
    await user.type(within(form).getByLabelText(/Wariant \(co jeśli\)/u), "win=13");
    await user.click(within(form).getByRole("button", { name: "Usuń: Zachłanny: punkty" }));
    await user.selectOptions(within(form).getByLabelText("Bot do dodania"), "ismcts:50");
    await user.click(within(form).getByRole("button", { name: "Dodaj bota" }));
    await user.clear(within(form).getByLabelText("Partie"));
    await user.type(within(form).getByLabelText("Partie"), "60");
    await user.clear(within(form).getByLabelText("Ziarno"));
    await user.type(within(form).getByLabelText("Ziarno"), "99");
    expect(within(form).getByText("zmiany po srednikach: win=N")).toBeInTheDocument();
    await user.click(within(form).getByRole("button", { name: "Uruchom eksperyment" }));

    expect(client.submit).toHaveBeenCalledWith({
      game: "klejnoty",
      kind: "Comparison",
      bots: ["greedy", "ismcts:50"],
      games: 60,
      seed: "99",
      variant: "win=13",
    });
  });

  it("explains a refused order and lets the user fix it", async () => {
    const client = fakeLab();
    client.submit.mockRejectedValueOnce(new ApiError("W kolejce jest juz 4 eksperymentow.", 503)).mockRejectedValueOnce(new Error(""));
    const { user } = page(client);

    const run = await screen.findByRole("button", { name: "Uruchom eksperyment" });
    await user.click(run);
    expect(await screen.findByRole("alert")).toHaveTextContent("W kolejce jest juz 4 eksperymentow.");
    await user.click(run);
    expect(await screen.findByRole("alert")).toHaveTextContent("Serwer nie przyjął eksperymentu.");
    await user.clear(screen.getByLabelText("Ziarno"));
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("switching to a league keeps the seed and gives three bots, a two-player game keeps its count", async () => {
    const { user } = page();

    const form = await screen.findByRole("form", { name: "Nowy eksperyment" });
    await user.clear(within(form).getByLabelText("Ziarno"));
    await user.type(within(form).getByLabelText("Ziarno"), "5");
    await user.click(within(form).getByLabelText("Liga"));

    expect(within(form).getByLabelText("Ziarno")).toHaveValue("5");
    expect(within(form).getByLabelText("Partie na parę")).toHaveValue("40");
    expect(within(within(form).getByRole("list", { name: "Wybrane boty" })).getAllByRole("listitem")).toHaveLength(2); // ConnectFour: mcts:50, random
    expect(within(form).queryByLabelText(/Wariant/u)).not.toBeInTheDocument();
  });

  it("stops adding bots at the server limit", async () => {
    const { user } = page();
    const add = await screen.findByRole("button", { name: "Dodaj bota" });

    for (let i = 0; i < 4; i++) await user.click(add);

    expect(add).toBeDisabled();
    expect(screen.getByText(/ConnectFour to gra dla 2–2 graczy/u)).toBeInTheDocument();
  });

  it("follows a running experiment, shows its progress and can cancel it", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const client = fakeLab(experiment({ status: "Running", done: 30 }));
    page(client, id);

    const bar = await screen.findByRole("progressbar", { name: "Postęp eksperymentu" });
    expect(bar).toHaveAttribute("aria-valuenow", "30");
    expect(bar).toHaveAttribute("aria-valuetext", "30 z 100 partii");
    expect(screen.getByText("liczy się", { selector: ".progress-head .status" })).toBeInTheDocument();

    client.get.mockResolvedValue(experiment({ status: "Done", done: 100, result: tournament() }));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(PollEvery);
    });
    expect(await screen.findByRole("region", { name: "Wyniki turnieju" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Anuluj" })).not.toBeInTheDocument();
    const calls = client.get.mock.calls.length;
    await act(async () => {
      await vi.advanceTimersByTimeAsync(PollEvery * 3);
    });
    expect(client.get.mock.calls.length).toBe(calls); // po koncu juz nie pyta
  });

  it("cancels and shows a failure with its reason", async () => {
    const client = fakeLab(experiment({ status: "Running" }));
    const { user } = page(client, id);

    await user.click(await screen.findByRole("button", { name: "Anuluj" }));
    expect(client.cancel).toHaveBeenCalledWith(id);

    client.cancel.mockRejectedValueOnce(new Error("siec"));
    await user.click(screen.getByRole("button", { name: "Anuluj" }));
  });

  it("shows the reason of a failed experiment and a result it cannot read", async () => {
    const failed = page(fakeLab(experiment({ status: "Failed", error: "bot sie wywrocil" })), id);
    expect(await screen.findByText("bot sie wywrocil")).toBeInTheDocument();
    failed.view.unmount();

    page(fakeLab(experiment({ status: "Done", result: { cos: 1 } })), id);
    expect(await screen.findByText(/wynik, którego ta wersja strony nie rozumie/u)).toBeInTheDocument();
  });

  it("says when the experiment cannot be loaded", async () => {
    const client = fakeLab();
    client.get.mockRejectedValueOnce(new ApiError("Nie ma takiej strony.", 404));
    const { view } = page(client, id);
    expect(await screen.findByText("Nie ma takiej strony.")).toBeInTheDocument(); // 404: bez ponawiania
    view.unmount();

    const other = fakeLab();
    other.get.mockRejectedValueOnce(new Error(""));
    page(other, id);
    expect(await screen.findByText("Nie udało się pobrać eksperymentu. Ponawiam…")).toBeInTheDocument();
  });
});
