<#--
  This file has been claimed for ownership from @keycloakify/email-native version 260007.0.0.
  To relinquish ownership and restore this file to its original content, run the following command:
  
  $ npx keycloakify own --path "email/html/template.ftl" --revert
-->
<#-- Diseño común de los correos de CamGuard. Estilos en línea: muchos clientes de correo ignoran <style>. -->
<#macro emailLayout>
<html lang="${locale.language!'es'}">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
</head>
<body style="margin:0;padding:0;background:#f6f7f9;font-family:system-ui,-apple-system,'Segoe UI',Roboto,sans-serif;color:#15181d;line-height:1.5;">
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f6f7f9;padding:24px 12px;">
    <tr>
        <td align="center">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:520px;background:#ffffff;border:1px solid #dde1e6;border-radius:10px;">
                <tr>
                    <td style="padding:20px 28px;border-bottom:1px solid #dde1e6;font-size:18px;font-weight:600;">
                        <span style="color:#2563eb;">&#9679;</span> CamGuard
                    </td>
                </tr>
                <tr>
                    <td style="padding:24px 28px;font-size:15px;">
                        <#nested>
                    </td>
                </tr>
                <tr>
                    <td style="padding:16px 28px;border-top:1px solid #dde1e6;font-size:12px;color:#5f6873;">
                        ${msg("camguardEmailFooter")}
                    </td>
                </tr>
            </table>
        </td>
    </tr>
</table>
</body>
</html>
</#macro>

<#-- Botón de acción con el enlace de Keycloak. -->
<#macro actionButton href label>
<p style="margin:24px 0;">
    <a href="${href}" style="display:inline-block;background:#2563eb;color:#ffffff;text-decoration:none;padding:10px 20px;border-radius:8px;font-weight:600;">${label}</a>
</p>
<p style="font-size:12px;color:#5f6873;word-break:break-all;">${msg("camguardEmailLinkFallback")}<br><a href="${href}" style="color:#2563eb;">${href}</a></p>
</#macro>
