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

/// <summary>
/// The OGS servers Hoshi can use, selectable at runtime in the lobby (independent of the .NET environment):
/// online-go.com always signs in with OAuth (browser, Google…); beta always uses password login because it
/// cannot register OAuth applications. Password login is therefore impossible against production.
/// </summary>
public sealed class OgsServerCatalog
{
    public OgsServerCatalog(OgsOptions production, OgsOptions beta, OgsOptions initial)
    {
        Production = production;
        Beta = beta;
        Initial = initial;
    }

    public OgsOptions Production { get; }

    public OgsOptions Beta { get; }

    /// <summary>Server selected when Hoshi starts (<c>Ogs:DefaultServer</c>).</summary>
    public OgsOptions Initial { get; }

    public IReadOnlyList<OgsOptions> All => [Production, Beta];

    /// <summary>
    /// <c>Ogs:ClientId</c> (public OAuth client of online-go.com), <c>Ogs:RedirectPort</c> and
    /// <c>Ogs:DefaultServer</c> = <c>online-go</c> (default) | <c>beta</c>.
    /// </summary>
    public static OgsServerCatalog FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        IConfigurationSection s = configuration.GetSection("Ogs");
        var defaults = new OgsOptions();
        string version = typeof(OgsServerCatalog).Assembly.GetName().Version?.ToString(3) ?? defaults.ClientVersion;

        OgsOptions production = defaults with
        {
            BaseUrl = new Uri("https://online-go.com"),
            AuthMode = OgsAuthMode.OAuth,
            ClientId = s["ClientId"] is { Length: > 0 } c ? c : null,
            RedirectPort = int.TryParse(s["RedirectPort"], out int p) ? p : defaults.RedirectPort,
            ClientVersion = version,
        };
        OgsOptions beta = defaults with
        {
            BaseUrl = new Uri("https://beta.online-go.com"),
            AuthMode = OgsAuthMode.Password,
            ClientVersion = version,
        };

        OgsOptions initial = s["DefaultServer"]?.Trim().ToLowerInvariant() switch
        {
            "beta" or "beta.online-go.com" => beta,
            _ => production,
        };
        return new OgsServerCatalog(production, beta, initial);
    }
}

public static class OgsServiceRegistration
{
    public const string HttpClientName = "ogs";

    public static IServiceCollection AddOgs(this IServiceCollection services, IConfiguration configuration)
    {
        OgsServerCatalog catalog = OgsServerCatalog.FromConfiguration(configuration);
        services.AddSingleton(catalog);

        services.AddHttpClient(HttpClientName, http =>
            {
                http.Timeout = TimeSpan.FromSeconds(30);
                http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(catalog.Production.ClientName, catalog.Production.ClientVersion));
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
        services.AddSingleton<IOgsConnectionFactory, OgsConnectionFactory>();
        services.AddSingleton<IBrowserLauncher, AvaloniaBrowserLauncher>();
        services.AddSingleton<OgsClient>();
        services.AddSingleton<IOgsClient>(sp => sp.GetRequiredService<OgsClient>());
        return services;
    }
}

/// <summary>Auth + REST + realtime for one server.</summary>
public sealed record OgsConnection(OgsAuthService Auth, OgsRestClient Rest, OgsRealtimeClient Realtime);

public interface IOgsConnectionFactory
{
    OgsConnection Create(OgsOptions options);
}

public sealed class OgsConnectionFactory(IHttpClientFactory http, ITokenStore store, ILoggerFactory loggers) : IOgsConnectionFactory
{
    public OgsConnection Create(OgsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        HttpClient Client()
        {
            HttpClient c = http.CreateClient(OgsServiceRegistration.HttpClientName);
            c.BaseAddress = options.BaseUrl;
            return c;
        }

        var auth = new OgsAuthService(options, Client(), store, TimeProvider.System, loggers.CreateLogger<OgsAuthService>());
        var rest = new OgsRestClient(Client(), auth);
        string userAgent = $"{options.ClientName}/{options.ClientVersion}";
        var realtime = new OgsRealtimeClient(
            options.EffectiveWebSocketUrl,
            () => new ClientWebSocketConnection(userAgent),
            () => auth.Session?.UserJwt,
            new OgsRealtimeOptions { ClientName = options.ClientName, ClientVersion = options.ClientVersion },
            TimeProvider.System,
            loggers.CreateLogger<OgsRealtimeClient>());
        return new OgsConnection(auth, rest, realtime);
    }
}
