using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ChatAyi.Models;
using ChatAyi.Services;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;

namespace ChatAyi.Pages;

public partial class ChatPage : ContentPage, IQueryAttributable
{
    private const string ThinkingModeKey = "ChatAyi.ThinkingMode";
    private const string EnableWebToolsKey = "ChatAyi.EnableWebTools";
    private const string EnableRememberKey = "ChatAyi.EnableRemember";
    private const string ShowCommandTipsKey = "ChatAyi.ShowCommandTips";
    private const string StructuredAnswersKey = "ChatAyi.StructuredAnswers";
    private const string ProviderKey = "ChatAyi.Provider";
    private const string CerebrasModelKey = "ChatAyi.Model.Cerebras";
    private const string NvidiaModelKey = "ChatAyi.Model.Nvidia";
    private const string InceptionModelKey = "ChatAyi.Model.Inception";
    private const string OfflineFallbackKey = "ChatAyi.OfflineFallback";
    private const string OfflineQueueKey = "ChatAyi.OfflineQueue";
    private const string EnableDcpKey = "ChatAyi.EnableDcp";

    private readonly ChatApiClient _api;
    private readonly FreeSearchClient _search;
    private readonly BrowseClient _browse;
    private readonly LocalMemoryStore _memory;
    private readonly PersonalMemoryStore _personalMemoryStore;
    private readonly LocalSessionStore _sessions;
    private readonly SessionCatalogStore _sessionCatalog;
    private readonly PersonaProfileStore _personaProfileStore;
    private readonly PromptContextAssembler _promptContextAssembler;
    private CancellationTokenSource _cts;
    private bool _isSending;
    private string _inputText = string.Empty;
    private bool _checkedKey;
    private string _thinkingMode = "off"; // off | on | verbose
    private bool _enableWebTools = true;
    private bool _enableRemember = true;
    private bool _isMemoryTemporarilyOff;
    private bool _showCommandTips = true;
    private bool _structuredAnswers = true;
    private bool _isSettingsOpen;

    private bool _enableOfflineFallback = true;
    private bool _queueWhileOffline = true;
    private bool _enableDcp = true;
    private bool _connectivitySubscribed;
    private string _dcpSummaryText = "-";

    private sealed record PendingSend(
        string SessionId,
        string Prompt,
        ChatApiClient.Provider Provider,
        string Model,
        bool EnableThinking,
        IEnumerable<object> RequestMessages,
        ChatMessage Assistant);

    private readonly Queue<PendingSend> _offlineQueue = new();
    private readonly SemaphoreSlim _offlineQueueGate = new(1, 1);
    private readonly DynamicContextPruner _dcp = new();
    private IReadOnlyList<string> _nvidiaRuntimeModels = Array.Empty<string>();
    private readonly HashSet<string> _nvidiaVerifiedModels = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _nvidiaRejectedModels = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _nvidiaCheckingModels = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _modelProbeGate = new(1, 1);

    private int _providerIndex; // 0 = Cerebras, 1 = NVIDIA Integrate, 2 = Inception
    private string _currentModel = "";
    private SessionMeta _selectedSession;
    private bool _syncingSessionSelection;
    private bool _sessionInitialized;
    private bool _forceFreshSessionOnAppear;

    private static readonly string[] CerebrasModels =
    {
        "gpt-oss-120b"
    };

    private static readonly string[] NvidiaModels =
    {
        "z-ai/glm5",
        "01-ai/yi-large",
        "abacusai/dracarys-llama-3.1-70b-instruct",
        "aisingapore/sea-lion-7b-instruct",
        "baichuan-inc/baichuan2-13b-chat",
        "bytedance/seed-oss-36b-instruct",
        "deepseek-ai/deepseek-r1",
        "deepseek-ai/deepseek-r1-distill-llama-8b",
        "deepseek-ai/deepseek-r1-distill-qwen-7b",
        "deepseek-ai/deepseek-r1-distill-qwen-14b",
        "deepseek-ai/deepseek-r1-distill-qwen-32b",
        "deepseek-ai/deepseek-v3.1",
        "deepseek-ai/deepseek-v3.1-terminus",
        "deepseek-ai/deepseek-v3.2",
        "google/codegemma-1.1-7b",
        "google/codegemma-7b",
        "google/gemma-2-2b-it",
        "google/gemma-2-27b-it",
        "google/gemma-2-9b-it",
        "google/gemma-3-1b-it",
        "google/gemma-7b",
        "google/recurrentgemma-2b",
        "google/shieldgemma-9b",
        "gotocompany/gemma-2-9b-cpt-sahabatai-instruct",
        "ibm/granite-3_3-8b-instruct",
        "ibm/granite-34b-code-instruct",
        "ibm/granite-8b-code-instruct",
        "ibm/granite-guardian-3.0-8b",
        "igenius/colosseum_355b_instruct_16k",
        "igenius/italia_10b_instruct_16k",
        "institute-of-science-tokyo/llama-3.1-swallow-70b-instruct-v01",
        "institute-of-science-tokyo/llama-3.1-swallow-8b-instruct-v0.1",
        "marin/marin-8b-instruct",
        "mediatek/breeze-7b-instruct",
        "meta/codellama-70b",
        "meta/llama2-70b",
        "meta/llama3-70b",
        "meta/llama3-8b",
        "meta/llama-3.1-405b-instruct",
        "meta/llama-3.1-70b-instruct",
        "meta/llama-3.1-8b-instruct",
        "meta/llama-3.2-1b-instruct",
        "meta/llama-3.2-3b-instruct",
        "meta/llama-3.3-70b-instruct",
        "microsoft/phi-3-medium-128k-instruct",
        "microsoft/phi-3-medium-4k-instruct",
        "microsoft/phi-3-mini-128k-instruct",
        "microsoft/phi-3-mini-4k-instruct",
        "microsoft/phi-3-small-128k-instruct",
        "microsoft/phi-3-small-8k-instruct",
        "microsoft/phi-3.5-mini",
        "microsoft/phi-4-mini-instruct",
        "microsoft/phi-4-mini-flash-reasoning",
        "minimaxai/minimax-m2",
        "minimaxai/minimax-m2.1",
        "mistralai/codestral-22b-instruct-v0.1",
        "mistralai/devstral-2-123b-instruct-2512",
        "mistralai/magistral-small-2506",
        "mistralai/mamba-codestral-7b-v0.1",
        "mistralai/mathstral-7b-v01",
        "mistralai/mistral-2-large-instruct",
        "mistralai/mistral-7b-instruct",
        "mistralai/mistral-7b-instruct-v0.3",
        "mistralai/mistral-large",
        "mistralai/mistral-nemotron",
        "mistralai/mistral-small-24b-instruct",
        "mistralai/mixtral-8x22b-instruct",
        "mistralai/mixtral-8x7b-instruct",
        "moonshotai/kimi-k2-instruct",
        "moonshotai/kimi-k2-instruct-0905",
        "moonshotai/kimi-k2-thinking",
        "nvidia/llama-3.1-nemoguard-8b-content-safety",
        "nvidia/llama-3.1-nemoguard-8b-topic-control",
        "nvidia/llama-3.1-nemotron-70b-reward",
        "nvidia/llama-3.1-nemotron-nano-4b-v1_1",
        "nvidia/llama-3.1-nemotron-nano-8b-v1",
        "nvidia/llama-3.1-nemotron-safety-guard-8b-v3",
        "nvidia/llama-3.1-nemotron-safety-guard-multilingual-8b-v1",
        "nvidia/llama-3.1-nemotron-ultra-253b-v1",
        "nvidia/llama-3.3-nemotron-super-49b-v1",
        "nvidia/llama-3.3-nemotron-super-49b-v1.5",
        "nvidia/llama3-chatqa-1.5-8b",
        "nvidia/llama3-chatqa-1.5-70b",
        "nvidia/nemotron-3-nano-30b-a3b",
        "nvidia/nemotron-4-mini-hindi-4b-instruct",
        "nvidia/nemotron-content-safety-reasoning-4b",
        "nvidia/nemotron-mini-4b-instruct",
        "nvidia/nvidia-nemotron-nano-9b-v2",
        "nvidia/riva-translate-4b-instruct-v1_1",
        "openai/gpt-oss-20b",
        "openai/gpt-oss-120b",
        "opengpt-x/teuken-7b-instruct-commercial-v0.4",
        "qwen/qwen2-7b-instruct",
        "qwen/qwen2.5-7b-instruct",
        "qwen/qwen2.5-coder-32b-instruct",
        "qwen/qwen2.5-coder-7b-instruct",
        "qwen/qwen3-235b-a22b",
        "qwen/qwen3-coder-480b-a35b-instruct",
        "qwen/qwen3-next-80b-a3b-instruct",
        "qwen/qwen3-next-80b-a3b-thinking",
        "qwen/qwq-32b",
        "rakuten/rakutenai-7b-chat",
        "rakuten/rakutenai-7b-instruct",
        "seallms/seallm-7b-v2.5",
        "sarvamai/sarvam-m",
        "speakleash/bielik-11b-v2_6-instruct",
        "stepfun-ai/step-3-5-flash",
        "stockmark/stockmark-2-100b-instruct",
        "thudm/chatglm3-6b",
        "tiiuae/falcon3-7b-instruct",
        "tokyotech-llm/llama-3-swallow-70b-instruct-v01",
        "upstage/solar-10.7b-instruct",
        "utter-project/eurollm-9b-instruct",
        "yentinglin/llama-3-taiwan-70b-instruct",
        "z-ai/glm4.7"
    };

    private static readonly string[] InceptionModels =
    {
        "mercury-2"
    };

    public int ProviderIndex
    {
        get => _providerIndex;
        set
        {
            if (_providerIndex == value) return;
            _providerIndex = value;

            Preferences.Set(ProviderKey, CurrentProvider switch
            {
                ChatApiClient.Provider.NvidiaIntegrate => "nvidia",
                ChatApiClient.Provider.Inception => "inception",
                _ => "cerebras"
            });

            // Load last model per provider.
            var (key, fallback) = CurrentProvider switch
            {
                ChatApiClient.Provider.NvidiaIntegrate => (NvidiaModelKey, "z-ai/glm5"),
                ChatApiClient.Provider.Inception => (InceptionModelKey, "mercury-2"),
                _ => (CerebrasModelKey, "gpt-oss-120b")
            };
            CurrentModel = Preferences.Get(key, fallback);

            OnPropertyChanged();
            OnPropertyChanged(nameof(ProviderSubtitle));
            OnPropertyChanged(nameof(ModelOptions));
            OnPropertyChanged(nameof(CurrentModelStatusText));

            _ = RefreshProviderModelsAsync(CurrentProvider);
        }
    }

    public string ProviderSubtitle
        => CurrentProvider switch
        {
            ChatApiClient.Provider.NvidiaIntegrate => "NVIDIA Integrate",
            ChatApiClient.Provider.Inception => "Inception",
            _ => "Cerebras"
        };

    private ChatApiClient.Provider CurrentProvider
        => ProviderIndex switch
        {
            1 => ChatApiClient.Provider.NvidiaIntegrate,
            2 => ChatApiClient.Provider.Inception,
            _ => ChatApiClient.Provider.Cerebras
        };

    public string CurrentModel
    {
        get => _currentModel;
        set
        {
            var v = (value ?? string.Empty).Trim();
            if (_currentModel == v) return;
            _currentModel = v;

            var key = CurrentProvider switch
            {
                ChatApiClient.Provider.NvidiaIntegrate => NvidiaModelKey,
                ChatApiClient.Provider.Inception => InceptionModelKey,
                _ => CerebrasModelKey
            };
            Preferences.Set(key, _currentModel);

            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentModelStatusText));

            if (CurrentProvider == ChatApiClient.Provider.NvidiaIntegrate)
                _ = VerifyNvidiaModelAsync(_currentModel);
        }
    }

    public IReadOnlyList<string> ModelOptions
        => CurrentProvider switch
        {
            ChatApiClient.Provider.NvidiaIntegrate => _nvidiaRuntimeModels.Count > 0
                ? _nvidiaRuntimeModels
                : NvidiaModels.Where(IsLikelyNvidiaChatModel).ToArray(),
            ChatApiClient.Provider.Inception => InceptionModels,
            _ => CerebrasModels
        };

    public string CurrentModelStatusText
    {
        get
        {
            if (CurrentProvider != ChatApiClient.Provider.NvidiaIntegrate)
                return "-";

            if (string.IsNullOrWhiteSpace(CurrentModel))
                return "-";

            if (_nvidiaCheckingModels.Contains(CurrentModel))
                return "Checking...";

            if (_nvidiaVerifiedModels.Contains(CurrentModel))
                return "Verified";

            if (_nvidiaRejectedModels.Contains(CurrentModel))
                return "Unavailable";

            return "Unverified";
        }
    }

    public Color CurrentModelStatusColor
    {
        get
        {
            if (CurrentProvider != ChatApiClient.Provider.NvidiaIntegrate)
                return Colors.LightGray;

            if (string.IsNullOrWhiteSpace(CurrentModel))
                return Colors.LightGray;

            if (_nvidiaCheckingModels.Contains(CurrentModel))
                return Colors.Gainsboro;

            if (_nvidiaVerifiedModels.Contains(CurrentModel))
                return Color.FromArgb("#6ED46E");

            if (_nvidiaRejectedModels.Contains(CurrentModel))
                return Color.FromArgb("#FF6B6B");

            return Colors.Silver;
        }
    }

    public Command<object> CopyMessageCommand { get; }
    public Command ToggleSettingsCommand { get; }
    public Command CloseSettingsCommand { get; }
    public Command RetryOfflineQueueCommand { get; }
    public ObservableCollection<SessionMeta> SessionItems { get; }

    public SessionMeta SelectedSession
    {
        get => _selectedSession;
        set
        {
            if (ReferenceEquals(_selectedSession, value)) return;
            _selectedSession = value;
            OnPropertyChanged();

            if (_syncingSessionSelection || value is null)
                return;

            _ = SwitchToSessionAsync(value.SessionId);
        }
    }

    public bool EnableOfflineFallback
    {
        get => _enableOfflineFallback;
        set
        {
            if (_enableOfflineFallback == value) return;
            _enableOfflineFallback = value;
            Preferences.Set(OfflineFallbackKey, value);
            OnPropertyChanged();
            NotifyOfflineUi();
        }
    }

    public bool QueueWhileOffline
    {
        get => _queueWhileOffline;
        set
        {
            if (_queueWhileOffline == value) return;
            _queueWhileOffline = value;
            Preferences.Set(OfflineQueueKey, value);
            OnPropertyChanged();
            NotifyOfflineUi();
        }
    }

    public bool EnableDcp
    {
        get => _enableDcp;
        set
        {
            if (_enableDcp == value) return;
            _enableDcp = value;
            Preferences.Set(EnableDcpKey, value);
            OnPropertyChanged();
        }
    }

    public string DcpSummaryText
    {
        get => _dcpSummaryText;
        private set
        {
            if (string.Equals(_dcpSummaryText, value, StringComparison.Ordinal)) return;
            _dcpSummaryText = value;
            OnPropertyChanged();
        }
    }

    public bool IsOffline
        => Connectivity.Current.NetworkAccess != NetworkAccess.Internet;

    public int OfflineQueueCount
        => _offlineQueue.Count;

    public bool ShowOfflineBanner
        => EnableOfflineFallback && (IsOffline || OfflineQueueCount > 0);

    public bool CanRetryOfflineQueue
        => EnableOfflineFallback && !IsOffline && OfflineQueueCount > 0 && !_isSending;

    public string OfflineBannerText
    {
        get
        {
            if (!EnableOfflineFallback) return string.Empty;
            if (IsOffline)
            {
                if (OfflineQueueCount > 0) return $"Offline • queued {OfflineQueueCount}";
                return QueueWhileOffline ? "Offline • queuing enabled" : "Offline";
            }

            return OfflineQueueCount > 0 ? $"Online • queued {OfflineQueueCount}" : string.Empty;
        }
    }

    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set
        {
            if (_isSettingsOpen == value) return;
            _isSettingsOpen = value;
            OnPropertyChanged();
        }
    }

    public bool EnableWebTools
    {
        get => _enableWebTools;
        set
        {
            if (_enableWebTools == value) return;
            _enableWebTools = value;
            Preferences.Set(EnableWebToolsKey, value);
            OnPropertyChanged();
        }
    }

    public bool EnableRemember
    {
        get => _enableRemember;
        set
        {
            if (_enableRemember == value) return;
            _enableRemember = value;
            Preferences.Set(EnableRememberKey, value);
            OnPropertyChanged();
        }
    }

    public bool ShowCommandTips
    {
        get => _showCommandTips;
        set
        {
            if (_showCommandTips == value) return;
            _showCommandTips = value;
            Preferences.Set(ShowCommandTipsKey, value);
            OnPropertyChanged();
        }
    }

    public bool StructuredAnswers
    {
        get => _structuredAnswers;
        set
        {
            if (_structuredAnswers == value) return;
            _structuredAnswers = value;
            Preferences.Set(StructuredAnswersKey, value);
            OnPropertyChanged();
        }
    }

    public bool ThinkingEnabled
    {
        get => _thinkingMode != "off";
        set
        {
            var desired = value ? (_thinkingMode == "off" ? "on" : _thinkingMode) : "off";
            if (string.Equals(_thinkingMode, desired, StringComparison.Ordinal)) return;
            _thinkingMode = desired;
            Preferences.Set(ThinkingModeKey, _thinkingMode);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ThinkingVerbose));
        }
    }

    public bool ThinkingVerbose
    {
        get => string.Equals(_thinkingMode, "verbose", StringComparison.Ordinal);
        set
        {
            if (!ThinkingEnabled && value)
                ThinkingEnabled = true;

            var desired = value ? "verbose" : (ThinkingEnabled ? "on" : "off");
            if (string.Equals(_thinkingMode, desired, StringComparison.Ordinal)) return;
            _thinkingMode = desired;
            Preferences.Set(ThinkingModeKey, _thinkingMode);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ThinkingEnabled));
        }
    }

    public ChatPage()
    {
        InitializeComponent();
        Messages = new ObservableCollection<ChatMessage>();
        SessionItems = new ObservableCollection<SessionMeta>();
        _cts = new CancellationTokenSource();

        // Shell route creation does not guarantee DI-based page construction.
        // Try to resolve from DI, otherwise fall back to local instances.
        var services = Application.Current?.Handler?.MauiContext?.Services;
        _api = services?.GetService<ChatApiClient>()
               ?? new ChatApiClient(
                    new HttpClient { BaseAddress = new Uri("https://api.cerebras.ai"), Timeout = TimeSpan.FromMinutes(10) },
                    // IMPORTANT: BaseAddress must end with a trailing '/' because ChatApiClient uses a relative path.
                    // Without it, HttpClient will drop the last segment (`v1`) and hit the wrong endpoint.
                    new HttpClient { BaseAddress = new Uri("https://integrate.api.nvidia.com/v1/"), Timeout = TimeSpan.FromMinutes(10) },
                    new HttpClient { BaseAddress = new Uri("https://api.inceptionlabs.ai"), Timeout = TimeSpan.FromMinutes(10) });
        _search = services?.GetService<FreeSearchClient>()
                  ?? new FreeSearchClient(
                      new SearxngSearchClient(new HttpClient { Timeout = TimeSpan.FromSeconds(20) }, "https://searx.be"),
                      new HttpClient { Timeout = TimeSpan.FromSeconds(20) },
                      new DdgSearchClient(new HttpClient { Timeout = TimeSpan.FromSeconds(15) }));
        _browse = services?.GetService<BrowseClient>()
                  ?? new BrowseClient(new HttpClient { Timeout = TimeSpan.FromSeconds(25) });
        _memory = services?.GetService<LocalMemoryStore>() ?? new LocalMemoryStore();
        _personalMemoryStore = services?.GetService<PersonalMemoryStore>() ?? new PersonalMemoryStore();
        _sessions = services?.GetService<LocalSessionStore>() ?? new LocalSessionStore();
        _sessionCatalog = services?.GetService<SessionCatalogStore>() ?? new SessionCatalogStore();
        _personaProfileStore = services?.GetService<PersonaProfileStore>() ?? new PersonaProfileStore();
        _promptContextAssembler = services?.GetService<PromptContextAssembler>() ?? new PromptContextAssembler();

        CopyMessageCommand = new Command<object>(async (param) => await CopyMessageAsync(param));
        ToggleSettingsCommand = new Command(() => IsSettingsOpen = !IsSettingsOpen);
        CloseSettingsCommand = new Command(() => IsSettingsOpen = false);
        RetryOfflineQueueCommand = new Command(() => _ = ProcessOfflineQueueAsync());

        BindingContext = this;
    }

    private void NotifyOfflineUi()
    {
        OnPropertyChanged(nameof(IsOffline));
        OnPropertyChanged(nameof(OfflineQueueCount));
        OnPropertyChanged(nameof(ShowOfflineBanner));
        OnPropertyChanged(nameof(CanRetryOfflineQueue));
        OnPropertyChanged(nameof(OfflineBannerText));
    }

    private async Task RefreshProviderModelsAsync(ChatApiClient.Provider provider)
    {
        if (provider != ChatApiClient.Provider.NvidiaIntegrate)
            return;

        try
        {
            var models = await _api.GetModelsAsync(provider, _cts.Token);
            if (models.Count == 0)
                return;

            var filtered = models.Where(IsLikelyNvidiaChatModel).ToArray();
            if (filtered.Length == 0)
                filtered = models.ToArray();

            _nvidiaRuntimeModels = filtered;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                OnPropertyChanged(nameof(ModelOptions));
                OnPropertyChanged(nameof(CurrentModelStatusText));

                if (CurrentProvider == ChatApiClient.Provider.NvidiaIntegrate
                    && !ModelOptions.Contains(CurrentModel, StringComparer.OrdinalIgnoreCase))
                {
                    CurrentModel = ModelOptions[0];
                }

                if (CurrentProvider == ChatApiClient.Provider.NvidiaIntegrate)
                    _ = VerifyNvidiaModelAsync(CurrentModel);
            });
        }
        catch
        {
            // Keep static list when discovery endpoint fails.
        }
    }

    private static bool IsLikelyNvidiaChatModel(string model)
    {
        if (string.IsNullOrWhiteSpace(model)) return false;
        var m = model.Trim().ToLowerInvariant();

        var blocked = new[]
        {
            "embed", "rerank", "reward", "guard", "safety", "nemoguard", "content-safety", "riva-translate"
        };
        if (blocked.Any(m.Contains)) return false;

        return m.Contains("instruct")
            || m.Contains("-chat")
            || m.EndsWith("-it", StringComparison.Ordinal)
            || m.Contains("glm")
            || m.Contains("gpt-oss")
            || m.Contains("qwq")
            || m.Contains("deepseek-r1");
    }

    private async Task VerifyNvidiaModelAsync(string model)
    {
        if (CurrentProvider != ChatApiClient.Provider.NvidiaIntegrate)
            return;
        if (string.IsNullOrWhiteSpace(model))
            return;
        if (_nvidiaVerifiedModels.Contains(model) || _nvidiaRejectedModels.Contains(model))
        {
            MainThread.BeginInvokeOnMainThread(() => OnPropertyChanged(nameof(CurrentModelStatusText)));
            return;
        }

        if (!await _modelProbeGate.WaitAsync(0))
            return;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var probeMessages = new object[]
            {
                new { role = "user", content = "Say OK" }
            };

            var gotAny = false;
            await foreach (var delta in _api.StreamChatAsync(ChatApiClient.Provider.NvidiaIntegrate, probeMessages, model, enableThinking: false, cts.Token)
                               .WithCancellation(cts.Token)
                               .ConfigureAwait(false))
            {
                if (string.IsNullOrWhiteSpace(delta)) continue;
                gotAny = true;
                break;
            }

            if (gotAny)
            {
                _nvidiaVerifiedModels.Add(model);
                _nvidiaRejectedModels.Remove(model);
            }
            else
            {
                _nvidiaRejectedModels.Add(model);
                _nvidiaVerifiedModels.Remove(model);
            }
        }
        catch (HttpRequestException)
        {
            _nvidiaRejectedModels.Add(model);
            _nvidiaVerifiedModels.Remove(model);
        }
        catch (OperationCanceledException)
        {
            // keep unknown status on timeout
        }
        finally
        {
            _modelProbeGate.Release();
            MainThread.BeginInvokeOnMainThread(() => OnPropertyChanged(nameof(CurrentModelStatusText)));
        }
    }

    private void OnConnectivityChanged(object sender, ConnectivityChangedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(NotifyOfflineUi);

        if (EnableOfflineFallback && QueueWhileOffline && e.NetworkAccess == NetworkAccess.Internet)
            _ = ProcessOfflineQueueAsync();
    }

    private async Task ProcessOfflineQueueAsync()
    {
        if (!EnableOfflineFallback || !QueueWhileOffline) return;
        if (IsOffline) return;
        if (OfflineQueueCount == 0) return;

        if (!await _offlineQueueGate.WaitAsync(0))
            return;

        var toggledSending = false;
        try
        {
            if (!_isSending)
            {
                toggledSending = true;
                _isSending = true;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    OnPropertyChanged(nameof(IsSending));
                    OnPropertyChanged(nameof(CanSend));
                    NotifyOfflineUi();
                });
            }

            while (OfflineQueueCount > 0 && !IsOffline)
            {
                PendingSend item;
                lock (_offlineQueue)
                {
                    if (_offlineQueue.Count == 0) break;
                    item = _offlineQueue.Dequeue();
                }

                MainThread.BeginInvokeOnMainThread(NotifyOfflineUi);

                // Stream into the existing placeholder assistant bubble.
                var assistant = item.Assistant;
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    assistant.Content = string.Empty;
                    assistant.IsEphemeral = true;
                });

                var pending = new StringBuilder();
                var captured = new StringBuilder();
                var sw = Stopwatch.StartNew();
                var lastFlushMs = 0L;
                const int FlushIntervalMs = 60;

                try
                {
                    await foreach (var delta in _api
                                       .StreamChatAsync(item.Provider, item.RequestMessages, item.Model, enableThinking: item.EnableThinking, _cts.Token)
                                       .WithCancellation(_cts.Token)
                                       .ConfigureAwait(false))
                    {
                        if (string.IsNullOrEmpty(delta)) continue;

                        pending.Append(delta);
                        captured.Append(delta);
                        var now = sw.ElapsedMilliseconds;
                        if (now - lastFlushMs < FlushIntervalMs) continue;

                        var chunk = pending.ToString();
                        pending.Clear();
                        lastFlushMs = now;

                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            assistant.Content += chunk;
                        });
                    }

                    if (pending.Length > 0)
                    {
                        var chunk = pending.ToString();
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            assistant.Content += chunk;
                        });
                    }

                    if (captured.Length > 0)
                    {
                        await AppendSessionRecordAsync(item.SessionId, "assistant", captured.ToString(), item.Model, string.Empty, _cts.Token);
                    }

                    await MainThread.InvokeOnMainThreadAsync(() => assistant.IsEphemeral = false);
                }
                catch (OperationCanceledException)
                {
                    // Ignore cancellations.
                    return;
                }
                catch (Exception ex)
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        assistant.Content = "Error: " + ex.Message + "\n\nTip: ketik /retry saat online.";
                        assistant.IsEphemeral = true;
                    });
                    return;
                }
            }
        }
        finally
        {
            if (toggledSending)
            {
                _isSending = false;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    OnPropertyChanged(nameof(IsSending));
                    OnPropertyChanged(nameof(CanSend));
                    NotifyOfflineUi();
                });
            }

            _offlineQueueGate.Release();
        }
    }

    private static bool LooksLikeUnauthorized(HttpRequestException ex)
    {
        var m = ex?.Message ?? string.Empty;
        return m.StartsWith("unauthorized", StringComparison.OrdinalIgnoreCase)
               || m.Contains("forbidden", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikePermanentClientError(HttpRequestException ex)
    {
        var m = ex?.Message ?? string.Empty;

        // Many upstreams return JSON bodies for 4xx. We don't want to queue these.
        if (m.Contains("\"status\":4", StringComparison.Ordinal)) return true;
        if (m.Contains("\"code\":4", StringComparison.Ordinal)) return true;
        if (m.StartsWith("HTTP 4", StringComparison.OrdinalIgnoreCase)) return true;
        if (m.Contains("bad request", StringComparison.OrdinalIgnoreCase)) return true;

        // NVIDIA Integrate specific: model/function not provisioned for this account.
        if (m.Contains("Function '", StringComparison.Ordinal) && m.Contains("Not found for account", StringComparison.Ordinal))
            return true;

        return false;
    }

    private void OnSettingsClicked(object sender, EventArgs e)
    {
        IsSettingsOpen = !IsSettingsOpen;
    }

    private void OnCloseSettingsClicked(object sender, EventArgs e)
    {
        IsSettingsOpen = false;
    }

    private static async Task CopyMessageAsync(object param)
    {
        if (param is ChatAyi.Models.ChatMessage m)
        {
            // Only copy assistant bubbles via double-tap.
            if (!string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase))
                return;

            param = m.Content;
        }

        var clean = (param?.ToString() ?? string.Empty).Trim();
        if (clean.Length == 0) return;

        await Clipboard.SetTextAsync(clean);
        SemanticScreenReader.Announce("Copied to clipboard");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            if (_forceFreshSessionOnAppear)
            {
                _forceFreshSessionOnAppear = false;
                await CreateNewSessionAsync();
            }

            if (!_connectivitySubscribed)
            {
                Connectivity.ConnectivityChanged += OnConnectivityChanged;
                _connectivitySubscribed = true;
            }

            if (!_sessionInitialized)
                await EnsureSessionCatalogInitializedAsync(CancellationToken.None);

            if (_checkedKey) return;
            _checkedKey = true;

            _thinkingMode = NormalizeThinkingMode(Preferences.Get(ThinkingModeKey, "off"));
            var provider = (Preferences.Get(ProviderKey, "cerebras") ?? "cerebras").Trim().ToLowerInvariant();
            _providerIndex = provider switch
            {
                "nvidia" => 1,
                "inception" => 2,
                _ => 0
            };
            var (modelKey, modelFallback) = provider switch
            {
                "nvidia" => (NvidiaModelKey, "z-ai/glm5"),
                "inception" => (InceptionModelKey, "mercury-2"),
                _ => (CerebrasModelKey, "gpt-oss-120b")
            };
            _currentModel = Preferences.Get(modelKey, modelFallback);
            if (!ModelOptions.Contains(_currentModel, StringComparer.OrdinalIgnoreCase))
                _currentModel = modelFallback;
            _enableWebTools = Preferences.Get(EnableWebToolsKey, true);
            _enableRemember = Preferences.Get(EnableRememberKey, true);
            _showCommandTips = Preferences.Get(ShowCommandTipsKey, true);
            _structuredAnswers = Preferences.Get(StructuredAnswersKey, true);
            _enableOfflineFallback = Preferences.Get(OfflineFallbackKey, true);
            _queueWhileOffline = Preferences.Get(OfflineQueueKey, true);
            _enableDcp = Preferences.Get(EnableDcpKey, true);

            OnPropertyChanged(nameof(EnableWebTools));
            OnPropertyChanged(nameof(EnableRemember));
            OnPropertyChanged(nameof(ShowCommandTips));
            OnPropertyChanged(nameof(ThinkingEnabled));
            OnPropertyChanged(nameof(ThinkingVerbose));
            OnPropertyChanged(nameof(StructuredAnswers));
            OnPropertyChanged(nameof(ProviderIndex));
            OnPropertyChanged(nameof(ProviderSubtitle));
            OnPropertyChanged(nameof(CurrentModel));
            OnPropertyChanged(nameof(ModelOptions));
            OnPropertyChanged(nameof(CurrentModelStatusText));
            OnPropertyChanged(nameof(EnableOfflineFallback));
            OnPropertyChanged(nameof(QueueWhileOffline));
            OnPropertyChanged(nameof(EnableDcp));
            OnPropertyChanged(nameof(DcpSummaryText));
            NotifyOfflineUi();

            try
            {
                await _memory.EnsureInitializedAsync(CancellationToken.None);
            }
            catch { }

            await EnsureApiKeyAsync(CurrentProvider);

            if (CurrentProvider == ChatApiClient.Provider.NvidiaIntegrate)
                await RefreshProviderModelsAsync(CurrentProvider);

            if (EnableOfflineFallback && QueueWhileOffline && !IsOffline)
                _ = ProcessOfflineQueueAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatPage] OnAppearing failed: {ex}");
            await DisplayAlert("Chat Initialization Error", ex.Message, "OK");
        }
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query is null)
            return;

        if (!query.TryGetValue("fresh", out var value) || value is null)
            return;

        var raw = value.ToString()?.Trim();
        _forceFreshSessionOnAppear = string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeThinkingMode(string raw)
    {
        var v = (raw ?? string.Empty).Trim().ToLowerInvariant();
        return v switch
        {
            "on" or "true" or "1" or "basic" => "on",
            "verbose" => "verbose",
            _ => "off"
        };
    }

    private string GetThinkingInstruction()
    {
        return _thinkingMode switch
        {
            "on" => "Think carefully internally before answering. Do not reveal your reasoning.",
            "verbose" => "First write a short Plan (3-6 bullets), then write the Answer. Do not reveal private reasoning.",
            _ => string.Empty
        };
    }

    private string GetResponseFormatInstruction(bool hasSources)
    {
        if (!StructuredAnswers) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("Format jawaban harus rapi dan ringkas:");
        sb.AppendLine("- Gunakan heading singkat: 'Jawaban', 'Poin Penting'.");
        if (hasSources)
            sb.AppendLine("- Untuk mode bersumber, WAJIB pakai format ini secara berurutan: [FAKTA], [INFERENSI], Sumber:.");
        if (hasSources)
            sb.AppendLine("- [FAKTA] hanya berisi klaim yang didukung sumber dan setiap klaim wajib punya sitasi [1], [2], dst.");
        if (hasSources)
            sb.AppendLine("- [INFERENSI] harus dipisah dari fakta. Jika tidak ada inferensi, tulis persis: [INFERENSI] Tidak ada inferensi tambahan.");
        if (hasSources)
            sb.AppendLine("- Bagian penutup wajib: 'Sumber:' lalu daftar minimal [1] ... sesuai data yang dipakai.");
        sb.AppendLine("- Jangan mengarang repo/tautan/fakta. Jika tidak yakin, tulis 'Gua belum yakin' dan minta link / gunakan /browse.");
        return sb.ToString().Trim();
    }

    private static string GetUnifiedVoiceInstruction()
    {
        return "Kontrak voice ChatAyi: selalu Bahasa Indonesia santai dengan pronomina 'gua/lu' secara konsisten. " +
               "Jangan campur ke 'kamu/Anda' atau gaya netral/formal kecuali user minta eksplisit.";
    }

    private static string EnforceStrictSearchTemplate(string raw, IReadOnlyList<FreeSearchClient.SearchResult> results)
    {
        var text = (raw ?? string.Empty).Trim();
        var facts = ExtractBulletLines(text, "[FAKTA]", "[INFERENSI]");
        if (facts.Count == 0)
            facts = FallbackBullets(text, 2);
        if (facts.Count == 0)
            facts.Add("Data sumber terbatas, jadi gua belum bisa kasih fakta kuat.");

        var infer = ExtractBulletLines(text, "[INFERENSI]", "Sumber:");
        if (infer.Count == 0)
            infer.Add("Tidak ada inferensi tambahan.");

        var ordered = (results ?? Array.Empty<FreeSearchClient.SearchResult>())
            .OrderBy(x => IsWikipediaUrl(x?.Url) ? 1 : 0)
            .Take(2)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("[FAKTA]");
        foreach (var item in facts.Take(2))
            sb.Append("- ").AppendLine(EnsureThirdPerson(item));
        sb.AppendLine();
        sb.AppendLine("[INFERENSI]");
        foreach (var item in infer.Take(2))
            sb.Append("- ").AppendLine(EnsureThirdPerson(item));
        sb.AppendLine();
        sb.AppendLine("Sumber:");

        if (ordered.Count == 0)
        {
            sb.AppendLine("[1] Sumber valid tidak tersedia.");
        }
        else
        {
            for (var i = 0; i < ordered.Count; i++)
                sb.Append('[').Append(i + 1).Append("] ").AppendLine(ordered[i].Url);
        }

        return sb.ToString().TrimEnd();
    }

    private static string EnforceStrictBrowseTemplate(string raw, string sourceUrl)
    {
        var text = (raw ?? string.Empty).Trim();
        var ringkasan = ExtractBulletLines(text, "[RINGKASAN]", "[POIN PENTING]");
        if (ringkasan.Count == 0)
            ringkasan = FallbackBullets(text, 3);
        if (ringkasan.Count == 0)
            ringkasan.Add("Konten halaman terbatas, jadi gua cuma bisa kasih ringkasan minimum.");

        var poin = ExtractBulletLines(text, "[POIN PENTING]", "Sumber:");
        if (poin.Count == 0)
            poin = ringkasan.Take(2).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("[RINGKASAN]");
        foreach (var item in ringkasan.Take(3))
            sb.Append("- ").AppendLine(item);
        sb.AppendLine();
        sb.AppendLine("[POIN PENTING]");
        foreach (var item in poin.Take(3))
            sb.Append("- ").AppendLine(item);
        sb.AppendLine();
        sb.AppendLine("Sumber:");
        sb.Append("[1] ").AppendLine(string.IsNullOrWhiteSpace(sourceUrl) ? "-" : sourceUrl.Trim());
        return sb.ToString().TrimEnd();
    }

    private static List<string> ExtractBulletLines(string text, string startMarker, string endMarker)
    {
        var source = (text ?? string.Empty);
        var start = source.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return new List<string>();

        start += startMarker.Length;
        var end = source.Length;
        if (!string.IsNullOrWhiteSpace(endMarker))
        {
            var idx = source.IndexOf(endMarker, start, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                end = idx;
        }

        var body = source.Substring(start, Math.Max(0, end - start));
        var lines = body
            .Split('\n')
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Select(x => x.StartsWith("- ", StringComparison.Ordinal) ? x.Substring(2).Trim() : x)
            .Where(x => x.Length > 0)
            .Where(x => !x.StartsWith("[", StringComparison.Ordinal))
            .ToList();

        return lines;
    }

    private static List<string> FallbackBullets(string text, int maxItems)
    {
        var lines = (text ?? string.Empty)
            .Split('\n')
            .Select(x => x.Trim())
            .Where(x => x.Length >= 12)
            .Where(x => !x.StartsWith("[", StringComparison.Ordinal))
            .Where(x => !x.StartsWith("Sumber", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.StartsWith("- ", StringComparison.Ordinal) ? x.Substring(2).Trim() : x)
            .Distinct(StringComparer.Ordinal)
            .Take(Math.Max(1, maxItems))
            .ToList();

        return lines;
    }

    private static string EnsureThirdPerson(string line)
    {
        var value = (line ?? string.Empty).Trim();
        if (value.Length == 0)
            return value;

        value = value.Replace("Gua ", "Subjek ini ", StringComparison.OrdinalIgnoreCase)
                     .Replace("Gue ", "Subjek ini ", StringComparison.OrdinalIgnoreCase)
                     .Replace("Aku ", "Subjek ini ", StringComparison.OrdinalIgnoreCase)
                     .Replace("Saya ", "Subjek ini ", StringComparison.OrdinalIgnoreCase);
        return value;
    }

    private static string GetStrictSearchTemplateInstruction()
    {
        var sb = new StringBuilder();
        sb.AppendLine("KONSTRAINT OUTPUT /search (WAJIB):");
        sb.AppendLine("- Output HARUS berupa bullet template tetap, bukan paragraf bebas.");
        sb.AppendLine("- Dilarang gaya ensiklopedik/rangkuman artikel panjang.");
        sb.AppendLine("- Gunakan sudut pandang pihak ketiga yang netral, bukan roleplay orang pertama.");
        sb.AppendLine("- Semua klaim di [FAKTA] wajib bersitasi [n] yang cocok dengan bagian Sumber.");
        sb.AppendLine("- Jika data sumber lemah/kurang/konflik, tulis keterbatasan data di [FAKTA].");
        sb.AppendLine("- Jika inferensi tidak ada, wajib tulis tepat: '- Tidak ada inferensi tambahan.' di [INFERENSI].");
        sb.AppendLine("- Sebelum final, validasi output tetap mengikuti template ini.");
        sb.AppendLine();
        sb.AppendLine("Template wajib:");
        sb.AppendLine("[FAKTA]");
        sb.AppendLine("- <klaim faktual bersitasi [1]> ");
        sb.AppendLine("- <klaim faktual bersitasi [2]> ");
        sb.AppendLine("[INFERENSI]");
        sb.AppendLine("- <inferensi berbasis fakta>  ATAU  - Tidak ada inferensi tambahan.");
        sb.AppendLine("Sumber:");
        sb.AppendLine("[1] <url/sumber>");
        sb.AppendLine("[2] <url/sumber>");
        sb.AppendLine();
        sb.AppendLine("Jika tidak ada sumber valid sama sekali, tetap pakai template di atas dan jelaskan keterbatasan pada [FAKTA].");
        return sb.ToString().Trim();
    }

    private static string GetStrictBrowseTemplateInstruction()
    {
        var sb = new StringBuilder();
        sb.AppendLine("KONSTRAINT OUTPUT /browse (WAJIB):");
        sb.AppendLine("- Jawaban akhir HARUS Bahasa Indonesia.");
        sb.AppendLine("- Ringkas, jelas, dan langsung ke inti (hindari paragraf panjang).");
        sb.AppendLine("- Maksimal total 6 bullet (gabungan semua section).");
        sb.AppendLine("- Jangan copy-tempel kalimat panjang dari halaman sumber.");
        sb.AppendLine("- Jika sumber berbahasa Inggris, parafrasekan ke Bahasa Indonesia.");
        sb.AppendLine("- Jika konten tidak cukup jelas/noisy, akui keterbatasan data secara jujur.");
        sb.AppendLine("- Tetap sudut pandang pihak ketiga, jangan roleplay subjek halaman.");
        sb.AppendLine();
        sb.AppendLine("Template wajib:");
        sb.AppendLine("[RINGKASAN]");
        sb.AppendLine("- <2-4 poin inti>");
        sb.AppendLine("[POIN PENTING]");
        sb.AppendLine("- <fakta penting dari halaman, singkat>");
        sb.AppendLine("Sumber:");
        sb.AppendLine("[1] <url>");
        return sb.ToString().Trim();
    }

    private static string BuildSafetyAndBoundariesInstruction(
        string purpose,
        string thinkingInstruction,
        string responseFormatInstruction,
        params string[] optionalContextBlocks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("System safety + app boundaries:");
        sb.AppendLine("- Follow safety policy and refuse unsafe requests.");
        sb.AppendLine("- Stay within app scope and avoid fabricating sources or facts.");
        sb.AppendLine("- " + purpose);

        if (!string.IsNullOrWhiteSpace(thinkingInstruction))
            sb.AppendLine("- " + thinkingInstruction.Trim());

        if (!string.IsNullOrWhiteSpace(responseFormatInstruction))
            sb.AppendLine(responseFormatInstruction.Trim());

        if (optionalContextBlocks is not null)
        {
            foreach (var block in optionalContextBlocks)
            {
                if (string.IsNullOrWhiteSpace(block))
                    continue;

                sb.AppendLine();
                sb.AppendLine(block.Trim());
            }
        }

        return sb.ToString().Trim();
    }

    private async Task<string> GetOrCreateActiveSessionIdAsync(CancellationToken ct)
    {
        try
        {
            var active = await _sessionCatalog.GetActiveSessionIdAsync(ct);
            if (LocalSessionStore.IsSafeSessionId(active))
                return active;
        }
        catch
        {
            // fallback to preference-backed session id
        }

        var fallback = _sessions.GetOrCreateSessionId();
        if (LocalSessionStore.IsSafeSessionId(fallback))
        {
            try
            {
                await _sessionCatalog.SetActiveSessionIdAsync(fallback, ct);
            }
            catch
            {
                // non-blocking fallback path
            }
        }

        return fallback;
    }

    private async void OnNewSessionClicked(object sender, EventArgs e)
    {
        await CreateNewSessionAsync();
    }

    private async Task CreateNewSessionAsync()
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var ct = CancellationToken.None;

        await _sessionCatalog.TouchAsync(sessionId, "New Session", DateTimeOffset.UtcNow, ct);
        await _sessionCatalog.SetActiveSessionIdAsync(sessionId, ct);

        ResetEphemeralStateForSessionSwitch();
        await HydrateMessagesAsync(sessionId, ct);
        await RefreshSessionSelectorAsync(ct, sessionId);

        _sessionInitialized = true;
    }

    private async Task SwitchToSessionAsync(string sessionId)
    {
        if (!SessionMeta.IsSafeSessionId(sessionId))
            return;

        var ct = CancellationToken.None;
        await _sessionCatalog.SetActiveSessionIdAsync(sessionId, ct);

        ResetEphemeralStateForSessionSwitch();
        await HydrateMessagesAsync(sessionId, ct);
        await RefreshSessionSelectorAsync(ct, sessionId);

        _sessionInitialized = true;
    }

    private async Task EnsureSessionCatalogInitializedAsync(CancellationToken ct)
    {
        if (_sessionInitialized)
            return;

        var activeSessionId = await GetOrCreateActiveSessionIdAsync(ct);
        var list = await _sessionCatalog.ListAsync(ct);
        if (!list.Any(x => string.Equals(x.SessionId, activeSessionId, StringComparison.Ordinal)))
            await _sessionCatalog.TouchAsync(activeSessionId, "New Session", DateTimeOffset.UtcNow, ct);

        await _sessionCatalog.SetActiveSessionIdAsync(activeSessionId, ct);

        await HydrateMessagesAsync(activeSessionId, ct);
        await RefreshSessionSelectorAsync(ct, activeSessionId);

        _sessionInitialized = true;
    }

    private async Task RefreshSessionSelectorAsync(CancellationToken ct, string preferredSessionId = null)
    {
        var list = (await _sessionCatalog.ListAsync(ct)).ToList();
        if (list.Count == 0)
            return;

        var selectedSessionId = SessionMeta.IsSafeSessionId(preferredSessionId)
            ? preferredSessionId
            : await _sessionCatalog.GetActiveSessionIdAsync(ct);
        if (!SessionMeta.IsSafeSessionId(selectedSessionId))
            selectedSessionId = list[0].SessionId;

        _syncingSessionSelection = true;
        try
        {
            SessionItems.Clear();
            foreach (var item in list)
                SessionItems.Add(item);

            SelectedSession = SessionItems.FirstOrDefault(x => string.Equals(x.SessionId, selectedSessionId, StringComparison.Ordinal))
                ?? SessionItems.FirstOrDefault();
        }
        finally
        {
            _syncingSessionSelection = false;
        }

        OnPropertyChanged(nameof(SessionItems));
    }

    private void ResetEphemeralStateForSessionSwitch()
    {
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        _isMemoryTemporarilyOff = false;

        _isSending = false;

        lock (_offlineQueue)
        {
            _offlineQueue.Clear();
        }

        OnPropertyChanged(nameof(IsSending));
        OnPropertyChanged(nameof(CanSend));
        NotifyOfflineUi();
    }

    private async Task HydrateMessagesAsync(string sessionId, CancellationToken ct)
    {
        var transcript = await _sessions.ReadTranscriptAsync(sessionId, ct);
        var visible = transcript
            .Where(x => x.Role is "user" or "assistant")
            .Select(x => new ChatMessage(x.Role, x.Content))
            .ToList();

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            Messages.Clear();
            foreach (var item in visible)
                Messages.Add(item);
        });
    }

    private async Task<SessionContextSnapshot> BuildSessionContextSnapshotAsync(string sessionId, CancellationToken ct)
    {
        try
        {
            var recent = await _sessions.ReadRecentChatAsync(sessionId, 6, ct);
            var transcript = await _sessions.ReadTranscriptAsync(sessionId, ct);
            var summary = TryExtractSummaryBullets(transcript);
            return SessionContextSnapshot.Create(sessionId, recent, summary);
        }
        catch
        {
            return SessionContextSnapshot.Create(sessionId, Enumerable.Empty<(string Role, string Content)>(), Enumerable.Empty<string>());
        }
    }

    private async Task<SessionContextSnapshot> BuildSessionContextSnapshotForNormalChatAsync(string sessionId, CancellationToken ct)
    {
        return await BuildSessionContextSnapshotWithoutCommandsAsync(sessionId, ct, "normal-chat");
    }

    private async Task<SessionContextSnapshot> BuildSessionContextSnapshotForSearchAsync(string sessionId, CancellationToken ct)
    {
        return await BuildSessionContextSnapshotWithoutCommandsAsync(sessionId, ct, "search");
    }

    private async Task<SessionContextSnapshot> BuildSessionContextSnapshotForBrowseAsync(string sessionId, CancellationToken ct)
    {
        return await BuildSessionContextSnapshotWithoutCommandsAsync(sessionId, ct, "browse");
    }

    private async Task<SessionContextSnapshot> BuildSessionContextSnapshotWithoutCommandsAsync(string sessionId, CancellationToken ct, string branch)
    {
        try
        {
            var transcript = await _sessions.ReadTranscriptAsync(sessionId, ct);
            var recent = BuildRecentNormalChatTurns(transcript, maxTurns: 6);
            var summary = TryExtractSummaryBullets(transcript);
            Debug.WriteLine($"[ContextFilter] branch={branch} recent={recent.Count}");
            return SessionContextSnapshot.Create(sessionId, recent, summary);
        }
        catch
        {
            return SessionContextSnapshot.Create(sessionId, Enumerable.Empty<(string Role, string Content)>(), Enumerable.Empty<string>());
        }
    }

    private static IReadOnlyList<(string Role, string Content)> BuildRecentNormalChatTurns(
        IReadOnlyList<LocalSessionStore.SessionTranscriptEntry> transcript,
        int maxTurns)
    {
        if (transcript is null || transcript.Count == 0)
            return Array.Empty<(string Role, string Content)>();

        var filtered = new List<(string Role, string Content)>();
        var skipNextAssistant = false;
        var skippedCommandTurns = 0;
        var skippedAssistantAfterCommand = 0;

        foreach (var entry in transcript)
        {
            var role = (entry.Role ?? string.Empty).Trim().ToLowerInvariant();
            var content = (entry.Content ?? string.Empty).Trim();
            if (content.Length == 0)
                continue;

            if (role == "user")
            {
                if (IsCommandMessage(content))
                {
                    skipNextAssistant = true;
                    skippedCommandTurns++;
                    continue;
                }

                skipNextAssistant = false;
                filtered.Add(("user", content));
                continue;
            }

            if (role == "assistant")
            {
                if (skipNextAssistant)
                {
                    skipNextAssistant = false;
                    skippedAssistantAfterCommand++;
                    continue;
                }

                filtered.Add(("assistant", content));
            }
        }

        if (skippedCommandTurns > 0 || skippedAssistantAfterCommand > 0)
        {
            Debug.WriteLine($"[ContextFilter] removed command turns={skippedCommandTurns}, removed assistant command replies={skippedAssistantAfterCommand}, remaining={filtered.Count}");
        }

        if (filtered.Count <= maxTurns)
            return filtered;

        return filtered.Skip(filtered.Count - maxTurns).ToList();
    }

    private static bool IsCommandMessage(string text)
    {
        var value = (text ?? string.Empty).TrimStart();
        return value.StartsWith("/", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> TryExtractSummaryBullets(IReadOnlyList<LocalSessionStore.SessionTranscriptEntry> transcript)
    {
        if (transcript is null || transcript.Count == 0)
            return Array.Empty<string>();

        for (var i = transcript.Count - 1; i >= 0; i--)
        {
            var entry = transcript[i];
            if (!string.Equals(entry.Role, "system", StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.IsNullOrWhiteSpace(entry.Content))
                continue;

            var bullets = entry.Content
                .Split('\n')
                .Select(x => x.Trim())
                .Where(x => x.StartsWith("- ", StringComparison.Ordinal) || x.StartsWith("* ", StringComparison.Ordinal))
                .Select(x => x[2..].Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Take(5)
                .ToList();

            if (bullets.Count > 0)
                return bullets;
        }

        return Array.Empty<string>();
    }

    private async Task AppendSessionRecordAsync(
        string sessionId,
        string role,
        string content,
        string model,
        string titleSource,
        CancellationToken ct)
    {
        var safeRole = (role ?? string.Empty).Trim().ToLowerInvariant();
        var safeContent = (content ?? string.Empty).Trim();
        var safeModel = (model ?? string.Empty).Trim();
        var title = safeRole == "user" ? BuildSessionTitle(titleSource) : string.Empty;

        try
        {
            await _sessions.AppendWithMetadataAsync(
                sessionId,
                new
                {
                    ts = DateTimeOffset.UtcNow,
                    role = safeRole,
                    content = safeContent,
                    model = safeModel
                },
                _sessionCatalog,
                title,
                ct);

            if (safeRole == "user")
                await _sessionCatalog.SetActiveSessionIdAsync(sessionId, ct);
        }
        catch
        {
            await _sessions.AppendAsync(sessionId, new
            {
                ts = DateTimeOffset.UtcNow,
                role = safeRole,
                content = safeContent,
                model = safeModel
            }, ct);
        }

        try
        {
            await RefreshSessionSelectorAsync(CancellationToken.None, sessionId);
        }
        catch
        {
            // Non-blocking UI refresh.
        }
    }

    private static string BuildSessionTitle(string text)
    {
        var cleaned = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
            return "New Session";

        return cleaned.Length <= 60 ? cleaned : cleaned[..60].TrimEnd() + "...";
    }

    private static string TrimForContext(string s, int maxChars)
    {
        s ??= string.Empty;
        if (s.Length <= maxChars) return s;
        return s.Substring(0, maxChars) + "\n\n[...truncated...]";
    }

    private static bool TryGetRoleAndContent(object message, out string role, out string content)
    {
        role = string.Empty;
        content = string.Empty;
        if (message is null) return false;

        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(message));
            var root = doc.RootElement;
            if (!root.TryGetProperty("role", out var roleEl)) return false;
            if (!root.TryGetProperty("content", out var contentEl)) return false;

            role = roleEl.GetString() ?? string.Empty;
            content = contentEl.ValueKind == JsonValueKind.String
                ? contentEl.GetString() ?? string.Empty
                : contentEl.ToString();

            return !string.IsNullOrWhiteSpace(role);
        }
        catch
        {
            return false;
        }
    }

    private List<object> ApplyDcp(List<object> requestMessages, ChatApiClient.Provider provider)
    {
        if (!EnableDcp || requestMessages.Count == 0)
            return requestMessages;

        var parsed = new List<DynamicContextPruner.Message>(requestMessages.Count);
        for (var i = 0; i < requestMessages.Count; i++)
        {
            if (!TryGetRoleAndContent(requestMessages[i], out var role, out var content))
                return requestMessages;

            var pinned = (i == 0 && string.Equals(role, "system", StringComparison.OrdinalIgnoreCase))
                         || (i == requestMessages.Count - 1 && string.Equals(role, "user", StringComparison.OrdinalIgnoreCase));

            parsed.Add(new DynamicContextPruner.Message(role, content, pinned));
        }

        var options = provider == ChatApiClient.Provider.NvidiaIntegrate
            ? new DynamicContextPruner.Options(MaxChars: 12000, PreserveRecentMessages: 8)
            : new DynamicContextPruner.Options(MaxChars: 26000, PreserveRecentMessages: 12);

        var pruned = _dcp.Prune(parsed, options);
        if (!pruned.Pruned)
        {
            DcpSummaryText = "No prune";
            return requestMessages;
        }

        var savedApproxTokens = Math.Max(0, pruned.SavedChars / 4);
        DcpSummaryText = $"Saved ~{savedApproxTokens} tok";

        return pruned.Messages
            .Select(m => (object)new { role = m.Role, content = m.Content })
            .ToList();
    }

    private bool TryHandleThinkingCommand(string prompt, ChatMessage assistant)
    {
        if (!prompt.StartsWith("/thinking", StringComparison.OrdinalIgnoreCase))
            return false;

        var rest = prompt.Length > 9 ? prompt.Substring(9).Trim() : string.Empty;
        if (string.IsNullOrWhiteSpace(rest) || rest.Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            assistant.Content = $"Thinking mode: {_thinkingMode}\nUsage: /thinking on | /thinking off | /thinking verbose";
            return true;
        }

        var arg = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0].Trim().ToLowerInvariant();
        _thinkingMode = NormalizeThinkingMode(arg);
        Preferences.Set(ThinkingModeKey, _thinkingMode);
        OnPropertyChanged(nameof(ThinkingEnabled));
        OnPropertyChanged(nameof(ThinkingVerbose));

        assistant.Content = $"Thinking mode set to: {_thinkingMode}";
        return true;
    }

    private bool TryHandleRetryCommand(string prompt, ChatMessage assistant)
    {
        if (!prompt.StartsWith("/retry", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!EnableOfflineFallback || !QueueWhileOffline)
        {
            assistant.Content = "Offline queue is disabled.";
            return true;
        }

        if (OfflineQueueCount == 0)
        {
            assistant.Content = "No queued messages.";
            return true;
        }

        if (IsOffline)
        {
            assistant.Content = $"Offline. Queued: {OfflineQueueCount}. Will retry automatically when online.";
            return true;
        }

        assistant.Content = "Retrying queued messages...";
        _ = ProcessOfflineQueueAsync();
        return true;
    }

    private async Task<bool> TryHandleMemoryCommandAsync(string prompt, ChatMessage assistant, CancellationToken ct)
    {
        if (!prompt.StartsWith("/memory", StringComparison.OrdinalIgnoreCase))
            return false;

        var rest = prompt.Length > 7 ? prompt.Substring(7).Trim() : string.Empty;
        if (string.IsNullOrWhiteSpace(rest))
        {
            assistant.Content = "Usage: /memory list | /memory add [category] <content> | /memory update <id> [category] <content> | /memory delete <id> | /memory off | /memory on";
            return true;
        }

        var parts = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var action = parts[0].Trim().ToLowerInvariant();
        var payload = parts.Length > 1 ? parts[1].Trim() : string.Empty;

        try
        {
            switch (action)
            {
                case "list":
                case "ls":
                    {
                        var items = await _personalMemoryStore.ListAsync(ct);
                        if (items.Count == 0)
                        {
                            assistant.Content = "Memory list kosong.";
                            return true;
                        }

                        var sb = new StringBuilder();
                        sb.AppendLine($"Memory items: {items.Count}");
                        foreach (var item in items.Take(20))
                        {
                            sb.Append("- ");
                            sb.Append(item.MemoryId);
                            sb.Append(" [");
                            sb.Append(item.Category);
                            sb.Append("] ");
                            sb.AppendLine(item.Content);
                        }

                        if (items.Count > 20)
                            sb.AppendLine($"... {items.Count - 20} item lainnya tidak ditampilkan");

                        assistant.Content = sb.ToString().TrimEnd();
                        return true;
                    }
                case "add":
                case "save":
                case "create":
                    {
                        if (string.IsNullOrWhiteSpace(payload))
                        {
                            assistant.Content = "Usage: /memory add [preference|active_project|important_info] <content>";
                            return true;
                        }

                        var (category, content) = ParseMemoryCategoryAndContent(payload, defaultCategory: PersonalMemoryItem.CategoryPreference);
                        if (string.IsNullOrWhiteSpace(content))
                        {
                            assistant.Content = "Error: content memory wajib diisi.";
                            return true;
                        }

                        var created = await _personalMemoryStore.AddAsync(category, content, ct);
                        assistant.Content = $"Memory saved: id={created.MemoryId} [{created.Category}] {created.Content}";
                        return true;
                    }
                case "update":
                case "edit":
                    {
                        if (string.IsNullOrWhiteSpace(payload))
                        {
                            assistant.Content = "Usage: /memory update <id> [preference|active_project|important_info] <content>";
                            return true;
                        }

                        var updateParts = payload.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                        if (updateParts.Length < 2)
                        {
                            assistant.Content = "Usage: /memory update <id> [preference|active_project|important_info] <content>";
                            return true;
                        }

                        var memoryId = updateParts[0].Trim();
                        var tail = updateParts[1].Trim();
                        var existing = (await _personalMemoryStore.ListAsync(ct))
                            .FirstOrDefault(x => string.Equals(x.MemoryId, memoryId, StringComparison.Ordinal));
                        if (existing is null)
                        {
                            assistant.Content = $"Memory id '{memoryId}' tidak ditemukan.";
                            return true;
                        }

                        var (category, content) = ParseMemoryCategoryAndContent(tail, existing.Category);
                        if (string.IsNullOrWhiteSpace(content))
                        {
                            assistant.Content = "Error: content memory wajib diisi.";
                            return true;
                        }

                        var updated = await _personalMemoryStore.UpdateAsync(memoryId, category, content, ct);
                        assistant.Content = $"Memory updated: id={updated.MemoryId} [{updated.Category}] {updated.Content}";
                        return true;
                    }
                case "delete":
                case "del":
                case "rm":
                    {
                        var memoryId = payload.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(memoryId))
                        {
                            assistant.Content = "Usage: /memory delete <id>";
                            return true;
                        }

                        var deleted = await _personalMemoryStore.DeleteAsync(memoryId, ct);
                        assistant.Content = deleted
                            ? $"Memory deleted: id={memoryId}"
                            : $"Memory id '{memoryId}' tidak ditemukan.";
                        return true;
                    }
                case "off":
                    _isMemoryTemporarilyOff = true;
                    assistant.Content = "Memory mode: OFF (sementara untuk sesi aktif).";
                    return true;
                case "on":
                    _isMemoryTemporarilyOff = false;
                    assistant.Content = "Memory mode: ON (sesi aktif).";
                    return true;
                case "status":
                    assistant.Content = _isMemoryTemporarilyOff
                        ? "Memory mode: OFF (temporary session mode)."
                        : "Memory mode: ON (default session mode).";
                    return true;
                default:
                    assistant.Content = "Unknown /memory command. Gunakan: list, add, update, delete, off, on, status.";
                    return true;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException)
        {
            assistant.Content = "Memory command gagal: " + ex.Message;
            return true;
        }
    }

    private async Task<bool> TryHandleNaturalMemorySaveIntentAsync(string prompt, ChatMessage assistant, CancellationToken ct)
    {
        if (!TryExtractExplicitMemorySaveText(prompt, out var content))
            return false;

        if (string.IsNullOrWhiteSpace(content))
        {
            assistant.Content = "Siap, tapi kontennya belum ada. Contoh: 'ingat ini saya suka jawaban ringkas'.";
            return true;
        }

        var category = PersonalMemoryItem.NormalizeCategory(content);
        var created = await _personalMemoryStore.AddAsync(category, content, ct);
        assistant.Content = $"Memory saved from explicit intent: id={created.MemoryId} [{created.Category}] {created.Content}";
        return true;
    }

    private static (string Category, string Content) ParseMemoryCategoryAndContent(string payload, string defaultCategory)
    {
        var text = (payload ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return (defaultCategory, string.Empty);

        var tokens = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return (defaultCategory, string.Empty);

        if (!TryMapMemoryCategoryAlias(tokens[0], out var category))
            return (defaultCategory, text);

        var content = tokens.Length > 1 ? tokens[1].Trim() : string.Empty;
        return (category, content);
    }

    private static bool TryExtractExplicitMemorySaveText(string prompt, out string content)
    {
        content = string.Empty;
        if (string.IsNullOrWhiteSpace(prompt))
            return false;

        var text = prompt.Trim();
        var lowered = text.ToLowerInvariant();
        var triggers = new[] { "tolong simpan ini", "ingat ini" };

        var bestIndex = -1;
        var matchedTrigger = string.Empty;
        foreach (var trigger in triggers)
        {
            var idx = lowered.IndexOf(trigger, StringComparison.Ordinal);
            if (idx < 0)
                continue;

            if (bestIndex < 0 || idx < bestIndex)
            {
                bestIndex = idx;
                matchedTrigger = trigger;
            }
        }

        if (bestIndex < 0)
            return false;

        var start = bestIndex + matchedTrigger.Length;
        content = start >= text.Length
            ? string.Empty
            : text[start..].TrimStart(':', ';', '-', ',', ' ').Trim();
        return true;
    }

    private async Task<bool> TryHandleIdentityQuestionAsync(string prompt, ChatMessage assistant, CancellationToken ct)
    {
        if (!IsIdentityNameQuestion(prompt))
            return false;

        var sessionId = await GetOrCreateActiveSessionIdAsync(ct);
        var name = await _personalMemoryStore.GetLatestIdentityNameAsync(ct);

        assistant.Content = string.IsNullOrWhiteSpace(name)
            ? "Gua belum tahu nama lu."
            : $"Nama lu {name}.";

        await AppendSessionRecordAsync(sessionId, "user", prompt, string.Empty, prompt, ct);
        await AppendSessionRecordAsync(sessionId, "assistant", assistant.Content, string.Empty, string.Empty, ct);
        return true;
    }

    private static bool IsIdentityNameQuestion(string prompt)
    {
        var raw = (prompt ?? string.Empty).Trim();
        if (raw.Length == 0 || raw.Length > 50)
            return false;

        var normalized = NormalizeSimple(raw);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        return normalized is "nama gua siapa"
            or "siapa nama gua"
            or "nama saya siapa"
            or "siapa nama saya";
    }

    private static string NormalizeSimple(string value)
    {
        var text = (value ?? string.Empty).ToLowerInvariant();
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'))
                sb.Append(ch);
            else
                sb.Append(' ');
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool TryMapMemoryCategoryAlias(string raw, out string category)
    {
        category = PersonalMemoryItem.CategoryPreference;
        var token = (raw ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_');
        if (string.IsNullOrWhiteSpace(token))
            return false;

        switch (token)
        {
            case "preference":
            case "preferensi":
            case "pref":
                category = PersonalMemoryItem.CategoryPreference;
                return true;
            case "active_project":
            case "project":
            case "proyek":
                category = PersonalMemoryItem.CategoryActiveProject;
                return true;
            case "important_info":
            case "important":
            case "penting":
            case "info":
                category = PersonalMemoryItem.CategoryImportantInfo;
                return true;
            default:
                return false;
        }
    }

    private async Task<bool> EnsureApiKeyAsync(ChatApiClient.Provider provider)
    {
        try
        {
            var key = await _api.GetApiKeyAsync(provider);
            if (!string.IsNullOrWhiteSpace(key)) return true;

            var (title, msg, placeholder) = provider switch
            {
                ChatApiClient.Provider.NvidiaIntegrate => (
                    "NVIDIA API Key",
                    "Masukkan API key NVIDIA (disimpan di SecureStorage).",
                    "nvapi-..."),
                ChatApiClient.Provider.Inception => (
                    "Inception API Key",
                    "Masukkan API key Inception (disimpan di SecureStorage).",
                    "inception-..."),
                _ => (
                    "Cerebras API Key",
                    "Masukkan API key Cerebras (disimpan di SecureStorage).",
                    "csk-...")
            };

            var entered = await DisplayPromptAsync(
                title,
                msg,
                "Save",
                "Cancel",
                placeholder: placeholder,
                maxLength: 200,
                keyboard: Keyboard.Text,
                initialValue: "");

            if (string.IsNullOrWhiteSpace(entered)) return false;
            await _api.SetApiKeyAsync(provider, entered);

            if (provider == ChatApiClient.Provider.NvidiaIntegrate)
            {
                _nvidiaVerifiedModels.Clear();
                _nvidiaRejectedModels.Clear();
                await RefreshProviderModelsAsync(provider);
                OnPropertyChanged(nameof(CurrentModelStatusText));
            }

            return true;
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
            return false;
        }
    }

    private static bool TryParseBrowseCommand(string input, out string url, out string question)
    {
        url = string.Empty;
        question = string.Empty;

        // "/browse" is 7 chars including '/'.
        var rest = input.Length > 7 ? input.Substring(7).Trim() : string.Empty;
        if (string.IsNullOrWhiteSpace(rest)) return false;

        var parts = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        url = parts[0].Trim().Trim('"', '\'', '<', '>', '(', ')', '[', ']', '{', '}', ',', '.');
        question = parts.Length > 1 ? parts[1].Trim() : string.Empty;
        return url.Length > 0;
    }

    private static bool TryParseSearchCommand(string input, out string query)
    {
        query = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var text = input.Trim();
        if (!string.Equals(text, "/search", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("/search ", StringComparison.OrdinalIgnoreCase))
            return false;

        query = text.Length > 7 ? text.Substring(7).Trim() : string.Empty;
        return true;
    }

    private static List<int> BuildSearchBrowseCandidateOrder(IReadOnlyList<FreeSearchClient.SearchResult> results)
    {
        var nonWiki = new List<int>();
        var wiki = new List<int>();

        for (var i = 0; i < results.Count; i++)
        {
            var url = results[i]?.Url ?? string.Empty;
            if (IsWikipediaUrl(url))
                wiki.Add(i);
            else
                nonWiki.Add(i);
        }

        nonWiki.AddRange(wiki);
        return nonWiki;
    }

    private static bool IsWikipediaUrl(string url)
    {
        if (!Uri.TryCreate(url ?? string.Empty, UriKind.Absolute, out var uri))
            return false;

        var host = (uri.Host ?? string.Empty).Trim().ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal))
            host = host.Substring(4);

        return host.Equals("wikipedia.org", StringComparison.Ordinal)
               || host.EndsWith(".wikipedia.org", StringComparison.Ordinal);
    }

    public ObservableCollection<ChatMessage> Messages { get; }

    public bool IsSending => _isSending;

    public string InputText
    {
        get => _inputText;
        set
        {
            if (_inputText == value) return;
            _inputText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSend));
        }
    }

    public bool CanSend => !_isSending && !string.IsNullOrWhiteSpace(InputText);

    private async void OnSendClicked(object sender, EventArgs e)
    {
        var prompt = InputText?.Trim();
        if (string.IsNullOrWhiteSpace(prompt) || _isSending) return;

        _isSending = true;
        OnPropertyChanged(nameof(IsSending));
        OnPropertyChanged(nameof(CanSend));

        InputText = string.Empty;

        var user = new ChatMessage("user", prompt);
        var assistant = new ChatMessage("assistant", string.Empty, isEphemeral: true);
        Messages.Add(user);
        Messages.Add(assistant);

        _cts.Cancel();
        _cts = new CancellationTokenSource();

        try
        {
            if (TryHandleThinkingCommand(prompt, assistant))
                return;

            if (TryHandleRetryCommand(prompt, assistant))
                return;

            if (await TryHandleMemoryCommandAsync(prompt, assistant, _cts.Token))
                return;

            if (await TryHandleNaturalMemorySaveIntentAsync(prompt, assistant, _cts.Token))
                return;

            if (await TryHandleIdentityQuestionAsync(prompt, assistant, _cts.Token))
            {
                Debug.WriteLine("[Routing] branch=identity-question");
                return;
            }

            if (!await EnsureApiKeyAsync(CurrentProvider))
            {
                assistant.Content = "Error: Missing API key";
                return;
            }

            var sessionId = await GetOrCreateActiveSessionIdAsync(_cts.Token);
            var persona = _personaProfileStore.LoadPersona();
            var profile = _personaProfileStore.LoadProfile();
            var provider = CurrentProvider;
            var model = string.IsNullOrWhiteSpace(CurrentModel)
                ? (provider == ChatApiClient.Provider.NvidiaIntegrate ? "z-ai/glm5"
                    : provider == ChatApiClient.Provider.Inception ? "mercury-2"
                    : "gpt-oss-120b")
                : CurrentModel.Trim();

            if (prompt.StartsWith("/remember", StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine("[Routing] branch=remember (disabled explicit-only)");
                assistant.Content = "Command /remember dinonaktifkan untuk menjaga mode memory explicit-only. Gunakan /memory add|update|delete atau perintah eksplisit seperti 'ingat ini ...'.";
                return;
            }

            if (TryParseSearchCommand(prompt, out var searchQuery))
            {
                Debug.WriteLine("[Routing] branch=search");
                if (!EnableWebTools)
                {
                    assistant.Content = "Web tools are disabled. Enable them in Settings.";
                    return;
                }

                if (EnableOfflineFallback && IsOffline)
                {
                    assistant.Content = "Offline: /search needs internet access.";
                    return;
                }

                if (string.IsNullOrWhiteSpace(searchQuery))
                {
                    assistant.Content = "Usage: /search <query>";
                    return;
                }

                Debug.WriteLine($"[Routing] /search command accepted. query='{searchQuery}'");

                assistant.Content = "Searching...";

                List<FreeSearchClient.SearchResult> results;
                try
                {
                    results = await _search.SearchAsync(searchQuery, maxResults: 5, _cts.Token);
                }
                catch (Exception ex)
                {
                    var more = ex.InnerException?.Message;
                    assistant.Content = "Search failed: " + ex.Message + (string.IsNullOrWhiteSpace(more) ? string.Empty : "\n" + more) + "\n\nTip: coba /browse <url> langsung.";
                    return;
                }
                if (results.Count == 0)
                {
                    assistant.Content = "No results from search provider.";
                    return;
                }

                // Combine search + browse: prefer non-wiki pages first and fetch up to three successful pages.
                var pages = new List<(int Index, BrowseClient.BrowsePage Page)>();
                var browseCandidates = BuildSearchBrowseCandidateOrder(results);
                foreach (var idx in browseCandidates)
                {
                    if (pages.Count >= 3)
                        break;

                    try
                    {
                        var p = await _browse.FetchAsync(results[idx].Url, _cts.Token);
                        pages.Add((idx + 1, p));
                    }
                    catch
                    {
                        // Keep trying other candidates if one source fails to browse.
                    }
                }

                var sourcesBlock = new StringBuilder();
                var sourceOrder = BuildSearchBrowseCandidateOrder(results);
                foreach (var idx in sourceOrder.Take(4))
                {
                    var r = results[idx];
                    sourcesBlock.Append('[').Append(idx + 1).Append("] ");
                    sourcesBlock.AppendLine(r.Title);
                    sourcesBlock.AppendLine(r.Url);
                    if (!string.IsNullOrWhiteSpace(r.Snippet)) sourcesBlock.AppendLine(r.Snippet);
                    if (!string.IsNullOrWhiteSpace(r.Source)) sourcesBlock.AppendLine("Source: " + r.Source);
                    sourcesBlock.AppendLine();
                }
                if (results.Count > 4)
                    sourcesBlock.AppendLine($"(Sumber tambahan disingkat: {results.Count - 4} item tidak ditampilkan)");

                var pagesBlock = new StringBuilder();
                foreach (var (idx, page) in pages)
                {
                    pagesBlock.Append('[').Append(idx).Append("] ");
                    pagesBlock.AppendLine(page.Url);
                    if (!string.IsNullOrWhiteSpace(page.Title)) pagesBlock.AppendLine(page.Title);
                    pagesBlock.AppendLine();
                    var excerpt = page.Text;
                    if (excerpt.Length > 2200)
                        excerpt = excerpt.Substring(0, 2200) + "\n\n[...excerpt dipotong untuk grounding ringkas...]";
                    pagesBlock.AppendLine(excerpt);
                    pagesBlock.AppendLine();
                }

                var searchModel = model;
                var searchThinking = GetThinkingInstruction();
                var searchFormat = GetStrictSearchTemplateInstruction();
                var searchSnapshot = await BuildSessionContextSnapshotForSearchAsync(sessionId, _cts.Token);
                var searchGroundingRules =
                    "Grounding rules (search mode):\n" +
                    "- Jawab hanya dari sumber yang diberikan di bawah (search results + browsed excerpts).\n" +
                    "- Jangan pakai pengetahuan umum/model jika tidak didukung sumber.\n" +
                    "- Jawaban akhir WAJIB Bahasa Indonesia. Dilarang menjawab dalam bahasa Inggris.\n" +
                    "- Bedakan [FAKTA] (bersitasi) vs [INFERENSI] (dugaan terbatas).\n" +
                    "- Jika data tidak cukup/konflik, tulis jelas keterbatasannya dan minta query lanjutan atau /browse URL.\n" +
                    "- Jika sumber didominasi wiki sementara sumber non-wiki tipis/gagal di-browse, tulis keterbatasan data secara eksplisit di [FAKTA].\n" +
                    "- Voice wajib konsisten gaya ChatAyi: pakai gua/lu, jangan campur kamu/Anda/gaya netral.\n" +
                    "- Hindari gaya ensiklopedik, akademik, atau terlalu formal seperti artikel.\n\n" +
                    "[STRICT OUTPUT RULES - IDENTITY SAFETY]\n" +
                    "- Kamu adalah ChatAyi, bukan subjek yang dibahas.\n" +
                    "- Dilarang menjawab seolah-olah kamu adalah orang yang sedang dibahas.\n" +
                    "- Semua jawaban HARUS menggunakan sudut pandang pihak ketiga.\n" +
                    "- BENAR: 'Windah Basudara adalah...'\n" +
                    "- SALAH: 'Gua adalah...', 'Saya adalah...'\n" +
                    "- Jika sumber menggunakan gaya orang pertama (gue/saya/aku), WAJIB dikonversi menjadi pihak ketiga.\n" +
                    "- Pelanggaran aturan ini dianggap jawaban salah.";
                var searchSafety = BuildSafetyAndBoundariesInstruction(
                    "Gunakan hanya bukti dari sumber web yang diberikan. Wajib Bahasa Indonesia. Setiap klaim faktual wajib sitasi [1], [2], dst. Jangan roleplay sebagai subjek pembahasan. Voice wajib gua/lu konsisten.",
                    searchThinking,
                    searchFormat,
                    GetUnifiedVoiceInstruction(),
                    searchGroundingRules,
                    "Search results (SearXNG primary + Jina booster + fallback providers):\n\n" + sourcesBlock.ToString().Trim(),
                    pagesBlock.Length > 0 ? "Browsed page excerpts:\n\n" + pagesBlock.ToString().Trim() : null);

                var searchRequestMessages = _promptContextAssembler.Build(new PromptContextAssembler.BuildInput(
                    searchSafety,
                    persona,
                    profile,
                    null,
                    searchSnapshot,
                    searchQuery));
                searchRequestMessages = ApplyDcp(searchRequestMessages, provider);

                await AppendSessionRecordAsync(sessionId, "user", "/search " + searchQuery, searchModel, searchQuery, _cts.Token);

                assistant.Content = string.Empty;

                var searchPending = new StringBuilder();
                var searchCaptured = new StringBuilder();
                var searchSw = Stopwatch.StartNew();
                var searchLastFlushMs = 0L;
                const int SearchFlushIntervalMs = 140;

                await foreach (var delta in _api
                                   .StreamChatAsync(provider, searchRequestMessages, searchModel, enableThinking: ThinkingEnabled, _cts.Token)
                                   .WithCancellation(_cts.Token)
                                   .ConfigureAwait(false))
                {
                    if (string.IsNullOrEmpty(delta)) continue;

                    searchPending.Append(delta);
                    searchCaptured.Append(delta);
                    var now = searchSw.ElapsedMilliseconds;
                    if (now - searchLastFlushMs < SearchFlushIntervalMs) continue;

                    var chunk = searchPending.ToString();
                    searchPending.Clear();
                    searchLastFlushMs = now;

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        assistant.Content += chunk;
                    });
                }

                if (searchPending.Length > 0)
                {
                    var chunk = searchPending.ToString();
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        assistant.Content += chunk;
                    });
                }

                if (searchCaptured.Length > 0)
                {
                    var strictSearch = EnforceStrictSearchTemplate(searchCaptured.ToString(), results);
                    assistant.Content = strictSearch;
                    await AppendSessionRecordAsync(sessionId, "assistant", strictSearch, searchModel, string.Empty, _cts.Token);
                }

                return;
            }

            if (prompt.StartsWith("/browse", StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine("[Routing] branch=browse");
                if (!EnableWebTools)
                {
                    assistant.Content = "Web tools are disabled. Enable them in Settings.";
                    return;
                }

                if (EnableOfflineFallback && IsOffline)
                {
                    assistant.Content = "Offline: /browse needs internet access.";
                    return;
                }

                if (!TryParseBrowseCommand(prompt, out var url, out var question))
                {
                    assistant.Content = "Usage: /browse <url> [question]";
                    return;
                }

                assistant.Content = "Browsing...";
                BrowseClient.BrowsePage page;
                try
                {
                    page = await _browse.FetchAsync(url, _cts.Token);
                }
                catch (Exception ex)
                {
                    var more = ex.InnerException?.Message;
                    assistant.Content = "Browse failed: " + ex.Message + (string.IsNullOrWhiteSpace(more) ? string.Empty : "\n" + more);
                    return;
                }

                var browseModel = model;
                var q = string.IsNullOrWhiteSpace(question) ? "Ringkas isi halaman ini dalam bahasa Indonesia." : question;
                var browseThinking = GetThinkingInstruction();
                var browseFormat = GetStrictBrowseTemplateInstruction();

                var pageBlock = new StringBuilder();
                pageBlock.AppendLine("[1] " + page.Url);
                if (!string.IsNullOrWhiteSpace(page.Title)) pageBlock.AppendLine(page.Title);
                pageBlock.AppendLine();
                var compactText = page.Text;
                if (compactText.Length > 3500)
                    compactText = compactText.Substring(0, 3500) + "\n\n[...excerpt truncated for concise grounding...]";
                pageBlock.AppendLine(compactText);

                var browseSnapshot = await BuildSessionContextSnapshotForBrowseAsync(sessionId, _cts.Token);
                var browseGroundingRules =
                    "Aturan /browse:\n" +
                    "- Jawaban akhir WAJIB Bahasa Indonesia.\n" +
                    "- Jika halaman sumber berbahasa Inggris, parafrasekan ke Bahasa Indonesia, jangan copy paragraf Inggris mentah.\n" +
                    "- Jika konten halaman tidak cukup/jelas, nyatakan keterbatasannya secara jujur.\n" +
                    "- Tetap pihak ketiga, jangan roleplay sebagai subjek halaman.\n" +
                    "- Voice wajib konsisten gaya ChatAyi: pakai gua/lu, jangan campur kamu/Anda/gaya netral.";
                var browseSafety = BuildSafetyAndBoundariesInstruction(
                    "Gunakan bukti dari halaman web yang diberikan dan cantumkan sitasi [1]. Jawaban harus Bahasa Indonesia, ringkas, tidak seperti dump mentah, dan voice gua/lu konsisten.",
                    browseThinking,
                    browseFormat,
                    GetUnifiedVoiceInstruction(),
                    browseGroundingRules,
                    "Web page excerpt:\n\n" + pageBlock.ToString().Trim());

                var browseRequestMessages = _promptContextAssembler.Build(new PromptContextAssembler.BuildInput(
                    browseSafety,
                    persona,
                    profile,
                    null,
                    browseSnapshot,
                    q));
                browseRequestMessages = ApplyDcp(browseRequestMessages, provider);

                await AppendSessionRecordAsync(sessionId, "user", "/browse " + url + (question.Length > 0 ? " " + question : ""), browseModel, q, _cts.Token);

                assistant.Content = string.Empty;

                var browsePending = new StringBuilder();
                var browseCaptured = new StringBuilder();
                var browseSw = Stopwatch.StartNew();
                var browseLastFlushMs = 0L;
                const int BrowseFlushIntervalMs = 140;

                await foreach (var delta in _api
                                   .StreamChatAsync(provider, browseRequestMessages, browseModel, enableThinking: ThinkingEnabled, _cts.Token)
                                   .WithCancellation(_cts.Token)
                                   .ConfigureAwait(false))
                {
                    if (string.IsNullOrEmpty(delta)) continue;

                    browsePending.Append(delta);
                    browseCaptured.Append(delta);
                    var now = browseSw.ElapsedMilliseconds;
                    if (now - browseLastFlushMs < BrowseFlushIntervalMs) continue;

                    var chunk = browsePending.ToString();
                    browsePending.Clear();
                    browseLastFlushMs = now;

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        assistant.Content += chunk;
                    });
                }

                if (browsePending.Length > 0)
                {
                    var chunk = browsePending.ToString();
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        assistant.Content += chunk;
                    });
                }

                if (browseCaptured.Length > 0)
                {
                    var strictBrowse = EnforceStrictBrowseTemplate(browseCaptured.ToString(), page.Url);
                    assistant.Content = strictBrowse;
                    await AppendSessionRecordAsync(sessionId, "assistant", strictBrowse, browseModel, string.Empty, _cts.Token);
                }

                return;
            }

            IReadOnlyList<PersonalMemoryItem> relevantMemories = Array.Empty<PersonalMemoryItem>();
            Debug.WriteLine("[Routing] branch=normal-chat");
            if (!_isMemoryTemporarilyOff)
            {
                try
                {
                    relevantMemories = await _personalMemoryStore.GetRelevantAsync(prompt, _cts.Token);
                    Debug.WriteLine($"[Memory] session={sessionId} relevant={relevantMemories.Count} prompt='{prompt}'");
                }
                catch
                {
                    relevantMemories = Array.Empty<PersonalMemoryItem>();
                }
            }

            var chatThinking = GetThinkingInstruction();
            var chatFormat = GetResponseFormatInstruction(hasSources: false);
            var sessionSnapshot = await BuildSessionContextSnapshotForNormalChatAsync(sessionId, _cts.Token);
            var chatSafety = BuildSafetyAndBoundariesInstruction(
                "Gunakan memory personal hanya jika relevan. Jika memory bentrok dengan pesan user terbaru, ikuti pesan user terbaru. Voice wajib gua/lu konsisten.",
                chatThinking,
                chatFormat,
                GetUnifiedVoiceInstruction());

            var requestMessages = _promptContextAssembler.Build(new PromptContextAssembler.BuildInput(
                chatSafety,
                persona,
                profile,
                relevantMemories,
                sessionSnapshot,
                prompt));
            Debug.WriteLine($"[Memory] injectedBlock={(relevantMemories.Count > 0 ? "yes" : "no")}");
            requestMessages = ApplyDcp(requestMessages.ToList(), provider);

            await AppendSessionRecordAsync(sessionId, "user", prompt, model, prompt, _cts.Token);

            // Throttle UI updates to avoid ANR/freezes on Android.
            var pending = new StringBuilder();
            var captured = new StringBuilder();
            var sw = Stopwatch.StartNew();
            var lastFlushMs = 0L;
            const int FlushIntervalMs = 140;

            if (EnableOfflineFallback && IsOffline)
            {
                if (QueueWhileOffline)
                {
                    lock (_offlineQueue)
                    {
                        _offlineQueue.Enqueue(new PendingSend(sessionId, prompt, provider, model, ThinkingEnabled, requestMessages, assistant));
                    }

                    assistant.Content = $"> Offline\n\nPesan kamu diantrikan. Queued: {OfflineQueueCount}.\n\nKetik `/retry` saat online atau tunggu otomatis.";
                    assistant.IsEphemeral = true;
                    NotifyOfflineUi();
                    return;
                }

                assistant.Content = "> Offline\n\nTidak bisa mengirim sekarang. Coba lagi saat online.";
                assistant.IsEphemeral = true;
                NotifyOfflineUi();
                return;
            }

            try
            {
                await foreach (var delta in _api
                                   .StreamChatAsync(provider, requestMessages, model, enableThinking: ThinkingEnabled, _cts.Token)
                                   .WithCancellation(_cts.Token)
                                   .ConfigureAwait(false))
                {
                    if (string.IsNullOrEmpty(delta)) continue;

                    pending.Append(delta);
                    captured.Append(delta);
                    var now = sw.ElapsedMilliseconds;
                    if (now - lastFlushMs < FlushIntervalMs) continue;

                    var chunk = pending.ToString();
                    pending.Clear();
                    lastFlushMs = now;

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        assistant.Content += chunk;
                    });
                }
            }
            catch (HttpRequestException ex)
            {
                if (LooksLikeUnauthorized(ex))
                {
                    assistant.Content = "> Unauthorized\n\nAPI key invalid/expired. Please enter it again.";
                    assistant.IsEphemeral = true;

                    // Prompt user to re-enter key; don't queue because it will keep failing.
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        await EnsureApiKeyAsync(provider);
                    });

                    return;
                }

                if (LooksLikePermanentClientError(ex))
                {
                    assistant.Content = "> Request failed\n\n" + ex.Message.Trim() +
                                        "\n\nTidak diantrikan karena ini bukan error jaringan sementara. " +
                                        "Coba ganti model di Settings (contoh: `meta/llama-3.1-8b-instruct`).";
                    assistant.IsEphemeral = true;
                    NotifyOfflineUi();
                    return;
                }

                if (EnableOfflineFallback && QueueWhileOffline)
                {
                    lock (_offlineQueue)
                    {
                        _offlineQueue.Enqueue(new PendingSend(sessionId, prompt, provider, model, ThinkingEnabled, requestMessages, assistant));
                    }

                    assistant.Content = $"> Network error\n\n{ex.Message}\n\nPesan diantrikan. Queued: {OfflineQueueCount}.";
                    assistant.IsEphemeral = true;
                    NotifyOfflineUi();
                    return;
                }

                throw;
            }

            if (pending.Length > 0)
            {
                var chunk = pending.ToString();
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    assistant.Content += chunk;
                });
            }

            if (captured.Length > 0)
            {
                await AppendSessionRecordAsync(sessionId, "assistant", captured.ToString(), model, string.Empty, _cts.Token);
            }

            assistant.IsEphemeral = false;
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellations (new message/back navigation).
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                assistant.Content = $"Error: {ex.Message}";
            });
        }
        finally
        {
            _isSending = false;
            OnPropertyChanged(nameof(IsSending));
            OnPropertyChanged(nameof(CanSend));
        }
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        _cts.Cancel();
        await Shell.Current.GoToAsync("..");
    }

    protected override void OnDisappearing()
    {
        _cts.Cancel();

        if (_connectivitySubscribed)
        {
            Connectivity.ConnectivityChanged -= OnConnectivityChanged;
            _connectivitySubscribed = false;
        }

        base.OnDisappearing();
    }
}
