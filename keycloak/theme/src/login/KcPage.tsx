import { Suspense, lazy } from "react";
import type { ClassKey } from "keycloakify/login";
import type { KcContext } from "./KcContext";
import { useI18n } from "./i18n";
import DefaultPage from "keycloakify/login/DefaultPage";
import Template from "./Template";
const Login = lazy(() => import("./pages/Login"));
const UserProfileFormFields = lazy(() => import("keycloakify/login/UserProfileFormFields"));

const doMakeUserConfirmPassword = true;

// Sin el CSS de Keycloak (doUseDefaultCss=false): cada página usa las clases cg-* de main.css.
export default function KcPage(props: { kcContext: KcContext }) {
    const { kcContext } = props;

    const { i18n } = useI18n({ kcContext });

    return (
        <Suspense>
            {(() => {
                switch (kcContext.pageId) {
                    case "login.ftl":
                        return <Login kcContext={kcContext} i18n={i18n} classes={classes} Template={Template} doUseDefaultCss={false} />;
                    default:
                        return (
                            <DefaultPage
                                kcContext={kcContext}
                                i18n={i18n}
                                classes={classes}
                                Template={Template}
                                doUseDefaultCss={false}
                                UserProfileFormFields={UserProfileFormFields}
                                doMakeUserConfirmPassword={doMakeUserConfirmPassword}
                            />
                        );
                }
            })()}
        </Suspense>
    );
}

const classes = {
    kcHtmlClass: "cg-html",
    kcBodyClass: "cg-body",
    kcLoginClass: "cg-page",
    kcFormCardClass: "cg-card",
    kcFormHeaderClass: "cg-card-header",
    kcFormClass: "cg-form",
    kcFormGroupClass: "cg-field",
    kcFormGroupErrorClass: "cg-field-invalid",
    kcLabelWrapperClass: "cg-label-wrapper",
    kcLabelClass: "cg-label",
    kcInputWrapperClass: "cg-input-wrapper",
    kcInputClass: "cg-input",
    kcTextareaClass: "cg-input",
    kcInputGroup: "cg-input-group",
    kcInputErrorMessageClass: "cg-field-error",
    kcInputHelperTextBeforeClass: "cg-help",
    kcInputHelperTextAfterClass: "cg-help",
    kcFormPasswordVisibilityButtonClass: "cg-input-toggle",
    kcFormPasswordVisibilityIconShow: "cg-icon cg-icon-eye",
    kcFormPasswordVisibilityIconHide: "cg-icon cg-icon-eye-off",
    kcFormSettingClass: "cg-form-settings",
    kcFormOptionsClass: "cg-form-options",
    kcFormOptionsWrapperClass: "cg-form-options-wrapper",
    kcFormButtonsClass: "cg-actions",
    kcButtonClass: "cg-btn",
    kcButtonPrimaryClass: "cg-btn-primary",
    kcButtonDefaultClass: "cg-btn-secondary",
    kcButtonSecondaryClass: "cg-btn-secondary",
    kcButtonBlockClass: "cg-btn-block",
    kcCheckboxInputClass: "cg-checkbox",
    kcInputClassCheckbox: "cg-check",
    kcInputClassCheckboxInput: "cg-checkbox",
    kcInputClassCheckboxLabel: "cg-check-label",
    kcInputClassRadio: "cg-check",
    kcInputClassRadioInput: "cg-checkbox",
    kcInputClassRadioLabel: "cg-check-label",
    kcSignUpClass: "cg-info",
    kcSrOnlyClass: "cg-sr-only",
    kcFeedbackErrorIcon: "cg-icon cg-icon-error",
    kcFeedbackWarningIcon: "cg-icon cg-icon-warning",
    kcFeedbackSuccessIcon: "cg-icon cg-icon-success",
    kcFeedbackInfoIcon: "cg-icon cg-icon-info",
    kcAlertClass: "cg-alert",
    kcAlertTitleClass: "cg-alert-text",
    kcFormSocialAccountSectionClass: "cg-social",
    kcFormSocialAccountListClass: "cg-social-list",
    kcFormSocialAccountListButtonClass: "cg-btn cg-btn-secondary cg-btn-block"
} satisfies { [key in ClassKey]?: string };
