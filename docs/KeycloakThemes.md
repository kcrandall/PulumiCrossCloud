# Keycloak Theme Integration Guide

This document explains how to integrate custom Keycloak themes (such as Keycloakify-generated themes) with the PulumiCrossCloud Keycloak stack.

## Overview

The PulumiCrossCloud Keycloak implementation now supports:
- **Theme Provider JAR Files**: Keycloakify-generated JAR files or other Keycloak theme providers
- **Custom Theme Directories**: Direct theme file copying to `/opt/keycloak/themes`
- **Realm Theme Configuration**: Automatic theme assignment to realms

## Quick Start

### 1. Build Your Keycloakify Theme

First, build your Keycloakify theme to generate JAR files:

```bash
cd frontend/keycloaktheme
bash scripts/build.sh
```

This creates JAR files in `dist_keycloak/`:
- `keycloak-theme-for-kc-22-to-25.jar` (for modern Keycloak versions)
- `keycloak-theme-for-kc-all-other-versions.jar` (for older versions)

### 2. Configure Theme Providers

```csharp
var themeProviders = new KeycloakThemeProviders
{
    ThemeJarFiles = 
    [
        "../../frontend/keycloaktheme/dist_keycloak/keycloak-theme-for-kc-22-to-25.jar"
    ]
};
```

### 3. Configure Realm Theme

```csharp
var realmConfig = new PulumiRealmConfig
{
    RealmName = "my-realm",
    RealmArgs = new RealmArgs
    {
        Enabled = true,
        DisplayName = "My Application",
        // ... other realm settings
    },
    ThemeConfig = new RealmThemeConfig
    {
        LoginTheme = "tcg-app-theme",      // Your custom theme name
        AccountTheme = "tcg-app-theme",    // Optional: use same theme for account pages
        // AdminTheme = "keycloak",        // Keep default admin theme
        // EmailTheme = "keycloak"         // Keep default email theme
    }
};
```

### 4. Deploy Keycloak with Themes

```csharp
var keycloakStack = new KeycloakStack(
    factory,
    "my-keycloak",
    postgresUrlSecret,
    postgresUserSecret,
    postgresPasswordSecret,
    realmConfig,
    themeProviders  // 👈 Pass theme providers here
);
```

## Configuration Options

### KeycloakThemeProviders

```csharp
public sealed class KeycloakThemeProviders
{
    /// <summary>
    /// List of theme JAR file paths to copy to the Keycloak providers directory.
    /// These files should be Keycloakify-generated JAR files or other Keycloak theme providers.
    /// </summary>
    public List<string> ThemeJarFiles { get; init; } = [];

    /// <summary>
    /// Optional custom themes directory to copy to /opt/keycloak/themes.
    /// This is an alternative to JAR-based themes.
    /// </summary>
    public string? CustomThemesDirectory { get; init; }
}
```

### RealmThemeConfig

```csharp
public sealed class RealmThemeConfig
{
    /// <summary>
    /// The name of the login theme to use for this realm.
    /// </summary>
    public string? LoginTheme { get; init; }

    /// <summary>
    /// The name of the account theme to use for this realm.
    /// </summary>
    public string? AccountTheme { get; init; }

    /// <summary>
    /// The name of the admin theme to use for this realm.
    /// </summary>
    public string? AdminTheme { get; init; }

    /// <summary>
    /// The name of the email theme to use for this realm.
    /// </summary>
    public string? EmailTheme { get; init; }
}
```

## Usage Examples

### Example 1: JAR-based Themes (Keycloakify)

```csharp
var themeProviders = new KeycloakThemeProviders
{
    ThemeJarFiles = 
    [
        "path/to/my-theme.jar",
        "path/to/another-theme.jar"
    ]
};

var realmConfig = new PulumiRealmConfig
{
    RealmName = "my-app",
    RealmArgs = new RealmArgs { Enabled = true },
    ThemeConfig = new RealmThemeConfig
    {
        LoginTheme = "my-custom-theme"
    }
};
```

### Example 2: Directory-based Themes

```csharp
var themeProviders = new KeycloakThemeProviders
{
    CustomThemesDirectory = "path/to/themes/directory"
};
```

### Example 3: Multiple Themes for Different Purposes

```csharp
var realmConfig = new PulumiRealmConfig
{
    // ... realm settings
    ThemeConfig = new RealmThemeConfig
    {
        LoginTheme = "custom-login-theme",     // Custom branded login
        AccountTheme = "custom-account-theme", // Custom account management
        AdminTheme = "keycloak",               // Keep default admin theme
        EmailTheme = "custom-email-theme"      // Custom email templates
    }
};
```

## How It Works

### Build Process

1. **Theme JAR Detection**: The system checks if specified JAR files exist
2. **Docker Context Preparation**: Creates a temporary build directory
3. **File Copying**: Copies JAR files to `providers/` subdirectory in build context
4. **Dockerfile Generation**: Dynamically adds `COPY` commands to the Dockerfile
5. **Container Build**: Builds Keycloak container with themes included

### Generated Dockerfile Example

```dockerfile
FROM quay.io/keycloak/keycloak:24.0.1

# ... existing Keycloak configuration ...

# Copy theme provider JAR files
COPY providers/my-theme.jar /opt/keycloak/providers/

# Copy custom themes (if using directory-based themes)
COPY themes/ /opt/keycloak/themes/

ENTRYPOINT ["/opt/keycloak/bin/kc.sh", "start-dev"]
```

### Realm Configuration

When the realm is created, theme settings are automatically applied:

```csharp
// This happens automatically in PulumiRealmDeployer
if (config.ThemeConfig is not null)
{
    if (!string.IsNullOrWhiteSpace(config.ThemeConfig.LoginTheme))
        config.RealmArgs.LoginTheme = config.ThemeConfig.LoginTheme;
    // ... other theme assignments
}
```

## Troubleshooting

### Common Issues

1. **JAR File Not Found**
   ```
   [KeycloakStack] Warning: Theme JAR file not found: path/to/theme.jar
   ```
   - Ensure the JAR file path is correct and the file exists
   - Build your Keycloakify theme first: `bash scripts/build.sh`

2. **Theme Not Applied**
   - Check that the theme name in `LoginTheme` matches the theme name in your JAR
   - Verify the JAR was copied to `/opt/keycloak/providers/` in the container
   - Check Keycloak logs for theme loading errors

3. **Build Failures**
   - Ensure Docker has access to the theme files
   - Check file permissions on theme JAR files
   - Verify the build context doesn't exceed Docker's size limits

### Debugging

Enable verbose logging to see theme processing:

```csharp
// The system automatically logs theme operations:
// [KeycloakStack] Copied theme JAR: path/to/theme.jar -> providers/theme.jar
// [KeycloakStack] Dockerfile written to temp path: /tmp/.pulumi-docker/...
```

## Integration with Keycloakify

### Typical Workflow

1. **Develop Theme**: Work on your Keycloakify theme in `frontend/keycloaktheme/`
2. **Build Theme**: Run `bash scripts/build.sh` to generate JAR files
3. **Update Infrastructure**: Reference the JAR in your Pulumi configuration
4. **Deploy**: Run `pulumi up` to deploy Keycloak with the new theme

### Continuous Integration

```yaml
# Example CI/CD pipeline step
- name: Build Keycloak Theme
  run: |
    cd frontend/keycloaktheme
    pnpm install
    bash scripts/build.sh

- name: Deploy Infrastructure
  run: |
    cd infrastructure
    pulumi up --yes
```

## Best Practices

1. **Version Control**: Don't commit JAR files to git; build them in CI/CD
2. **Path Management**: Use relative paths from your Pulumi project root
3. **Theme Naming**: Use consistent theme names across environments
4. **Testing**: Test themes in development before production deployment
5. **Backup**: Keep backups of working theme JAR files for rollback scenarios

## Advanced Configuration

### Multiple Environments

```csharp
var themeJarPath = Environment.GetEnvironmentVariable("ENVIRONMENT") switch
{
    "production" => "themes/prod/my-theme.jar",
    "staging" => "themes/staging/my-theme.jar", 
    _ => "themes/dev/my-theme.jar"
};

var themeProviders = new KeycloakThemeProviders
{
    ThemeJarFiles = [themeJarPath]
};
```

### Dynamic Theme Selection

```csharp
var themeName = Environment.GetEnvironmentVariable("KEYCLOAK_THEME") ?? "default-theme";

var realmConfig = new PulumiRealmConfig
{
    // ... other settings
    ThemeConfig = new RealmThemeConfig
    {
        LoginTheme = themeName
    }
};
```

This integration provides a seamless way to deploy custom Keycloak themes alongside your infrastructure, ensuring consistent branding across your authentication flows.
