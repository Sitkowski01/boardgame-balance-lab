import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";
import { connectHub } from "./api/hub";
import { labClient } from "./lab/client";
import { storedTickets } from "./table/tickets";
import { applyTheme, loadTheme } from "./theme";
import "@fontsource-variable/inter/index.css";
import "@fontsource-variable/fraunces/index.css";
import "./styles.css";
import "./pages.css";

// Motyw wybrany wczesniej przed pierwszym renderem: strona nie mignie w zlym kolorze.
applyTheme(loadTheme());
const root = document.getElementById("root");
if (root !== null) {
  createRoot(root).render(
    <StrictMode>
      <App connect={connectHub} tickets={storedTickets()} lab={labClient()} />
    </StrictMode>,
  );
}
