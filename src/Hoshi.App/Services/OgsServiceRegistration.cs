using System.Net;
using System.Net.Http.Headers;
using Hoshi.Ogs;
using Hoshi.Ogs.Auth;
using Hoshi.Ogs.Realtime;
using Hoshi.Ogs.Rest;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hoshi.App.Services;

public static class OgsServiceRegistration
{
    public const string HttpClientName = "ogs";

    /// <summary>
    /// Reads the <c>Ogs</c> section. Production (online-go.com) uses OAuth; the Development environment
    /// (<c>appsettings.Development.json</c>) points to beta.online-go.com with password login.
    /// </summary>
    public static OgsOptions ReadOptions(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        IConfigurationSection s = configuration.GetSection("Ogs");
        var defaults = new OgsOptions();
        OgsOptions options = defaults with
        {
            BaseUrl = s["BaseUrl"] is { Length: > 0 } b ? new Uri(b) : defaults.BaseUrl,
            WebSocketUrl = s["WebSocketUrl"] is { Length: > 0 } w ? new Uri(w) : null,
            ClientId = s["ClientId"] is { Length: > 0 } c ? c : null,
            AuthMode = Enum.TryParse(s["AuthMode"], ignoreCase: true, out OgsAuthMode m) ? m : defaults.AuthMode,
            RedirectPort = int.TryParse(s["RedirectPort"], out int p) ? p : defaults.RedirectPort,
            ClientVersion = typeof(OgsServiceRegistration).Assembly.GetName().Version?.ToString(3) ?? defaults.ClientVersion,
        };

        if (options.IsProduction && options.AuthMode == OgsAuthMode.Password)
        {
            // CLAUDE.md: the password flow is a development aid for beta only.
            throw new InvalidOperationException("El inicio de sesión con contraseña solo se permite contra beta.online-go.com.");
        }

        return options;
    }

    public static IServiceCollection AddOgs(this IServiceCollection services, IConfiguration configuration)
    {
        OgsOptions options = ReadOptions(configuration);
        services.AddSingleton(options);

        services.AddHttpClient(HttpClientName, http =>
            {
                http.BaseAddress = options.BaseUrl;
                http.Timeout = TimeSpan.FromSeconds(30);
                http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(options.ClientName, options.ClientVersion));
                http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            // Cookies are handled by OgsAuthService (password mode), never by a shared handler.
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        services.AddSingleton<ITokenStore>(sp => SecureTokenStore.Create(sp.GetRequiredService<ILoggerFactory>()));
        services.AddSingleton(sp => new OgsAuthService(
            options,
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
            sp.GetRequiredService<ITokenStore>(),
            TimeProvider.System,
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<OgsAuthService>()));
        services.AddSingleton(sp => new OgsRestClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
            sp.GetRequiredService<OgsAuthService>()));
        services.AddSingleton(sp =>
        {
            OgsAuthService auth = sp.GetRequiredService<OgsAuthService>();
            string userAgent = $"{options.ClientName}/{options.ClientVersion}";
            return new OgsRealtimeClient(
                options.EffectiveWebSocketUrl,
                () => new ClientWebSocketConnection(userAgent),
                () => auth.Session?.UserJwt,
                new OgsRealtimeOptions { ClientName = options.ClientName, ClientVersion = options.ClientVersion },
                TimeProvider.System,
                sp.GetRequiredService<ILoggerFactory>().CreateLogger<OgsRealtimeClient>());
        });
        services.AddSingleton<IBrowserLauncher, AvaloniaBrowserLauncher>();
        services.AddSingleton<OgsClient>();
        services.AddSingleton<IOgsClient>(sp => sp.GetRequiredService<OgsClient>());
        return services;
    }
}
