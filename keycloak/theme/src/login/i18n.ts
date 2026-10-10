import { i18nBuilder } from "keycloakify/login";
import type { ThemeName } from "../kc.gen";

/** @see: https://docs.keycloakify.dev/features/i18n */
const { useI18n, ofTypeI18n } = i18nBuilder
    .withThemeName<ThemeName>()
    .withCustomTranslations({
        es: {
            loginAccountTitle: "Hola de nuevo.",
            loginTitle: "CamGuard",
            loginTitleHtml: "CamGuard",
            updatePasswordTitle: "Elige una contraseña nueva",
            emailForgotTitle: "¿Olvidaste tu contraseña?",
            emailInstruction:
                "Escribe tu usuario o correo y te enviaremos un enlace para crear una contraseña nueva.",
            updatePasswordMessage: "Por seguridad, elige una contraseña nueva antes de continuar.",
            backToLogin: "← Volver al inicio de sesión",
            doSubmit: "Continuar",
            doForgotPassword: "¿La olvidaste?",
            rememberMe: "Seguir conectado en este dispositivo",
            doLogIn: "Iniciar sesión",
            usernameOrEmail: "Usuario o email",
            camguardFooter: "CamGuard · Acceso privado",
            camguardLoginSubtitle: "Tus cámaras siguen grabando. Identifícate para volver a verlas.",
            camguardShow: "Mostrar",
            camguardHide: "Ocultar",
            camguardVerifying: "Verificando…",
            camguardShutterTitle: "OBTURADOR DE PRIVACIDAD",
            camguardShutterText: "Nadie mira mientras escribes.",
            camguardSignalLocked: "SEÑAL CIFRADA",
            camguardSignalVerifying: "VERIFICANDO",
            camguardLockedMessage: "Señal cifrada · identifícate para ver",
            camguardVisualFootLeft: "ACCESO PRIVADO · CONEXIÓN SEGURA",
            camguardVisualFootRight: "CAMGUARD · VIGILANCIA LOCAL"
        }
    })
    .build();

type I18n = typeof ofTypeI18n;

export { useI18n, type I18n };
