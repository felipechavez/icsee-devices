<#--
  This file has been claimed for ownership from @keycloakify/email-native version 260007.0.0.
  To relinquish ownership and restore this file to its original content, run the following command:
  
  $ npx keycloakify own --path "email/html/executeActions.ftl" --revert
-->
<#outputformat "plainText">
<#assign requiredActionsText><#if requiredActions??><#list requiredActions><#items as reqActionItem>${msg("requiredAction.${reqActionItem}")}<#sep>, </#sep></#items></#list></#if></#assign>
</#outputformat>
<#import "template.ftl" as layout>
<@layout.emailLayout>
<p style="margin-top:0;">${msg("camguardExecuteActionsIntro", user.username)}</p>
<#if requiredActionsText?has_content>
<p><strong>${requiredActionsText}</strong></p>
</#if>
<@layout.actionButton href=link label=msg("camguardExecuteActionsButton") />
<p>${msg("camguardLinkExpiration", linkExpirationFormatter(linkExpiration))}</p>
</@layout.emailLayout>
