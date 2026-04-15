using Microsoft.Extensions.Logging;
using ChatAyi.Services;
using ChatAyi.Services.Search;
using System.Net;
using System.Net.Http;

namespace ChatAyi;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        builder.Services.AddSingleton(sp =>
        {
            var cerebras = new HttpClient(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(8) })
            {
                BaseAddress = new Uri("https://api.cerebras.ai"),
                Timeout = Timeout.InfiniteTimeSpan
            };

            var nvidia = new HttpClient(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(8) })
            {
                BaseAddress = new Uri("https://integrate.api.nvidia.com/v1/"),
                Timeout = Timeout.InfiniteTimeSpan
            };

            var inception = new HttpClient(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(8) })
            {
                BaseAddress = new Uri("https://api.inceptionlabs.ai"),
                Timeout = Timeout.InfiniteTimeSpan
            };

            return new ChatApiClient(cerebras, nvidia, inception);
        });

        builder.Services.AddSingleton(sp =>
        {
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                ConnectTimeout = TimeSpan.FromSeconds(8)
            };
            var http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(20)
            };

            return new DdgSearchClient(http);
        });

        builder.Services.AddSingleton(sp =>
        {
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                ConnectTimeout = TimeSpan.FromSeconds(8)
            };
            var http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(20)
            };

            var baseUrl = Environment.GetEnvironmentVariable("CHATAYI_SEARXNG_BASE_URL");
            var fallbackRaw = Environment.GetEnvironmentVariable("CHATAYI_SEARXNG_FALLBACK_INSTANCES");
            var fallbackInstances = SearxngSearchClient.ParseFallbackInstancesEnvVar(fallbackRaw);
            return new SearxngSearchClient(http, baseUrl, fallbackInstances, TimeSpan.FromSeconds(8));
        });

        builder.Services.AddSingleton<SearchIntentClassifier>();

        builder.Services.AddSingleton(sp =>
        {
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                ConnectTimeout = TimeSpan.FromSeconds(8)
            };
            var http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(20)
            };

            var searxng = sp.GetRequiredService<SearxngSearchClient>();
            var ddg = sp.GetRequiredService<DdgSearchClient>();
            return new SearchProviderMux(searxng, http, ddg);
        });

        builder.Services.AddSingleton(sp =>
            new FreeSearchClient(
                sp.GetRequiredService<SearchIntentClassifier>(),
                sp.GetRequiredService<SearchProviderMux>()));

        builder.Services.AddSingleton(sp =>
        {
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                ConnectTimeout = TimeSpan.FromSeconds(8)
            };
            var http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(25)
            };
            return new BrowseClient(http);
        });

        builder.Services.AddSingleton<EvidenceFetcher>();
        builder.Services.AddSingleton<PassageExtractor>();
        builder.Services.AddSingleton<SearchHealthEvaluator>();
        builder.Services.AddSingleton<SearchGroundingComposer>();
        builder.Services.AddSingleton<SearchOrchestrator>();
        builder.Services.AddSingleton<LocalMemoryStore>();
        builder.Services.AddSingleton<PersonalMemoryStore>();
        builder.Services.AddSingleton<LocalSessionStore>();
        builder.Services.AddSingleton<SessionCatalogStore>();
        builder.Services.AddSingleton<PersonaProfileStore>();
        builder.Services.AddSingleton<PromptContextAssembler>();

        return builder.Build();
    }
}
