# Keycloak Dockerfile for PulumiCrossCloud
FROM quay.io/keycloak/keycloak:24.0.1

# Example: Add custom configuration or themes here
# COPY ./themes /opt/keycloak/themes

# Set environment variables for Keycloak
ENV KC_HEALTH_ENABLED=true
ENV KC_METRICS_ENABLED=true
ENV KC_FEATURES=token-exchange
ENV KC_DB=postgres
ENV KC_HTTP_ENABLED=true
ENV KC_HOSTNAME_STRICT=false
ENV KC_PROXY=edge
ENV KC_CACHE=local
ENV KC_LOG_LEVEL=INFO

# Expose default Keycloak port as 
EXPOSE 80
EXPOSE 8080
ENV KC_HTTP_PORT=8080

# Start Keycloak in development mode (customize as needed)
ENTRYPOINT ["/opt/keycloak/bin/kc.sh", "start-dev"]

