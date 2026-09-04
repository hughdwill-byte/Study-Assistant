using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using StudyHud.Capture;
using StudyHud.Core.Services;
using StudyHud.Macros;
using StudyHud.Macros.Models;
using StudyHud.Macros.Services;
using StudyHud.Notion;
using StudyHud.Ocr;
using StudyHud.Overlay;
using StudyHud.Search;
using StudyHud.Storage;
using StudyHud.Theming;
using StudyHud.Windows.Services;

namespace StudyHud.App;

/// <summary>
/// WPF application entry point (spec §103, §149).
/// Startup sequence: config → logging → DI → monitor service → HUD (no sync block).
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Step 1: Configure Serilog before anything else (spec §70)
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StudyHud", "Logs", "studyhud-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Debug()
            .WriteTo.File(logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("Study HUD starting. Version={Version}", GetType().Assembly.GetName().Version);

        try
        {
            _host = BuildHost();
            await _host.StartAsync();

            // Step 2: Initialise monitor service
            var monitors = _host.Services.GetRequiredService<IMonitorService>();
            await monitors.InitialiseAsync();

            // Step 3: Initialise database
            var dbPath = GetDatabasePath();
            var migrator = _host.Services.GetRequiredService<DatabaseMigrator>();
            await migrator.MigrateAsync();

            // Step 4: Load persisted settings and apply them to initial state (spec §19, §71)
            var settingsStore = _host.Services.GetRequiredService<ISettingsStore>();
            var settings = await settingsStore.LoadAsync();

            // Step 4a: Apply theme (and any custom accent) from settings
            var theme = _host.Services.GetRequiredService<IThemeService>();
            theme.ApplyTheme(settings.ThemeId);
            ApplyAccentFromSettings(theme, settings.AccentColour);

            // Step 4b: Restore session context BEFORE overlays are built so panels populate
            // for the correct workspace and Assessment Mode is enforced from the first frame.
            var appState = _host.Services.GetRequiredService<IApplicationStateService>();
            // Force the policy singleton to construct now so it subscribes to state changes
            // before we set Assessment Mode — the policy mirrors ApplicationState (spec §41, §182).
            _ = _host.Services.GetRequiredService<AssessmentPolicyService>();
            await appState.SetAssessmentModeAsync(settings.AssessmentModeActive);
            if (!string.IsNullOrWhiteSpace(settings.CurrentCourseId))
                await appState.SetCourseAsync(settings.CurrentCourseId!);
            await appState.SwitchWorkspaceAsync(settings.CurrentWorkspace);

            // Step 5: Start foreground tracking
            var foreground = _host.Services.GetRequiredService<IForegroundWindowService>();
            await foreground.StartAsync();

            // Step 6: Create overlay windows (must be on WPF UI thread)
            var overlayManager = _host.Services.GetRequiredService<OverlayManager>();
            overlayManager.Initialise();

            // Step 6b: Start global input + Hold-to-Interact (with the user's configured trigger)
            var globalInput = _host.Services.GetRequiredService<GlobalInputService>();
            await globalInput.StartAsync();
            var holdToInteract = _host.Services.GetRequiredService<HoldToInteractService>();
            holdToInteract.ApplySettings(settings);
            holdToInteract.Start();

            // Step 7: Start macro engine, then load user macros and route global input to it.
            var macroEngine = _host.Services.GetRequiredService<MacroEngine>();
            macroEngine.Start();

            var macroManager = _host.Services.GetRequiredService<MacroManager>();
            macroManager.Attach();
            macroManager.LoadAndApply();

            // Step 7b: Start workspace coordination (restores saved layout + macro profile)
            var coordinator = _host.Services.GetRequiredService<WorkspaceCoordinator>();
            coordinator.Start();

            // Step 8: Show settings window (first run: onboarding)
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();

            Log.Information("Study HUD started successfully. HUD is active and in Ghost mode.");

            // Step 9: Start allowed background Notion sync AFTER HUD is shown (spec §103, §149)
            _ = Task.Run(() => StartBackgroundSyncAsync(_host.Services));
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Study HUD failed to start.");
            MessageBox.Show(
                $"Study HUD failed to start:\n\n{ex.Message}\n\nSee logs for details.",
                "Study HUD — Startup Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private IHost BuildHost()
    {
        var dbPath = GetDatabasePath();

        return Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices((ctx, services) =>
            {
                // ── Core services ────────────────────────────────────────────
                services.AddSingleton<IApplicationStateService, ApplicationStateService>();
                services.AddSingleton<AssessmentPolicyService>();
                services.AddSingleton<IAssessmentPolicyService>(sp =>
                    sp.GetRequiredService<AssessmentPolicyService>());

                // ── Windows-native services ──────────────────────────────────
                services.AddSingleton<IMonitorService, MonitorService>();
                services.AddSingleton<GlobalInputService>();
                services.AddSingleton<IGlobalInputService>(sp => sp.GetRequiredService<GlobalInputService>());
                services.AddSingleton<HoldToInteractService>();
                services.AddSingleton<IForegroundWindowService>(sp =>
                    new ForegroundWindowService(
                        sp.GetRequiredService<ILogger<ForegroundWindowService>>(),
                        sp.GetRequiredService<IAssessmentPolicyService>()));
                services.AddSingleton<ForegroundWindowService>(sp =>
                    (ForegroundWindowService)sp.GetRequiredService<IForegroundWindowService>());

                // ── Overlay ──────────────────────────────────────────────────
                services.AddSingleton<OverlayManager>(sp => new OverlayManager(
                    sp.GetRequiredService<IMonitorService>(),
                    sp.GetRequiredService<IApplicationStateService>(),
                    sp.GetRequiredService<IThemeService>(),
                    sp.GetRequiredService<ICaptureService>(),
                    sp.GetRequiredService<IQuestionFinder>(),
                    sp.GetRequiredService<IAssessmentPolicyService>(),
                    sp.GetRequiredService<ILogger<OverlayManager>>()));

                // ── Capture ──────────────────────────────────────────────────
                services.AddSingleton<ICaptureService, CaptureService>();

                // ── Macro engine ─────────────────────────────────────────────
                services.AddSingleton<MacroEngine>();
                services.AddSingleton<StudyHud.Macros.MacroStore>();
                services.AddSingleton<MacroManager>();

                // ── OCR ──────────────────────────────────────────────────────
                services.AddSingleton<IOcrService, WindowsOcrService>();

                // ── Storage / database ───────────────────────────────────────
                services.AddSingleton(_ => new DatabaseMigrator(
                    dbPath,
                    services.BuildServiceProvider()
                        .GetRequiredService<ILogger<DatabaseMigrator>>()));

                // ── Search ───────────────────────────────────────────────────
                services.AddSingleton<ISearchIndex>(_ =>
                    new LocalSearchIndex(dbPath,
                        services.BuildServiceProvider()
                            .GetRequiredService<ILogger<LocalSearchIndex>>()));

                // ── Courses (library + Notion root-page mapping, spec §43, §60) ─
                services.AddSingleton<ICourseRepository>(sp => new CourseRepository(
                    dbPath, sp.GetRequiredService<ILogger<CourseRepository>>()));

                // ── Indexing pipeline (OCR → normalise → index, spec §50) ────
                services.AddSingleton<INoteIndexer, NoteIndexer>();

                // ── Question Finder runtime (capture → OCR → search, spec §38) ─
                services.AddSingleton<IQuestionFinder, QuestionFinder>();

                // ── Settings + layout persistence (spec §19, §71) ────────────
                services.AddSingleton<ISettingsStore>(sp =>
                    new JsonSettingsStore(
                        Path.Combine(GetAppDataDir(), "settings.json"),
                        sp.GetRequiredService<ILogger<JsonSettingsStore>>()));
                services.AddSingleton<ILayoutService>(sp =>
                    new LayoutService(
                        Path.Combine(GetAppDataDir(), "layouts"),
                        sp.GetRequiredService<ILogger<LayoutService>>()));

                // ── Coordination (spec §22, §29, §134) ───────────────────────
                services.AddSingleton<IMacroProfileSwitcher>(sp => sp.GetRequiredService<MacroEngine>());
                services.AddSingleton<WorkspaceCoordinator>();

                // ── Notion ───────────────────────────────────────────────────
                services.AddSingleton<ICredentialStore>(sp => new DpapiCredentialStore(
                    Path.Combine(GetAppDataDir(), "creds"),
                    sp.GetRequiredService<ILogger<DpapiCredentialStore>>()));
                services.AddSingleton<INoteSource, NotionConnector>();

                // ── Theming ──────────────────────────────────────────────────
                services.AddSingleton<IThemeService, ThemeService>();

                // ── Windows ──────────────────────────────────────────────────
                services.AddTransient<MainWindow>();
                services.AddTransient<SettingsView>();
                services.AddTransient<LibraryView>();
                services.AddTransient<MacrosView>();
                services.AddTransient<LayoutsView>();
                services.AddTransient<NotesView>();
                services.AddTransient<ThemesView>();
            })
            .Build();
    }

    private static void ApplyAccentFromSettings(IThemeService theme, string? accentHex)
    {
        if (string.IsNullOrWhiteSpace(accentHex)) return;
        try
        {
            var h = accentHex.TrimStart('#');
            if (h.Length != 6) return;
            theme.ApplyAccentColour(System.Drawing.Color.FromArgb(255,
                Convert.ToByte(h.Substring(0, 2), 16),
                Convert.ToByte(h.Substring(2, 2), 16),
                Convert.ToByte(h.Substring(4, 2), 16)));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not apply saved accent colour '{Hex}'.", accentHex);
        }
    }

    private static string GetAppDataDir()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StudyHud");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string GetDatabasePath() => Path.Combine(GetAppDataDir(), "studyhud.db");

    private static async Task StartBackgroundSyncAsync(IServiceProvider services)
    {
        try
        {
            // Background Notion sync — runs after HUD is live (spec §103)
            var policy = services.GetRequiredService<IAssessmentPolicyService>();
            var notion = services.GetRequiredService<INoteSource>();
            var logger = services.GetRequiredService<ILogger<App>>();

            if (!policy.IsOperationAllowed(PolicyOperation.NotionSync))
            {
                logger.LogInformation("Background sync skipped: Assessment Mode active.");
                return;
            }

            logger.LogInformation("Background Notion sync starting.");
            await notion.TestConnectionAsync();
        }
        catch (Exception ex)
        {
            // Background sync failure must never crash the HUD (spec §69)
            Log.Warning(ex, "Background sync encountered an error (non-fatal).");
        }
    }

    /// <summary>
    /// Saves the on-screen layout and the current session context (workspace, course,
    /// assessment mode, theme) so the next launch restores exactly where the user left off
    /// (spec §19, §71). Never throws — shutdown must not be blocked by a save failure.
    /// </summary>
    private static async Task SaveSessionAsync(IServiceProvider services)
    {
        try
        {
            var coordinator = services.GetService<WorkspaceCoordinator>();
            if (coordinator != null)
                await coordinator.SaveCurrentAsync();

            var settingsStore = services.GetService<ISettingsStore>();
            var appState = services.GetService<IApplicationStateService>();
            var theme = services.GetService<IThemeService>();
            if (settingsStore != null && appState != null)
            {
                var state = appState.Current;
                await settingsStore.UpdateAsync(s => s with
                {
                    CurrentWorkspace = state.CurrentWorkspace,
                    CurrentCourseId = state.CurrentCourseId,
                    AssessmentModeActive = state.AssessmentModeActive,
                    ThemeId = theme?.CurrentThemeId ?? s.ThemeId
                });
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to save session on exit (non-fatal).");
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        Log.Information("Study HUD shutting down.");

        if (_host != null)
        {
            try
            {
                // Persist layout + session context before tearing anything down (spec §19)
                await SaveSessionAsync(_host.Services);

                // Graceful shutdown
                var foreground = _host.Services.GetService<IForegroundWindowService>();
                if (foreground != null) await foreground.StopAsync();

                var macros = _host.Services.GetService<MacroEngine>();
                macros?.Stop();

                await _host.StopAsync(TimeSpan.FromSeconds(3));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during shutdown.");
            }
            finally
            {
                _host.Dispose();
            }
        }

        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
