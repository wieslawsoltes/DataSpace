using DataSpace.Controls;
using DataSpace.Core;
using DataSpace.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DataSpace.App;

public sealed partial class App : Application
{
    private Window? _window;
    private DatabaseWorkspaceView? _view;
    private WorkspaceSession? _session;
    public App() => InitializeComponent();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new Window { Title = "DataSpace" };
        var host = new Grid();
        var loading = OfficeVisuals.Text("Loading DataSpace…", 24, "A4373A");
        loading.HorizontalAlignment = HorizontalAlignment.Center; loading.VerticalAlignment = VerticalAlignment.Center;
        host.Children.Add(loading); _window.Content = host; _window.Activate();
        try
        {
            await PlatformServices.InitializeAsync();
            var workspace = new DatabaseWorkspace(SampleDatabase.Create());
            _session = new WorkspaceSession(workspace, PlatformServices.CreateStore());
            string? storageError = null;
            try { await _session.InitializeAsync(); }
            catch (Exception error) { storageError = "Stored data was not overwritten. " + error.Message; }
            _view = new DatabaseWorkspaceView(workspace)
            {
                StorageIsDirty = () => _session.IsDirty,
                SaveDatabaseAsync = async () =>
                {
                    await _session.SaveAsync();
                    PlatformServices.SetDirty(_session.IsDirty);
                },
                ImportTextAsync = PlatformServices.ImportTextAsync,
                ExportFileAsync = PlatformServices.ExportFileAsync,
                OpenSqliteAsync = PlatformServices.OpenSqliteAsync,
                ExportSqliteTableAsync = PlatformServices.ExportSqliteAsync
            };
            workspace.Changed += (_, _) => PlatformServices.SetDirty(_session.IsDirty);
            _view.Loaded += (_, _) => PlatformServices.Ready(workspace.Document.Name, _session.IsInitialized);
            host.Children.Clear(); host.Children.Add(_view);
            if (storageError is not null) _view.ShowError(storageError);
            else _view.ShowStatus("Ready — changes stay local. Ctrl+S saves; export a .dspace file for backup.");
            PlatformServices.SetDirty(_session.IsDirty);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            loading.Text = "DataSpace could not start.\n" + error.Message;
            loading.FontSize = 16; loading.TextWrapping = TextWrapping.Wrap; loading.Margin = new Thickness(32);
            PlatformServices.ReportFailure(error.Message);
        }
    }
}
