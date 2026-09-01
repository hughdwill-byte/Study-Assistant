using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StudyHud.Core.Models;
using StudyHud.Core.Services;

namespace StudyHud.App;

/// <summary>
/// Settings and management window (spec §3.2).
/// Closing this window does NOT exit the application — the HUD stays running (spec §105).
/// </summary>
public partial class MainWindow : Window
{
    private readonly IApplicationStateService _appState;
    private readonly IMonitorService _monitors;
    private readonly IAssessmentPolicyService _policy;
    private readonly ILogger<MainWindow> _logger;
    private readonly IServiceProvider _services;

    public MainWindow(
        IApplicationStateService appState,
        IMonitorService monitors,
        IAssessmentPolicyService policy,
        ILogger<MainWindow> logger,
        IServiceProvider services)
    {
        _appState = appState;
        _monitors = monitors;
        _policy = policy;
        _logger = logger;
        _services = services;

        InitializeComponent();
        SubscribeToState();
    }

    private void SubscribeToState()
    {
        _appState.StateChanged += (_, e) =>
        {
            Dispatcher.BeginInvoke(() => UpdateStatusBar(e.Current));
        };
    }

    private void UpdateStatusBar(ApplicationState state)
    {
        // Update status bar elements based on current state
        if (AssessmentIndicator != null)
        {
            AssessmentIndicator.Visibility = state.AssessmentModeActive
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (WorkspaceLabel != null)
            WorkspaceLabel.Text = state.CurrentWorkspace.ToString();

        if (CourseLabel != null)
            CourseLabel.Text = state.CurrentCourseId ?? "No course selected";
    }

    // Closing the window hides it rather than exiting (spec §105)
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
        _logger.LogDebug("Settings window hidden (HUD still running).");
    }

    // Navigation
    private SettingsView? _settingsView;

    private void OnShowSettings(object sender, RoutedEventArgs e)
    {
        _settingsView ??= _services.GetRequiredService<SettingsView>();
        PageHost.Content = _settingsView;
    }

    private void OnShowHome(object sender, RoutedEventArgs e)
    {
        PageHost.Content = HomeView;
    }

    // Commands
    private async void OnSwitchWorkspaceNoteTaking(object sender, RoutedEventArgs e)
        => await _appState.SwitchWorkspaceAsync(WorkspaceId.NoteTaking);

    private async void OnSwitchWorkspaceQuestionFinder(object sender, RoutedEventArgs e)
        => await _appState.SwitchWorkspaceAsync(WorkspaceId.QuestionFinder);

    private async void OnToggleAssessmentMode(object sender, RoutedEventArgs e)
        => await _appState.SetAssessmentModeAsync(!_policy.IsAssessmentModeActive);

    private void OnToggleHudVisibility(object sender, RoutedEventArgs e)
        => _appState.SetHudVisible(!_appState.Current.HudVisible);

    // XAML x:Name attributes generate the field declarations automatically.
    // No manual field declarations needed for AssessmentIndicator, WorkspaceLabel, CourseLabel.
}
