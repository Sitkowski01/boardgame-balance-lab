import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App, type AppProps } from "./App";
import { labClient } from "./lab/client";
import { storedTickets } from "./table/tickets";
import { applyTheme, loadTheme } from "./theme";
import "@fontsource-variable/inter/index.css";
import "@fontsource-variable/fraunces/index.css";
import "./styles.css";
import "./pages.css";

/** Prawdziwy serwer: hub SignalR i REST pod tym samym adresem co strona. */
async function serverProps(): Promise<AppProps> {
  const { connectHub } = await import("./api/hub");
  return { connect: connectHub, tickets: storedTickets(), lab: labClient() };
}

/**
 * Demo (build z --mode demo): serwer gry w Web Workerach (.NET w WebAssembly). Strona rozmawia z nim tym samym
 * kontraktem co z hubem, a zadania do /api przejmuje fetch demo — reszta aplikacji nie wie o roznicy.
 */
async function demoProps(): Promise<AppProps> {
  const { startDemo } = await import("./demo/boot");
  const demo = startDemo(window.fetch.bind(window));
  window.fetch = demo.fetch;
  return { connect: demo.connect, tickets: storedTickets(), lab: labClient(), demo: { ready: demo.ready } };
}

// Motyw wybrany wczesniej przed pierwszym renderem: strona nie mignie w zlym kolorze.
applyTheme(loadTheme());
const root = document.getElementById("root");
if (root !== null) {
  const props = await (import.meta.env.MODE === "demo" ? demoProps() : serverProps());
  createRoot(root).render(
    <StrictMode>
      <App {...props} />
    </StrictMode>,
  );
}
