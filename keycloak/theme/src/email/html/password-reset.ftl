<#--
  This file has been claimed for ownership from @keycloakify/email-native version 260007.0.0.
  To relinquish ownership and restore this file to its original content, run the following command:
  
  $ npx keycloakify own --path "email/html/password-reset.ftl" --revert
-->
<#import "template.ftl" as layout>
<@layout.emailLayout>
<p style="margin-top:0;">${msg("camguardPasswordResetIntro", user.username)}</p>
<@layout.actionButton href=link label=msg("camguardPasswordResetButton") />
<p>${msg("camguardLinkExpiration", linkExpirationFormatter(linkExpiration))}</p>
<p style="color:#5f6873;">${msg("camguardPasswordResetIgnore")}</p>
</@layout.emailLayout>
