import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { keycloakify } from "keycloakify/vite-plugin";

export default defineConfig({
    plugins: [
        react(),
        keycloakify({
            themeName: "camguard",
            accountThemeImplementation: "none",
            // Solo Keycloak 26+ (la imagen de keycloak/Dockerfile).
            keycloakVersionTargets: {
                "22-to-25": false,
                "all-other-versions": "camguard-theme.jar"
            }
        })
    ]
});
