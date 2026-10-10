import { createRoot } from "react-dom/client";
import { StrictMode } from "react";
import { KcPage } from "./kc.gen";

// En desarrollo (npm run dev) se ve una página con datos de prueba:
// http://localhost:5173/?page=login-update-password.ftl  (por defecto login.ftl)
if (import.meta.env.DEV && !window.kcContext) {
    const { getKcContextMock } = await import("./login/KcPageStory");
    const params = new URLSearchParams(window.location.search);
    const pageId = (params.get("page") ?? "login.ftl") as Parameters<typeof getKcContextMock>[0]["pageId"];
    const messageType = params.get("message") as "error" | "warning" | "success" | "info" | null;
    window.kcContext = getKcContextMock({
        pageId,
        overrides: {
            locale: { currentLanguageTag: "es" },
            ...(messageType !== null && {
                message: { type: messageType, summary: "Usuario o contraseña incorrectos." }
            })
        }
    });
}

createRoot(document.getElementById("root")!).render(
    <StrictMode>{!window.kcContext ? <h1>No Keycloak Context</h1> : <KcPage kcContext={window.kcContext} />}</StrictMode>
);
