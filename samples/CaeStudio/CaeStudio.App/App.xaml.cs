using CaeStudio.App.Services;
using CaeStudio.App.ViewModels;
using CaeStudio.App.Views;
using CaeStudio.Application;
using CaeStudio.Domain.Models;
using CaeStudio.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using R3;
using System.Diagnostics;
using System.Windows;
using WpfCustomUI.Controls.Theming;

namespace CaeStudio.App;

/// <summary>
/// コンポジションルート: Generic Host で全レイヤーのサービスを構成し、
/// R3 の WPF プロバイダ(ディスパッチャ連携)を初期化する(spec 6.26.3)。
/// </summary>
public partial class App : System.Windows.Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // R3: 未処理例外の記録+WPF ディスパッチャベースの既定 TimeProvider/FrameProvider
        WpfProviderInitializer.SetDefaultObservableSystem(
            exception => Trace.WriteLine($"[R3] 未処理例外: {exception}"));

        var builder = Host.CreateApplicationBuilder();

        // Application 層
        builder.Services.AddSingleton(new ProjectStore(ProjectTemplates.CreatePlateWithHole()));
        builder.Services.AddSingleton<AnalysisRunner>();

        // Infrastructure 層
        builder.Services.AddSingleton<IProjectRepository, JsonProjectRepository>();
        builder.Services.AddSingleton<ISettingsService, JsonSettingsService>();
        builder.Services.AddSingleton<IJobClient, SimulatedHpcClient>();

        // Presentation 層
        builder.Services.AddSingleton<IDialogService, DialogService>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        _host = builder.Build();
        _host.Start();

        // 保存済みテーマを最初のウィンドウ生成前に適用
        var settings = _host.Services.GetRequiredService<ISettingsService>();
        ThemeManager.SetTheme(settings.Current.Theme == "Light"
            ? WcuThemeVariant.Light : WcuThemeVariant.Dark);

        MainWindow = _host.Services.GetRequiredService<MainWindow>();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }
}
