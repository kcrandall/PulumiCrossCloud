# Keycloak Dockerfile for PulumiCrossCloud
FROM quay.io/keycloak/keycloak:24.0.1

# Example: Add custom configuration or themes here
# COPY ./themes /opt/keycloak/themes

# Set environment variables for Keycloak
ENV KEYCLOAK_ADMIN=admin
ENV KEYCLOAK_ADMIN_PASSWORD=admin

# Expose default Keycloak port
EXPOSE 8080

# Start Keycloak in development mode (customize as needed)
ENTRYPOINT ["/opt/keycloak/bin/kc.sh", "start-dev"]

