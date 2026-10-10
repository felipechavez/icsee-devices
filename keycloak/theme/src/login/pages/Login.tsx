// Basada en keycloakify/login/pages/Login.tsx (login.ftl), con el diseño de CamGuard:
// "¿La olvidaste?" junto a la contraseña, botón de texto Mostrar/Ocultar, "Seguir conectado"
// como interruptor y el error debajo de los campos (con sacudida).
import { useState } from "react";
import { kcSanitize } from "keycloakify/lib/kcSanitize";
import { useIsPasswordRevealed } from "keycloakify/tools/useIsPasswordRevealed";
import { clsx } from "keycloakify/tools/clsx";
import type { PageProps } from "keycloakify/login/pages/PageProps";
import { getKcClsx } from "keycloakify/login/lib/kcClsx";
import { useScript } from "keycloakify/login/pages/Login.useScript";
import type { KcContext } from "../KcContext";
import type { I18n } from "../i18n";

export default function Login(props: PageProps<Extract<KcContext, { pageId: "login.ftl" }>, I18n>) {
    const { kcContext, i18n, doUseDefaultCss, Template, classes } = props;

    const { kcClsx } = getKcClsx({ doUseDefaultCss, classes });

    const { social, realm, url, usernameHidden, login, auth, registrationDisabled, messagesPerField, enableWebAuthnConditionalUI, authenticators } =
        kcContext;

    const { msg, msgStr } = i18n;

    const [isLoginButtonDisabled, setIsLoginButtonDisabled] = useState(false);
    const [rememberMe, setRememberMe] = useState(!!login.rememberMe);

    const webAuthnButtonId = "authenticateWebAuthnButton";
    useScript({ webAuthnButtonId, kcContext, i18n });

    const hasError = messagesPerField.existsError("username", "password");

    return (
        <Template
            kcContext={kcContext}
            i18n={i18n}
            doUseDefaultCss={doUseDefaultCss}
            classes={classes}
            displayMessage={!hasError}
            headerNode={msg("loginAccountTitle")}
            displayInfo={realm.password && realm.registrationAllowed && !registrationDisabled}
            infoNode={
                <span>
                    {msg("noAccount")}{" "}
                    <a tabIndex={8} href={url.registrationUrl}>
                        {msg("doRegister")}
                    </a>
                </span>
            }
            socialProvidersNode={
                realm.password &&
                social?.providers !== undefined &&
                social.providers.length !== 0 && (
                    <div id="kc-social-providers" className={kcClsx("kcFormSocialAccountSectionClass")}>
                        <hr />
                        <h2>{msg("identity-provider-login-label")}</h2>
                        <ul className={kcClsx("kcFormSocialAccountListClass")}>
                            {social.providers.map(p => (
                                <li key={p.alias}>
                                    <a id={`social-${p.alias}`} className={kcClsx("kcFormSocialAccountListButtonClass")} href={p.loginUrl}>
                                        <span dangerouslySetInnerHTML={{ __html: kcSanitize(p.displayName) }} />
                                    </a>
                                </li>
                            ))}
                        </ul>
                    </div>
                )
            }
        >
            {realm.password && (
                <form
                    id="kc-form-login"
                    className={clsx("cg-login", hasError && "cg-shake")}
                    onSubmit={() => {
                        setIsLoginButtonDisabled(true);
                        return true;
                    }}
                    action={url.loginAction}
                    method="post"
                >
                    <div className="cg-login-fields">
                        {!usernameHidden && (
                            <div className={kcClsx("kcFormGroupClass")}>
                                <label htmlFor="username" className={kcClsx("kcLabelClass")}>
                                    {!realm.loginWithEmailAllowed
                                        ? msg("username")
                                        : !realm.registrationEmailAsUsername
                                          ? msg("usernameOrEmail")
                                          : msg("email")}
                                </label>
                                <input
                                    tabIndex={2}
                                    id="username"
                                    className={kcClsx("kcInputClass")}
                                    name="username"
                                    defaultValue={login.username ?? ""}
                                    type="text"
                                    placeholder="admin"
                                    autoFocus
                                    autoComplete={enableWebAuthnConditionalUI ? "username webauthn" : "username"}
                                    aria-invalid={hasError}
                                />
                            </div>
                        )}

                        <div className={kcClsx("kcFormGroupClass")}>
                            <div className="cg-label-row">
                                <label htmlFor="password" className={kcClsx("kcLabelClass")}>
                                    {msg("password")}
                                </label>
                                {realm.resetPasswordAllowed && (
                                    <a tabIndex={6} href={url.loginResetCredentialsUrl} className="cg-label-link">
                                        {msg("doForgotPassword")}
                                    </a>
                                )}
                            </div>
                            <PasswordInput i18n={i18n} className={kcClsx("kcInputGroup")} inputClassName={clsx(kcClsx("kcInputClass"), "cg-input-reveal")} invalid={hasError} />
                        </div>

                        {hasError && (
                            <div
                                id="input-error"
                                className="cg-alert cg-alert-error cg-alert-inline"
                                aria-live="polite"
                                dangerouslySetInnerHTML={{ __html: kcSanitize(messagesPerField.getFirstError("username", "password")) }}
                            />
                        )}
                    </div>

                    <div className="cg-login-actions">
                        <input type="hidden" id="id-hidden-input" name="credentialId" value={auth.selectedCredential} />
                        <button
                            tabIndex={7}
                            disabled={isLoginButtonDisabled}
                            className={kcClsx("kcButtonClass", "kcButtonPrimaryClass", "kcButtonBlockClass")}
                            name="login"
                            id="kc-login"
                            type="submit"
                        >
                            {isLoginButtonDisabled ? msg("camguardVerifying") : msgStr("doLogIn")}
                        </button>

                        {realm.rememberMe && !usernameHidden && (
                            <label className="cg-remember">
                                {/* El checkbox real va al formulario; el interruptor es solo su apariencia. */}
                                <input
                                    tabIndex={5}
                                    id="rememberMe"
                                    name="rememberMe"
                                    type="checkbox"
                                    className="cg-sr-only"
                                    checked={rememberMe}
                                    onChange={e => setRememberMe(e.target.checked)}
                                />
                                <span className="cg-toggle" aria-hidden="true" />
                                {msg("rememberMe")}
                            </label>
                        )}
                    </div>
                </form>
            )}

            {enableWebAuthnConditionalUI && (
                <>
                    <form id="webauth" action={url.loginAction} method="post">
                        <input type="hidden" id="clientDataJSON" name="clientDataJSON" />
                        <input type="hidden" id="authenticatorData" name="authenticatorData" />
                        <input type="hidden" id="signature" name="signature" />
                        <input type="hidden" id="credentialId" name="credentialId" />
                        <input type="hidden" id="userHandle" name="userHandle" />
                        <input type="hidden" id="error" name="error" />
                    </form>
                    {authenticators !== undefined && authenticators.authenticators.length !== 0 && (
                        <form id="authn_select" className={kcClsx("kcFormClass")}>
                            {authenticators.authenticators.map((authenticator, i) => (
                                <input key={i} type="hidden" name="authn_use_chk" readOnly value={authenticator.credentialId} />
                            ))}
                        </form>
                    )}
                    <input
                        id={webAuthnButtonId}
                        type="button"
                        className={kcClsx("kcButtonClass", "kcButtonDefaultClass", "kcButtonBlockClass")}
                        value={msgStr("passkey-doAuthenticate")}
                    />
                </>
            )}
        </Template>
    );
}

/** Va aparte porque useIsPasswordRevealed busca el input al montar, y debe existir ya en el DOM. */
function PasswordInput(props: { i18n: I18n; className: string; inputClassName: string; invalid: boolean }) {
    const { i18n, className, inputClassName, invalid } = props;
    const { msg, msgStr } = i18n;
    const { isPasswordRevealed, toggleIsPasswordRevealed } = useIsPasswordRevealed({ passwordInputId: "password" });

    return (
        <div className={className}>
            <input
                tabIndex={3}
                id="password"
                className={inputClassName}
                name="password"
                type="password"
                placeholder="••••••••"
                autoComplete="current-password"
                aria-invalid={invalid}
            />
            <button
                type="button"
                className="cg-reveal"
                aria-controls="password"
                aria-label={msgStr(isPasswordRevealed ? "hidePassword" : "showPassword")}
                onClick={toggleIsPasswordRevealed}
                tabIndex={4}
            >
                {isPasswordRevealed ? msg("camguardHide") : msg("camguardShow")}
            </button>
        </div>
    );
}
