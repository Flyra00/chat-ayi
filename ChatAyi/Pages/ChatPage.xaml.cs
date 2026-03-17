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
                      new DdgSearchClient(new HttpClient { Timeout = TimeSpan.FromSeconds(15) }),
                      new HttpClient { Timeout = TimeSpan.FromSeconds(20) });
        _browse = services?.GetService<BrowseClient>()
                  ?? new BrowseClient(new HttpClient { Timeout = TimeSpan.FromSeconds(25) });
        _memory = services?.GetService<LocalMemoryStore>() ?? new LocalMemoryStore();
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
            sb.AppendLine("- Jika memakai sumber yang diberikan, cantumkan sitasi [1], [2], dst dan akhiri dengan bagian 'Sumber'.");
        sb.AppendLine("- Jangan mengarang repo/tautan/fakta. Jika tidak yakin, tulis 'Saya belum yakin' dan minta link / gunakan /browse.");
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
                if (!EnableRemember)
                {
                    assistant.Content = "Remember is disabled. Enable it in Settings.";
                    return;
                }

                assistant.Content = "Saving memory...";

                // Syntax:
                // - /remember
                // - /remember daily <note>
                // - /remember longterm <note>
                // - /remember both <note>
                var rest = prompt.Length > 8 ? prompt.Substring(8).Trim() : string.Empty;
                var target = "both";
                var note = string.Empty;

                if (!string.IsNullOrWhiteSpace(rest))
                {
                    var parts = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                    var maybeTarget = parts[0].Trim().ToLowerInvariant();
                    if (maybeTarget is "daily" or "longterm" or "long" or "memory" or "both" or "all" or "*")
                    {
                        target = maybeTarget == "long" ? "longterm" : maybeTarget;
                        note = parts.Length > 1 ? parts[1].Trim() : string.Empty;
                    }
                    else
                    {
                        note = rest;
                    }
                }

                var recentLines = await _sessions.ReadRecentChatAsync(sessionId, 30, _cts.Token);
                if (recentLines.Count == 0)
                {
                    assistant.Content = "Error: No session transcript found yet";
                    return;
                }

                var transcript = new StringBuilder();
                foreach (var (role, content) in recentLines)
                {
                    transcript.Append(role.ToUpperInvariant());
                    transcript.Append(": ");
                    transcript.AppendLine(content.Replace("\r\n", "\n").Replace("\r", "\n"));
                    transcript.AppendLine();
                }

                var memoryResult = await _api.ExtractMemoryAsync(provider, transcript.ToString(), note, model, _cts.Token);
                var longterm = memoryResult.Longterm;
                var daily = memoryResult.Daily;

                static bool LooksLikeSecret(string s)
                {
                    var t = (s ?? string.Empty).ToLowerInvariant();
                    if (t.Contains("api key") || t.Contains("apikey") || t.Contains("token") || t.Contains("password")) return true;
                    if (t.Contains("csk-") || t.Contains("sk-") || t.Contains("bearer ")) return true;
                    return false;
                }

                longterm = longterm.Where(x => !LooksLikeSecret(x)).Take(8).ToList();
                daily = daily.Where(x => !LooksLikeSecret(x)).Take(8).ToList();

                var wroteLong = 0;
                var wroteDaily = 0;
                if (target is "both" or "all" or "*" or "longterm" or "long" or "memory")
                {
                    if (target is "longterm" or "long" or "memory")
                        daily.Clear();
                    await _memory.AppendLongTermManyAsync(longterm, _cts.Token);
                    wroteLong = longterm.Count;
                }
                if (target is "both" or "all" or "*" or "daily")
                {
                    if (target is "daily")
                        longterm.Clear();
                    await _memory.AppendDailyManyAsync(daily, date: null, _cts.Token);
                    wroteDaily = daily.Count;
                }

                await AppendSessionRecordAsync(
                    sessionId,
                    "system",
                    $"/remember wrote longterm={wroteLong} daily={wroteDaily}",
                    model,
                    string.Empty,
                    _cts.Token);

                assistant.Content = $"Remember OK\nlongterm: {wroteLong}\ndaily: {wroteDaily}";

                return;
            }

            if (prompt.StartsWith("/search", StringComparison.OrdinalIgnoreCase))
            {
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

                var q = prompt.Length > 7 ? prompt.Substring(7).Trim() : string.Empty;
                if (string.IsNullOrWhiteSpace(q))
                {
                    assistant.Content = "Usage: /search <query>";
                    return;
                }

                assistant.Content = "Searching...";

                List<FreeSearchClient.SearchResult> results;
                try
                {
                    results = await _search.SearchAsync(q, maxResults: 6, _cts.Token);
                }
                catch (Exception ex)
                {
                    var more = ex.InnerException?.Message;
                    assistant.Content = "Search failed: " + ex.Message + (string.IsNullOrWhiteSpace(more) ? string.Empty : "\n" + more) + "\n\nTip: coba /browse <url> langsung.";
                    return;
                }
                if (results.Count == 0)
                {
                    assistant.Content = "No results (DuckDuckGo Instant Answer returned empty).";
                    return;
                }

                // Combine search + browse: fetch the first couple of pages.
                var pages = new List<(int Index, BrowseClient.BrowsePage Page)>();
                for (var i = 0; i < Math.Min(2, results.Count); i++)
                {
                    try
                    {
                        var p = await _browse.FetchAsync(results[i].Url, _cts.Token);
                        pages.Add((i + 1, p));
                    }
                    catch
                    {
                        // Ignore browse failures; we still have snippets.
                    }
                }

                var sourcesBlock = new StringBuilder();
                for (var i = 0; i < results.Count; i++)
                {
                    var r = results[i];
                    sourcesBlock.Append('[').Append(i + 1).Append("] ");
                    sourcesBlock.AppendLine(r.Title);
                    sourcesBlock.AppendLine(r.Url);
                    if (!string.IsNullOrWhiteSpace(r.Snippet)) sourcesBlock.AppendLine(r.Snippet);
                    if (!string.IsNullOrWhiteSpace(r.Source)) sourcesBlock.AppendLine("Source: " + r.Source);
                    sourcesBlock.AppendLine();
                }

                var pagesBlock = new StringBuilder();
                foreach (var (idx, page) in pages)
                {
                    pagesBlock.Append('[').Append(idx).Append("] ");
                    pagesBlock.AppendLine(page.Url);
                    if (!string.IsNullOrWhiteSpace(page.Title)) pagesBlock.AppendLine(page.Title);
                    pagesBlock.AppendLine();
                    pagesBlock.AppendLine(page.Text);
                    pagesBlock.AppendLine();
                }

                var searchModel = model;
                var searchMemoryContext = await _memory.GetContextAsync(q, _cts.Token);
                var searchThinking = GetThinkingInstruction();
                var searchFormat = GetResponseFormatInstruction(hasSources: true);
                var searchSnapshot = await BuildSessionContextSnapshotAsync(sessionId, _cts.Token);
                var searchSafety = BuildSafetyAndBoundariesInstruction(
                    "Use provided web search evidence when relevant and cite sources like [1], [2].",
                    searchThinking,
                    searchFormat,
                    string.IsNullOrWhiteSpace(searchMemoryContext) ? null : "Local memory (if relevant):\n\n" + searchMemoryContext,
                    "Search results (DuckDuckGo Instant Answer):\n\n" + sourcesBlock.ToString().Trim(),
                    pagesBlock.Length > 0 ? "Browsed page excerpts:\n\n" + pagesBlock.ToString().Trim() : null);

                var searchRequestMessages = _promptContextAssembler.Build(new PromptContextAssembler.BuildInput(
                    searchSafety,
                    persona,
                    profile,
                    searchSnapshot,
                    q));
                searchRequestMessages = ApplyDcp(searchRequestMessages, provider);

                await AppendSessionRecordAsync(sessionId, "user", "/search " + q, searchModel, q, _cts.Token);

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
                    await AppendSessionRecordAsync(sessionId, "assistant", searchCaptured.ToString(), searchModel, string.Empty, _cts.Token);
                }

                return;
            }

            if (prompt.StartsWith("/browse", StringComparison.OrdinalIgnoreCase))
            {
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
                var q = string.IsNullOrWhiteSpace(question) ? "Summarize this page." : question;
                var browseMemoryContext = await _memory.GetContextAsync(q, _cts.Token);
                var browseThinking = GetThinkingInstruction();
                var browseFormat = GetResponseFormatInstruction(hasSources: true);

                var pageBlock = new StringBuilder();
                pageBlock.AppendLine("[1] " + page.Url);
                if (!string.IsNullOrWhiteSpace(page.Title)) pageBlock.AppendLine(page.Title);
                pageBlock.AppendLine();
                pageBlock.AppendLine(page.Text);

                var browseSnapshot = await BuildSessionContextSnapshotAsync(sessionId, _cts.Token);
                var browseSafety = BuildSafetyAndBoundariesInstruction(
                    "Use provided web page evidence when relevant and cite sources like [1].",
                    browseThinking,
                    browseFormat,
                    string.IsNullOrWhiteSpace(browseMemoryContext) ? null : "Local memory (if relevant):\n\n" + browseMemoryContext,
                    "Web page excerpt:\n\n" + pageBlock.ToString().Trim());

                var browseRequestMessages = _promptContextAssembler.Build(new PromptContextAssembler.BuildInput(
                    browseSafety,
                    persona,
                    profile,
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
                    await AppendSessionRecordAsync(sessionId, "assistant", browseCaptured.ToString(), browseModel, string.Empty, _cts.Token);
                }

                return;
            }

            var memoryContext = await _memory.GetContextAsync(prompt, _cts.Token);
            var chatThinking = GetThinkingInstruction();
            var chatFormat = GetResponseFormatInstruction(hasSources: false);
            memoryContext = TrimForContext(memoryContext, provider == ChatApiClient.Provider.NvidiaIntegrate ? 1800 : 3500);
            var sessionSnapshot = await BuildSessionContextSnapshotAsync(sessionId, _cts.Token);
            var chatSafety = BuildSafetyAndBoundariesInstruction(
                "Use local memory excerpts only when relevant. If irrelevant, ignore them.",
                chatThinking,
                chatFormat,
                string.IsNullOrWhiteSpace(memoryContext) ? null : "Local memory (if relevant):\n\n" + memoryContext);

            var requestMessages = _promptContextAssembler.Build(new PromptContextAssembler.BuildInput(
                chatSafety,
                persona,
                profile,
                sessionSnapshot,
                prompt));
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
