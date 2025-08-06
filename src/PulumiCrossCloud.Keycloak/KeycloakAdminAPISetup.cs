using FS.Keycloak.RestApiClient.Api;
using FS.Keycloak.RestApiClient.Authentication.ClientFactory;
using FS.Keycloak.RestApiClient.Authentication.Flow;
using FS.Keycloak.RestApiClient.ClientFactory;
using FS.Keycloak.RestApiClient.Model;

// Install NuGet Package: Schick.Keycloak.RestApiClient

public class IdempotentKeycloakSetup
{
    private readonly string _keycloakUrl;
    private readonly string _adminUsername;
    private readonly string _adminPassword;

    public IdempotentKeycloakSetup(string keycloakUrl, string adminUsername, string adminPassword)
    {
        _keycloakUrl = keycloakUrl;
        _adminUsername = adminUsername;
        _adminPassword = adminPassword;
    }

    /// <summary>
    /// Creates or updates a realm (idempotent operation)
    /// </summary>
    public async Task<(bool created, bool updated)> EnsureRealmAsync(string realmName, RealmRepresentation? realmConfig = null)
    {
        var credentials = new PasswordGrantFlow
        {
            KeycloakUrl = _keycloakUrl,
            Realm = "master",
            UserName = _adminUsername,
            Password = _adminPassword,
        };

        using var httpClient = AuthenticationHttpClientFactory.Create(credentials);
        using var realmsApi = ApiClientFactory.Create<RealmsAdminApi>(httpClient);

        try
        {
            // First, check if realm exists by trying to get it
            var existingRealm = await realmsApi.GetAsync(realmName);
            
            if (existingRealm != null)
            {
                // Realm exists - check if update is needed
                if (realmConfig is not null && ShouldUpdateRealm(existingRealm, realmConfig))
                {
                    // Update existing realm
                    realmConfig.Realm = realmName; // Ensure realm name is set
                    await realmsApi.PutAsync(realmName, realmConfig);
                    Console.WriteLine($"✅ Realm '{realmName}' updated successfully");
                    return (false, true);
                }
                else
                {
                    Console.WriteLine($"✅ Realm '{realmName}' already exists and is up to date");
                    return (false, false);
                }
            }
        }
        catch (Exception ex) when (IsNotFoundException(ex))
        {
            // Realm doesn't exist, create it
            var newRealmConfig = realmConfig ?? CreateDefaultRealmConfig(realmName);
            newRealmConfig.Realm = realmName;

            await realmsApi.PostAsync(newRealmConfig);
            Console.WriteLine($"✅ Realm '{realmName}' created successfully");
            return (true, false);
        }

        return (false, false);
    }

    /// <summary>
    /// Creates or updates a client in the specified realm (idempotent operation)
    /// </summary>
    public async Task<(bool created, bool updated)> EnsureClientAsync(
        string realmName, 
        string clientId, 
        string clientSecret, 
        List<string> redirectUris,
        ClientRepresentation clientConfig = null)
    {
        var credentials = new PasswordGrantFlow
        {
            KeycloakUrl = _keycloakUrl,
            Realm = "master",
            UserName = _adminUsername,
            Password = _adminPassword,
        };

        using var httpClient = AuthenticationHttpClientFactory.Create(credentials);
        using var clientsApi = ApiClientFactory.Create<ClientsApi>(httpClient);

        try
        {
            // Get all clients and find by clientId
            var existingClients = await clientsApi.GetClientsAsync(realmName, clientId: clientId);
            var existingClient = existingClients?.FirstOrDefault(c => c.ClientId == clientId);

            if (existingClient != null)
            {
                // Client exists - check if update is needed
                var newClientConfig = clientConfig ?? CreateDefaultClientConfig(clientId, clientSecret, redirectUris);
                
                if (ShouldUpdateClient(existingClient, newClientConfig))
                {
                    // Update existing client
                    newClientConfig.Id = existingClient.Id; // Preserve the ID
                    await clientsApi.PutClientsByClientUuidAsync(realmName, existingClient.Id, newClientConfig);
                    Console.WriteLine($"✅ Client '{clientId}' updated in realm '{realmName}'");
                    return (false, true);
                }
                else
                {
                    Console.WriteLine($"✅ Client '{clientId}' already exists and is up to date in realm '{realmName}'");
                    return (false, false);
                }
            }
            else
            {
                // Client doesn't exist, create it
                var newClientConfig = clientConfig ?? CreateDefaultClientConfig(clientId, clientSecret, redirectUris);
                await clientsApi.PostClientsAsync(realmName, newClientConfig);
                Console.WriteLine($"✅ Client '{clientId}' created in realm '{realmName}'");
                return (true, false);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error managing client '{clientId}': {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Creates or updates Google Identity Provider (idempotent operation)
    /// </summary>
    public async Task<(bool created, bool updated)> EnsureGoogleSSOAsync(
        string realmName, 
        string googleClientId, 
        string googleClientSecret,
        string alias = "google")
    {
        var credentials = new PasswordGrantFlow
        {
            KeycloakUrl = _keycloakUrl,
            Realm = "master",
            UserName = _adminUsername,
            Password = _adminPassword,
        };

        using var httpClient = AuthenticationHttpClientFactory.Create(credentials);
        using var identityProvidersApi = ApiClientFactory.Create<IdentityProvidersApi>(httpClient);

        try
        {
            // Check if identity provider exists
            var existingProvider = await identityProvidersApi.GetIdentityProviderInstancesByAliasAsync(realmName, alias);

            if (existingProvider != null)
            {
                // Provider exists - check if update is needed
                var newProviderConfig = CreateGoogleProviderConfig(alias, googleClientId, googleClientSecret);
                
                if (ShouldUpdateIdentityProvider(existingProvider, newProviderConfig))
                {
                    await identityProvidersApi.PutIdentityProviderInstancesByAliasAsync(realmName, alias, newProviderConfig);
                    Console.WriteLine($"✅ Google SSO updated for realm '{realmName}'");
                    return (false, true);
                }
                else
                {
                    Console.WriteLine($"✅ Google SSO already configured and up to date for realm '{realmName}'");
                    return (false, false);
                }
            }
        }
        catch (Exception ex) when (IsNotFoundException(ex))
        {
            // Provider doesn't exist, create it
            var newProviderConfig = CreateGoogleProviderConfig(alias, googleClientId, googleClientSecret);
            await identityProvidersApi.PostIdentityProviderInstancesAsync(realmName, newProviderConfig);
            Console.WriteLine($"✅ Google SSO configured for realm '{realmName}'");
            return (true, false);
        }
        
        return (false, false);
    }

    /// <summary>
    /// Creates or updates a user (idempotent operation)
    /// </summary>
    public async Task<(bool created, bool updated)> EnsureUserAsync(
        string realmName, 
        string username, 
        string email, 
        string? password = null,
        UserRepresentation userConfig = null)
    {
        var credentials = new PasswordGrantFlow
        {
            KeycloakUrl = _keycloakUrl,
            Realm = "master",
            UserName = _adminUsername,
            Password = _adminPassword,
        };

        using var httpClient = AuthenticationHttpClientFactory.Create(credentials);
        using var usersApi = ApiClientFactory.Create<UsersApi>(httpClient);

        try
        {
            // Search for existing user by username
            var existingUsers = await usersApi.GetUsersAsync(realmName, username: username, exact: true);
            var existingUser = existingUsers?.FirstOrDefault();

            if (existingUser != null)
            {
                // User exists - check if update is needed
                var newUserConfig = userConfig ?? CreateDefaultUserConfig(username, email);
                
                if (ShouldUpdateUser(existingUser, newUserConfig))
                {
                    newUserConfig.Id = existingUser.Id; // Preserve the ID
                    await usersApi.PutUsersByUserIdAsync(realmName, existingUser.Id, newUserConfig);
                    
                    // Update password if provided
                    if (!string.IsNullOrEmpty(password))
                    {
                        await UpdateUserPasswordAsync(usersApi, realmName, existingUser.Id, password);
                    }
                    
                    Console.WriteLine($"✅ User '{username}' updated in realm '{realmName}'");
                    return (false, true);
                }
                else
                {
                    Console.WriteLine($"✅ User '{username}' already exists and is up to date in realm '{realmName}'");
                    return (false, false);
                }
            }
            else
            {
                // User doesn't exist, create it
                var newUserConfig = userConfig ?? CreateDefaultUserConfig(username, email);
                
                // Set password credentials
                if (!string.IsNullOrEmpty(password))
                {
                    newUserConfig.Credentials = new List<CredentialRepresentation>
                    {
                        new ()
                        {
                            Type = "password",
                            Value = password,
                            Temporary = false
                        }
                    };
                }

                await usersApi.PostUsersAsync(realmName, newUserConfig);
                Console.WriteLine($"✅ User '{username}' created in realm '{realmName}'");
                return (true, false);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error managing user '{username}': {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Complete setup with idempotent operations
    /// </summary>
    public async Task<SetupResult> SetupAsync(SetupConfiguration config = null)
    {
        var result = new SetupResult();
        
        try
        {
            Console.WriteLine("🚀 Starting idempotent Keycloak setup...\n");

            config ??= new SetupConfiguration(); // Use default config if none provided

            // 1. Ensure realm exists
            var realmResult = await EnsureRealmAsync(config.RealmName, config.RealmConfig);
            result.RealmCreated = realmResult.created;
            result.RealmUpdated = realmResult.updated;

            // 2. Ensure web application client exists
            var clientResult = await EnsureClientAsync(
                realmName: config.RealmName,
                clientId: config.ClientId,
                clientSecret: config.ClientSecret,
                redirectUris: config.RedirectUris,
                clientConfig: config.ClientConfig
            );
            result.ClientCreated = clientResult.created;
            result.ClientUpdated = clientResult.updated;

            // 3. Ensure Google SSO is configured (if provided)
            if (!string.IsNullOrEmpty(config.GoogleClientId))
            {
                var googleResult = await EnsureGoogleSSOAsync(
                    realmName: config.RealmName,
                    googleClientId: config.GoogleClientId,
                    googleClientSecret: config.GoogleClientSecret
                );
                result.GoogleSSOCreated = googleResult.created;
                result.GoogleSSOUpdated = googleResult.updated;
            }

            // 4. Ensure test user exists (if provided)
            if (!string.IsNullOrEmpty(config.TestUsername))
            {
                var userResult = await EnsureUserAsync(
                    realmName: config.RealmName,
                    username: config.TestUsername,
                    email: config.TestEmail,
                    password: config.TestPassword,
                    userConfig: config.UserConfig
                );
                result.UserCreated = userResult.created;
                result.UserUpdated = userResult.updated;
            }

            Console.WriteLine("\n🎉 Setup completed successfully!");
            Console.WriteLine($"🌐 Admin Console: {_keycloakUrl}/admin/master/console/#/{config.RealmName}");
            Console.WriteLine($"🔐 User Account: {_keycloakUrl}/realms/{config.RealmName}/account");
            
            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Setup failed: {ex.Message}");
            result.Success = false;
            result.Error = ex.Message;
            throw;
        }
    }

    #region Helper Methods

    private bool IsNotFoundException(Exception ex)
    {
        // Check for 404 status code or similar not found indicators
        return ex.Message.Contains("404") || 
               ex.Message.Contains("Not Found") || 
               ex.Message.Contains("not found") ||
               ex.GetType().Name.Contains("NotFound");
    }

    private RealmRepresentation CreateDefaultRealmConfig(string realmName)
    {
        return new RealmRepresentation
        {
            Realm = realmName,
            DisplayName = realmName,
            Enabled = true,
            SslRequired = "external",
            RegistrationAllowed = true,
            LoginWithEmailAllowed = true,
            DuplicateEmailsAllowed = false,
            ResetPasswordAllowed = true,
            EditUsernameAllowed = false,
            BruteForceProtected = true,
            RememberMe = true,
            VerifyEmail = false,
            LoginTheme = "keycloak",
            AccessTokenLifespan = 300,
            SsoSessionIdleTimeout = 1800,
            SsoSessionMaxLifespan = 36000,
            RefreshTokenMaxReuse = 0,
        };
    }

    private ClientRepresentation CreateDefaultClientConfig(string clientId, string clientSecret, List<string> redirectUris)
    {
        return new ClientRepresentation
        {
            ClientId = clientId,
            Name = clientId,
            Enabled = true,
            PublicClient = false,
            BearerOnly = false,
            StandardFlowEnabled = true,
            ImplicitFlowEnabled = false,
            DirectAccessGrantsEnabled = false,
            ServiceAccountsEnabled = false,
            RedirectUris = redirectUris,
            WebOrigins = redirectUris?.Select(GetOriginFromUri).Distinct().ToList(),
            Protocol = "openid-connect",
            FullScopeAllowed = false,
            Secret = clientSecret
        };
    }

    private IdentityProviderRepresentation CreateGoogleProviderConfig(string alias, string clientId, string clientSecret)
    {
        return new IdentityProviderRepresentation
        {
            Alias = alias,
            DisplayName = "Google",
            ProviderId = "google",
            Enabled = true,
            UpdateProfileFirstLoginMode = "on",
            TrustEmail = true,
            StoreToken = true,
            AddReadTokenRoleOnCreate = true,
            AuthenticateByDefault = false,
            LinkOnly = false,
            FirstBrokerLoginFlowAlias = "first broker login",
            Config = new Dictionary<string, string>
            {
                ["clientId"] = clientId,
                ["clientSecret"] = clientSecret,
                ["hostedDomain"] = "",
                ["useJwksUrl"] = "true",
                ["syncMode"] = "IMPORT"
            }
        };
    }

    private UserRepresentation CreateDefaultUserConfig(string username, string email)
    {
        return new UserRepresentation
        {
            Username = username,
            Email = email,
            FirstName = username,
            LastName = "User",
            Enabled = true,
            EmailVerified = true
        };
    }

    private async Task UpdateUserPasswordAsync(UsersApi usersApi, string realmName, string userId, string password)
    {
        var credential = new CredentialRepresentation
        {
            Type = "password",
            Value = password,
            Temporary = false
        };
        
        await usersApi.PutUsersResetPasswordByUserIdAsync(realmName, userId, credential);
    }

    private bool ShouldUpdateRealm(RealmRepresentation existing, RealmRepresentation desired)
    {
        // Compare key properties to determine if update is needed
        return existing.DisplayName != desired.DisplayName ||
               existing.Enabled != desired.Enabled ||
               existing.SslRequired != desired.SslRequired ||
               existing.RegistrationAllowed != desired.RegistrationAllowed ||
               existing.LoginWithEmailAllowed != desired.LoginWithEmailAllowed ||
               existing.AccessTokenLifespan != desired.AccessTokenLifespan ||
               existing.SsoSessionIdleTimeout != desired.SsoSessionIdleTimeout;
    }

    private bool ShouldUpdateClient(ClientRepresentation existing, ClientRepresentation desired)
    {
        return existing.Name != desired.Name ||
               existing.Enabled != desired.Enabled ||
               existing.PublicClient != desired.PublicClient ||
               existing.StandardFlowEnabled != desired.StandardFlowEnabled ||
               existing.DirectAccessGrantsEnabled != desired.DirectAccessGrantsEnabled ||
               !ListsEqual(existing.RedirectUris, desired.RedirectUris) ||
               !ListsEqual(existing.WebOrigins, desired.WebOrigins);
    }

    private bool ShouldUpdateIdentityProvider(IdentityProviderRepresentation existing, IdentityProviderRepresentation desired)
    {
        return existing.DisplayName != desired.DisplayName ||
               existing.Enabled != desired.Enabled ||
               existing.TrustEmail != desired.TrustEmail ||
               existing.StoreToken != desired.StoreToken ||
               !DictionariesEqual(existing.Config, desired.Config);
    }

    private bool ShouldUpdateUser(UserRepresentation existing, UserRepresentation desired)
    {
        return existing.FirstName != desired.FirstName ||
               existing.LastName != desired.LastName ||
               existing.Email != desired.Email ||
               existing.Enabled != desired.Enabled ||
               existing.EmailVerified != desired.EmailVerified;
    }

    private bool ListsEqual<T>(List<T> list1, List<T> list2)
    {
        if (list1 == null && list2 == null) return true;
        if (list1 == null || list2 == null) return false;
        if (list1.Count != list2.Count) return false;
        
        return list1.OrderBy(x => x).SequenceEqual(list2.OrderBy(x => x));
    }

    private bool DictionariesEqual(Dictionary<string, string> dict1, Dictionary<string, string> dict2)
    {
        if (dict1 == null && dict2 == null) return true;
        if (dict1 == null || dict2 == null) return false;
        if (dict1.Count != dict2.Count) return false;
        
        return dict1.OrderBy(kvp => kvp.Key).SequenceEqual(dict2.OrderBy(kvp => kvp.Key));
    }

    private string GetOriginFromUri(string uri)
    {
        try
        {
            var parsedUri = new Uri(uri);
            return $"{parsedUri.Scheme}://{parsedUri.Authority}";
        }
        catch
        {
            return uri;
        }
    }

    #endregion
}

#region Configuration Classes

public class SetupConfiguration
{
    public string RealmName { get; set; } = "my-company";
    public string ClientId { get; set; } = "my-web-app";
    public string ClientSecret { get; set; } = "web-app-secret-123";
    public List<string> RedirectUris { get; set; } = new List<string>
    {
        "https://myapp.example.com/auth/callback",
        "http://localhost:3000/auth/callback"
    };
    
    // Optional Google SSO configuration
    public string GoogleClientId { get; set; }
    public string GoogleClientSecret { get; set; }
    
    // Optional test user configuration
    public string TestUsername { get; set; }
    public string TestEmail { get; set; }
    public string TestPassword { get; set; }
    
    // Optional custom configurations
    public RealmRepresentation RealmConfig { get; set; }
    public ClientRepresentation ClientConfig { get; set; }
    public UserRepresentation UserConfig { get; set; }
}

public class SetupResult
{
    public bool Success { get; set; }
    public string Error { get; set; }
    
    public bool RealmCreated { get; set; }
    public bool RealmUpdated { get; set; }
    
    public bool ClientCreated { get; set; }
    public bool ClientUpdated { get; set; }
    
    public bool GoogleSSOCreated { get; set; }
    public bool GoogleSSOUpdated { get; set; }
    
    public bool UserCreated { get; set; }
    public bool UserUpdated { get; set; }

    public override string ToString()
    {
        var results = new List<string>();
        
        if (RealmCreated) results.Add("Realm created");
        else if (RealmUpdated) results.Add("Realm updated");
        else results.Add("Realm unchanged");
        
        if (ClientCreated) results.Add("Client created");
        else if (ClientUpdated) results.Add("Client updated");
        else results.Add("Client unchanged");
        
        if (GoogleSSOCreated) results.Add("Google SSO created");
        else if (GoogleSSOUpdated) results.Add("Google SSO updated");
        
        if (UserCreated) results.Add("User created");
        else if (UserUpdated) results.Add("User updated");
        
        return string.Join(", ", results);
    }
}

#endregion

#region Usage Examples

public class Program
{
    public static async Task Main(string[] args)
    {
        var setup = new IdempotentKeycloakSetup(
            keycloakUrl: "http://localhost:8080",
            adminUsername: "admin",
            adminPassword: "admin"
        );

        // Example 1: Basic setup with defaults
        var basicConfig = new SetupConfiguration
        {
            RealmName = "my-company",
            ClientId = "my-web-app",
            ClientSecret = "web-app-secret-123",
            TestUsername = "testuser",
            TestEmail = "test@example.com",
            TestPassword = "Test123!"
        };

        var result = await setup.SetupAsync(basicConfig);
        Console.WriteLine($"Setup result: {result}");

        // Example 2: Setup with Google SSO
        var configWithGoogle = new SetupConfiguration
        {
            RealmName = "my-company-google",
            ClientId = "my-web-app-google",
            ClientSecret = "web-app-secret-456",
            GoogleClientId = "your-google-client-id.apps.googleusercontent.com",
            GoogleClientSecret = "your-google-client-secret",
            TestUsername = "testuser",
            TestEmail = "test@example.com",
            TestPassword = "Test123!"
        };

        var googleResult = await setup.SetupAsync(configWithGoogle);
        Console.WriteLine($"Google SSO setup result: {googleResult}");

        // Example 3: Individual resource management
        var (realmCreated, realmUpdated) = await setup.EnsureRealmAsync("test-realm");
        Console.WriteLine($"Realm: Created={realmCreated}, Updated={realmUpdated}");

        var (clientCreated, clientUpdated) = await setup.EnsureClientAsync(
            "test-realm", 
            "test-client", 
            "secret", 
            new List<string> { "http://localhost:3000/callback" }
        );
        Console.WriteLine($"Client: Created={clientCreated}, Updated={clientUpdated}");
    }
}

#endregion