using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using InternetPro.Filtering;

namespace InternetPro.Platforms.Shared;

public sealed class MainView : UserControl
{
    private static readonly Uri HomePage = new("https://www.youtube.com");
    private static readonly string OptimizationLevelPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "InternetPro", "tab-optimization-level.txt");
    private readonly TabControl _tabs = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly List<BrowserTab> _browserTabs = [];
    private readonly TextBox _address = new() { PlaceholderText = "Adresa sau cautare", MinWidth = 180 };
    private readonly TextBlock _optimizationLabel = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Slider _optimizationSlider = new()
    {
        Minimum = 0,
        Maximum = 2,
        Value = (int)LoadOptimizationLevel(),
        TickFrequency = 1,
        MinWidth = 110,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly DispatcherTimer _cleanupTimer = new() { Interval = TimeSpan.FromMinutes(1) };

    private BrowserTab? CurrentTab => _browserTabs.FirstOrDefault(tab => tab.Item == _tabs.SelectedItem);

    public MainView()
    {
        var layout = new DockPanel { LastChildFill = true };
        var header = new StackPanel { Spacing = 8, Margin = new Thickness(12) };
        var brand = new TextBlock
        {
            Text = "INTERNET PRO",
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#135E73"))
        };
        header.Children.Add(brand);

        var navigation = new WrapPanel { Orientation = Orientation.Horizontal, ItemWidth = double.NaN, ItemHeight = double.NaN };
        var back = MakeButton("Înapoi", (_, _) => CurrentTab?.View.GoBack());
        var forward = MakeButton("Înainte", (_, _) => CurrentTab?.View.GoForward());
        var reload = MakeButton("Reîncarcă", (_, _) => CurrentTab?.View.Refresh());
        var add = MakeButton("Filă nouă", (_, _) => CreateTab(HomePage));
        var close = MakeButton("Închide fila", (_, _) => CloseCurrentTab());
        var go = MakeButton("Mergi", (_, _) => NavigateAddress());
        _address.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
                NavigateAddress();
        };
        navigation.Children.Add(back);
        navigation.Children.Add(forward);
        navigation.Children.Add(reload);
        navigation.Children.Add(add);
        navigation.Children.Add(close);
        navigation.Children.Add(_address);
        navigation.Children.Add(go);
        header.Children.Add(navigation);

        var optimization = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        optimization.Children.Add(new TextBlock
        {
            Text = "Optimizare file",
            VerticalAlignment = VerticalAlignment.Center
        });
        _optimizationSlider.ValueChanged += (_, _) => UpdateOptimizationLabel();
        optimization.Children.Add(_optimizationSlider);
        optimization.Children.Add(_optimizationLabel);
        header.Children.Add(optimization);
        UpdateOptimizationLabel();

        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(_tabs);
        Content = layout;

        _tabs.SelectionChanged += (_, _) => UpdateSelectedTab();
        _cleanupTimer.Tick += (_, _) => CloseInactiveTabs();
        _cleanupTimer.Start();
        CreateTab(HomePage);
    }

    private static Button MakeButton(string text, EventHandler<RoutedEventArgs> onClick)
    {
        var button = new Button { Content = text, Margin = new Thickness(2), MinHeight = 36 };
        button.Click += onClick;
        return button;
    }

    private void UpdateOptimizationLabel()
    {
        var level = GetSelectedOptimizationLevel();
        var settings = TabRetentionPolicy.GetSettings(level);
        var levelName = level switch
        {
            TabOptimizationLevel.Moderate => "Moderat",
            TabOptimizationLevel.Strict => "Strict",
            _ => "Maxim"
        };
        _optimizationLabel.Text = $"{levelName}: închide după {settings.IdleThreshold.TotalMinutes:0} min; păstrează {settings.MaximumInactiveTabs}";
        SaveOptimizationLevel(level);
    }

    private static TabOptimizationLevel LoadOptimizationLevel()
    {
        try
        {
            if (int.TryParse(File.ReadAllText(OptimizationLevelPath), out var value)
                && Enum.IsDefined((TabOptimizationLevel)value))
                return (TabOptimizationLevel)value;
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return TabOptimizationLevel.Moderate;
    }

    private static void SaveOptimizationLevel(TabOptimizationLevel level)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(OptimizationLevelPath)!);
            File.WriteAllText(OptimizationLevelPath, ((int)level).ToString());
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private TabOptimizationLevel GetSelectedOptimizationLevel()
    {
        return (TabOptimizationLevel)Math.Clamp((int)Math.Round(_optimizationSlider.Value), 0, 2);
    }

    private void CreateTab(Uri address)
    {
        var view = new NativeWebView { Source = address };
        var item = new TabItem { Header = "Filă nouă", Content = view };
        var tab = new BrowserTab(Guid.NewGuid().ToString("N"), item, view);
        view.NavigationStarted += (_, args) =>
        {
            if (args.Request is not { } request || !IsAllowedUri(request))
            {
                args.Cancel = true;
                return;
            }

            if (request.Host.Length > 0)
                item.Header = request.Host;
        };
        view.NavigationCompleted += (_, args) =>
        {
            if (args.Request is { } request && request.Host.Length > 0)
                item.Header = request.Host;
        };
        view.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            if (args.Request is { } requestedUri)
                CreateTab(requestedUri);
        };

        _browserTabs.Add(tab);
        _tabs.Items.Add(item);
        _tabs.SelectedItem = item;
    }

    private static bool IsAllowedUri(Uri uri)
    {
        if (uri.Scheme is not ("http" or "https"))
            return false;

        var blockedDomains = DomainFilter.AdultDomains
            .Concat(DomainFilter.AdDomains)
            .Concat(DomainFilter.GamblingDomains)
            .Concat(DomainFilter.TradingDomains)
            .Concat(DomainFilter.RobloxDomains)
            .Concat(DomainFilter.MinecraftDomains)
            .Concat(DomainFilter.TikTokDomains);
        return !DomainFilter.MatchesDomain(uri.Host, blockedDomains);
    }

    private void NavigateAddress()
    {
        if (CurrentTab is not { } tab || string.IsNullOrWhiteSpace(_address.Text))
            return;

        var address = _address.Text.Trim();
        if (!address.Contains('.', StringComparison.Ordinal))
            address = "https://www.google.com/search?q=" + Uri.EscapeDataString(address);
        else if (!address.Contains("://", StringComparison.Ordinal))
            address = "https://" + address;

        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri is null || !IsAllowedUri(uri))
            return;

        tab.View.Navigate(uri);
    }

    private void UpdateSelectedTab()
    {
        if (CurrentTab is not { } tab)
            return;
        tab.LastActivatedAt = DateTimeOffset.UtcNow;
        _address.Text = tab.View.Source?.ToString();
    }

    private void CloseCurrentTab()
    {
        if (CurrentTab is not { } tab)
            return;
        if (_browserTabs.Count == 1)
        {
            tab.View.Navigate(HomePage);
            return;
        }

        CloseTab(tab);
    }

    private void CloseInactiveTabs()
    {
        var tabIds = TabRetentionPolicy.SelectTabsToClose(
            _browserTabs.Select(tab => new BrowserTabActivity(tab.Id, tab.LastActivatedAt)),
            CurrentTab?.Id,
            DateTimeOffset.UtcNow,
            GetSelectedOptimizationLevel());

        foreach (var id in tabIds)
        {
            var tab = _browserTabs.FirstOrDefault(candidate => candidate.Id == id);
            if (tab is not null && _browserTabs.Count > 1)
                CloseTab(tab);
        }
    }

    private void CloseTab(BrowserTab tab)
    {
        _browserTabs.Remove(tab);
        _tabs.Items.Remove(tab.Item);
        tab.Item.Content = null;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _cleanupTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _cleanupTimer.Start();
    }

    private sealed class BrowserTab(string id, TabItem item, NativeWebView view)
    {
        public string Id { get; } = id;
        public TabItem Item { get; } = item;
        public NativeWebView View { get; } = view;
        public DateTimeOffset LastActivatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}