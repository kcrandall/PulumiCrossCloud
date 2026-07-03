# Keycloak Dockerfile for PulumiCrossCloud - Optimized Mode
# Keycloak version - can be overridden at build time with --build-arg KEYCLOAK_VERSION=X.Y.Z
ARG KEYCLOAK_VERSION=26.3.3
FROM quay.io/keycloak/keycloak:${KEYCLOAK_VERSION} as builder

# Build-time environment variables (will be hardcoded by KeycloakStack)
ENV KC_DB=${KC_DB}
ENV KC_HEALTH_ENABLED=${KC_HEALTH_ENABLED}
ENV KC_METRICS_ENABLED=${KC_METRICS_ENABLED}
ENV KC_FEATURES=${KC_FEATURES}
ENV KC_HTTP_ENABLED=${KC_HTTP_ENABLED}
ENV KC_HOSTNAME_STRICT=${KC_HOSTNAME_STRICT}
ENV KC_PROXY=${KC_PROXY}
ENV KC_CACHE=${KC_CACHE}
ENV KC_PROXY_HEADERS=${KC_PROXY_HEADERS}
ENV KC_HTTP_PORT=${KC_HTTP_PORT}
ENV KC_LEGACY_OBSERVABILITY_INTERFACE=${KC_LEGACY_OBSERVABILITY_INTERFACE}

# Copy custom themes and providers if they exist
# These will be added by the build process if themeProviders are configured
# {THEMES}

# Build Keycloak with the specified configuration
# This pre-compiles and optimizes Keycloak for faster startup
RUN /opt/keycloak/bin/kc.sh build

# Production stage
FROM quay.io/keycloak/keycloak:${KEYCLOAK_VERSION}

# Note: Using Keycloak's built-in health endpoints

# Copy the optimized Keycloak build from the builder stage
COPY --from=builder /opt/keycloak/ /opt/keycloak/

# Runtime environment variables that don't affect the build
# Database connection details will be injected at runtime via secrets
ENV KC_HTTP_PORT=8080

# Expose ports
EXPOSE 8080

# Use optimized start command with --optimized flag to avoid runtime rebuilds
ENTRYPOINT ["/opt/keycloak/bin/kc.sh", "start", "--optimized"]

