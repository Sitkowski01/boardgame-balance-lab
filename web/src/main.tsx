import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";
import { connectHub } from "./api/hub";
import { labClient } from "./lab/client";
import { storedTickets } from "./table/tickets";
import "./styles.css";
import "./pages.css";

const root = document.getElementById("root");
if (root !== null) {
  createRoot(root).render(
    <StrictMode>
      <App connect={connectHub} tickets={storedTickets()} lab={labClient()} />
    </StrictMode>,
  );
}
