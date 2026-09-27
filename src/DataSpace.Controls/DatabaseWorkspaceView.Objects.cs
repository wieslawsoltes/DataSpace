namespace DataSpace.Controls;

public sealed partial class DatabaseWorkspaceView
{
    private async Task ObjectCommandAsync(DatabaseObjectItem item, string command)
    {
        CommitActive();
        if (command == "Delete")
        {
            if (!await ConfirmAsync("Delete " + item.Kind + " '" + item.Name + "'? This operation can be undone.")) return;
            Workspace.Edit("Delete " + item.Kind, document =>
            {
                if (document.Macros.Any(m => m.Steps.Any(s => s.Action == MacroActionKind.OpenObject && (Names.Equal(s.Argument, item.Key) || Names.Equal(s.Argument, item.Name))))) throw new DataSpaceException("A macro references this object. Remove the dependency first.");
                switch (item.Kind)
                {
                    case DatabaseObjectKind.Table: if (document.Queries.Count > 0) throw new DataSpaceException("Review and remove saved SQL dependencies before deleting a table."); document.Tables.RemoveAll(t => Names.Equal(t.Name, item.Name)); break;
                    case DatabaseObjectKind.Query: document.Queries.RemoveAll(q => Names.Equal(q.Name, item.Name)); break;
                    case DatabaseObjectKind.Form: document.Forms.RemoveAll(f => Names.Equal(f.Name, item.Name)); break;
                    case DatabaseObjectKind.Report: document.Reports.RemoveAll(r => Names.Equal(r.Name, item.Name)); break;
                    case DatabaseObjectKind.Macro: document.Macros.RemoveAll(m => Names.Equal(m.Name, item.Name)); break;
                }
            });
            if (_active?.Key == item.Key) { DisposeActive(); _active = null; }
            _documents.RemoveAll(d => d.Key == item.Key); if (_active is null) { if (_documents.LastOrDefault() is { } next) OpenObject(next); else EmptyView(); }
        }
        else if (command == "Rename")
        {
            var name = await PromptAsync("Rename " + item.Kind, "Name", item.Name); if (name is null || name == item.Name) return; Names.Validate(name);
            Workspace.Edit("Rename " + item.Kind, document =>
            {
                switch (item.Kind)
                {
                    case DatabaseObjectKind.Table:
                        if (document.Queries.Count > 0) throw new DataSpaceException("Review and remove saved SQL dependencies before renaming a table.");
                        document.Table(item.Name).Name = name;
                        foreach (var relation in document.Relationships) { if (Names.Equal(relation.ParentTable, item.Name)) relation.ParentTable = name; if (Names.Equal(relation.ChildTable, item.Name)) relation.ChildTable = name; }
                        foreach (var form in document.Forms.Where(f => Names.Equal(f.Source, item.Name))) form.Source = name;
                        foreach (var report in document.Reports.Where(r => Names.Equal(r.Source, item.Name))) report.Source = name;
                        break;
                    case DatabaseObjectKind.Query: document.Queries.First(q => Names.Equal(q.Name, item.Name)).Name = name; foreach (var report in document.Reports.Where(r => Names.Equal(r.Source, item.Name))) report.Source = name; break;
                    case DatabaseObjectKind.Form: document.Forms.First(f => Names.Equal(f.Name, item.Name)).Name = name; break;
                    case DatabaseObjectKind.Report: document.Reports.First(r => Names.Equal(r.Name, item.Name)).Name = name; break;
                    case DatabaseObjectKind.Macro: document.Macros.First(m => Names.Equal(m.Name, item.Name)).Name = name; break;
                }
                foreach (var step in document.Macros.SelectMany(m => m.Steps).Where(s => s.Action == MacroActionKind.OpenObject))
                    if (Names.Equal(step.Argument, item.Key) || Names.Equal(step.Argument, item.Name)) step.Argument = item.Kind + ":" + name;
            });
            var replacement = new DatabaseObjectItem(item.Kind, name);
            var index = _documents.FindIndex(d => d.Key == item.Key); if (index >= 0) _documents[index] = replacement;
            if (_active?.Key == item.Key) { var design = _design; DisposeActive(); _active = null; OpenObject(replacement, design); }
        }
        UpdateChrome();
    }
    private void ShowBackstage()
    {
        var root = OfficeVisuals.Grid("*", "210,*"); var menu = new StackPanel { Spacing = 8, Margin = new(18, 22, 18, 22) }; root.Background = OfficeVisuals.Brush("FFFFFF");
        void Item(string label, string command)
        {
            var button = OfficeVisuals.Button(label, () => { _backstage.Visibility = Visibility.Collapsed; Execute(command); }); button.Foreground = OfficeVisuals.Brush("FFFFFF"); button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Left; button.FontSize = 18; button.Height = 48; menu.Children.Add(button);
        }
        var back = OfficeVisuals.Button("←  Back", () => _backstage.Visibility = Visibility.Collapsed); back.Foreground = OfficeVisuals.Brush("FFFFFF"); back.FontSize = 22; back.Margin = new(0, 0, 0, 18); menu.Children.Add(back);
        Item("New", "newDatabase"); Item("Open", "open"); Item("Save", "save"); Item("Export Database", "exportDatabase"); Item("About", "about");
        OfficeVisuals.Add(root, OfficeVisuals.Border(menu, "A4373A", thickness: new(0)));
        var info = OfficeVisuals.Stack(OfficeVisuals.Text("Database Information", 30, "A4373A"), OfficeVisuals.Text(Workspace.Document.Name, 23), OfficeVisuals.Text($"{Workspace.Document.Tables.Count} tables · {Workspace.Document.Queries.Count} queries · {Workspace.Document.Forms.Count} forms · {Workspace.Document.Reports.Count} reports", 14, "666666"), OfficeVisuals.Text("DataSpace format (.dspace)\nLocal browser or desktop storage. Export a separate file for backup.\nNative Microsoft Access database files are not supported.", 14, "666666"));
        info.Margin = new(48); info.VerticalAlignment = VerticalAlignment.Top; OfficeVisuals.Add(root, info, column: 1); _backstage.Child = root; _backstage.Visibility = Visibility.Visible;
    }
    public async Task<bool> ConfirmAsync(string message)
    {
        var content = OfficeVisuals.Text(message, 14); content.TextWrapping = TextWrapping.Wrap;
        return await new ContentDialog { XamlRoot = XamlRoot, Title = "DataSpace", Content = content, PrimaryButtonText = "Continue", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close }.ShowAsync() == ContentDialogResult.Primary;
    }
    private async Task<string?> PromptAsync(string title, string label, string value)
    {
        var input = OfficeVisuals.Input(value); input.MinWidth = 360; AutomationProperties.SetName(input, label);
        var result = await new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = OfficeVisuals.Stack(OfficeVisuals.Text(label), input), PrimaryButtonText = "OK", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary }.ShowAsync();
        return result == ContentDialogResult.Primary ? input.Text : null;
    }
    private async Task MessageAsync(string title, string message)
    {
        var content = OfficeVisuals.Text(message, 14); content.TextWrapping = TextWrapping.Wrap;
        await new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = content, CloseButtonText = "Close" }.ShowAsync();
    }
}
