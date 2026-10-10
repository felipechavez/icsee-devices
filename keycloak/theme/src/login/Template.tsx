// Plantilla propia (basada en keycloakify/login/Template.tsx): sin CSS de Keycloak/PatternFly.
// Rediseño (designs/CamGuard_Rediseño/Login.dc.html): panel de "señal cifrada" a la izquierda con
// obturador de privacidad que se cierra al escribir la contraseña, y el formulario a la derecha.
import { useEffect, useState } from "react";
import { kcSanitize } from "keycloakify/lib/kcSanitize";
import type { TemplateProps } from "keycloakify/login/TemplateProps";
import { getKcClsx } from "keycloakify/login/lib/kcClsx";
import { useSetClassName } from "keycloakify/tools/useSetClassName";
import { useInitialize } from "keycloakify/login/Template.useInitialize";
import type { I18n } from "./i18n";
import type { KcContext } from "./KcContext";
import feedUrl from "./assets/feed.png";
import "@fontsource-variable/geist";
import "@fontsource-variable/geist-mono";
import "./main.css";

// Mosaico de 4 "cámaras" a partir de la misma imagen, con encuadres distintos.
const feeds = ["none", "scaleX(-1) scale(1.25)", "scale(1.6) translate(8%, 6%)", "scaleX(-1) scale(1.4) translate(-6%, -4%)"];

type Phase = "idle" | "typing" | "busy";

const pad = (n: number) => String(n).padStart(2, "0");
const formatClock = (d: Date) =>
    `${pad(d.getDate())}/${pad(d.getMonth() + 1)}/${d.getFullYear()}  ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;

/** Fase del panel: el obturador se cierra con el foco en una contraseña y se verifica al enviar. */
function usePhase(): Phase {
    const [typing, setTyping] = useState(false);
    const [busy, setBusy] = useState(false);

    useEffect(() => {
        const isPassword = (t: EventTarget | null) => t instanceof HTMLInputElement && t.type === "password";
        const onFocusIn = (e: FocusEvent) => setTyping(isPassword(e.target));
        const onFocusOut = (e: FocusEvent) => isPassword(e.target) && !isPassword(e.relatedTarget) && setTyping(false);
        const onSubmit = () => {
            setTyping(false);
            setBusy(true);
        };
        document.addEventListener("focusin", onFocusIn);
        document.addEventListener("focusout", onFocusOut);
        document.addEventListener("submit", onSubmit);
        return () => {
            document.removeEventListener("focusin", onFocusIn);
            document.removeEventListener("focusout", onFocusOut);
            document.removeEventListener("submit", onSubmit);
        };
    }, []);

    return busy ? "busy" : typing ? "typing" : "idle";
}

function SignalPanel({ i18n }: { i18n: I18n }) {
    const { msg } = i18n;
    const phase = usePhase();
    const [now, setNow] = useState(() => new Date());

    useEffect(() => {
        const timer = setInterval(() => setNow(new Date()), 1000);
        return () => clearInterval(timer);
    }, []);

    return (
        <section className="cg-visual" data-phase={phase} aria-hidden="true">
            <div className="cg-feeds">
                {feeds.map((transform, i) => (
                    <div key={i} className="cg-feed">
                        <img src={feedUrl} alt="" style={{ transform }} />
                    </div>
                ))}
            </div>
            <div className="cg-scanlines" />
            <div className="cg-vignette" />

            <div className="cg-shutter">
                <div />
                <div />
            </div>
            <div className="cg-shutter-label">
                <span>{msg("camguardShutterTitle")}</span>
                <strong>{msg("camguardShutterText")}</strong>
            </div>

            <span className="cg-corner cg-corner-tl" />
            <span className="cg-corner cg-corner-tr" />
            <span className="cg-corner cg-corner-bl" />
            <span className="cg-corner cg-corner-br" />

            <div className="cg-signal">
                <span className="cg-signal-dot" />
                {phase === "busy" ? msg("camguardSignalVerifying") : msg("camguardSignalLocked")}
            </div>
            <div className="cg-clock">{formatClock(now)}</div>

            <div className="cg-locked">
                <div className="cg-reticle">
                    <span />
                    <span />
                    <span />
                </div>
                <span>{phase === "busy" ? msg("camguardVerifying") : msg("camguardLockedMessage")}</span>
            </div>

            <div className="cg-visual-foot">
                <span>{msg("camguardVisualFootLeft")}</span>
                <span>{msg("camguardVisualFootRight")}</span>
            </div>
        </section>
    );
}

export default function Template(props: TemplateProps<KcContext, I18n>) {
    const {
        displayInfo = false,
        displayMessage = true,
        displayRequiredFields = false,
        headerNode,
        socialProvidersNode = null,
        infoNode = null,
        documentTitle,
        bodyClassName,
        kcContext,
        i18n,
        doUseDefaultCss,
        classes,
        children
    } = props;

    const { kcClsx } = getKcClsx({ doUseDefaultCss, classes });

    const { msg, msgStr } = i18n;

    const { realm, auth, url, message, isAppInitiatedAction } = kcContext;

    useEffect(() => {
        document.title = documentTitle ?? msgStr("loginTitle", realm.displayName || realm.name);
    }, []);

    useSetClassName({ qualifiedName: "html", className: kcClsx("kcHtmlClass") });
    useSetClassName({ qualifiedName: "body", className: bodyClassName ?? kcClsx("kcBodyClass") });

    const { isReadyToRender } = useInitialize({ kcContext, doUseDefaultCss });

    if (!isReadyToRender) {
        return null;
    }

    const showAttemptedUsername = auth !== undefined && auth.showUsername && !auth.showResetCredentials;

    return (
        <div className={kcClsx("kcLoginClass")}>
            <SignalPanel i18n={i18n} />

            <main className="cg-main">
                <header className="cg-brand">
                    <span className="cg-brand-mark" aria-hidden="true" />
                    CamGuard
                </header>

                <div className="cg-center">
                    <section className={kcClsx("kcFormCardClass")}>
                        <header className={kcClsx("kcFormHeaderClass")}>
                            <h1 id="kc-page-title" className="cg-title">
                                {headerNode}
                            </h1>
                            {kcContext.pageId === "login.ftl" && <p className="cg-subtitle">{msg("camguardLoginSubtitle")}</p>}
                            {showAttemptedUsername && (
                                <div id="kc-username" className="cg-attempted-user">
                                    <span id="kc-attempted-username">{auth.attemptedUsername}</span>
                                    <a id="reset-login" href={url.loginRestartFlowUrl} aria-label={msgStr("restartLoginTooltip")}>
                                        {msg("restartLoginTooltip")}
                                    </a>
                                </div>
                            )}
                            {displayRequiredFields && (
                                <p className="cg-required-note">
                                    <span className="cg-required">*</span> {msg("requiredFields")}
                                </p>
                            )}
                        </header>

                        {/* Las acciones iniciadas por la app no deben ver avisos sobre completar la acción. */}
                        {displayMessage && message !== undefined && (message.type !== "warning" || !isAppInitiatedAction) && (
                            <div className={`cg-alert cg-alert-${message.type}`} role={message.type === "error" ? "alert" : "status"}>
                                <span className={`cg-icon cg-icon-${message.type}`} aria-hidden="true" />
                                <span className="cg-alert-text" dangerouslySetInnerHTML={{ __html: kcSanitize(message.summary) }} />
                            </div>
                        )}

                        <div id="kc-content">
                            {children}

                            {auth !== undefined && auth.showTryAnotherWayLink && (
                                <form id="kc-select-try-another-way-form" action={url.loginAction} method="post">
                                    <div className={kcClsx("kcFormGroupClass")}>
                                        <input type="hidden" name="tryAnotherWay" value="on" />
                                        <a
                                            href="#"
                                            id="try-another-way"
                                            onClick={event => {
                                                document.forms["kc-select-try-another-way-form" as never].requestSubmit();
                                                event.preventDefault();
                                                return false;
                                            }}
                                        >
                                            {msg("doTryAnotherWay")}
                                        </a>
                                    </div>
                                </form>
                            )}

                            {socialProvidersNode}

                            {displayInfo && (
                                <div id="kc-info" className={kcClsx("kcSignUpClass")}>
                                    {infoNode}
                                </div>
                            )}
                        </div>
                    </section>
                </div>

                <footer className="cg-footer">{msg("camguardFooter")}</footer>
            </main>
        </div>
    );
}
