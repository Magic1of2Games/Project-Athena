using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SyndicateLauncher.Services;

namespace SyndicateLauncher.Views;

/// <summary>
/// Admin panel for in-game progress (Settings > Launcher > Progress admin): pick a season, see its level, XP and
/// Battle Pass tier, and add XP to it. Built in code on purpose (no XAML), and every colour is a theme brush
/// (SetResourceReference), so it follows the player's theme. The backend does the level maths.
/// </summary>
public sealed class ProgressAdminWindow : Window
{
    private sealed record SeasonItem(SeasonProgress Progress)
    {
        public override string ToString() =>
            $"{ProgressAdmin.SeasonName(Progress.Season)}  ·  Level {Progress.Level}  ·  Tier {Progress.Tier}" +
            (Progress.Active ? "  (active)" : "");
    }

    private readonly string _profile;
    private readonly Func<Task> _ensureBackend;
    private readonly ComboBox _seasons = new();
    private readonly TextBlock _current = new();
    private readonly TextBox _amount = new();
    private readonly Button _add = new();
    private readonly TextBlock _status = new();
    private readonly List<Button> _quick = new();
    private long _maxXp = 10_000_000;
    private bool _busy;

    public ProgressAdminWindow(string profile, int preferredSeason, Func<Task> ensureBackend)
    {
        _profile = profile;
        _ensureBackend = ensureBackend;
        _preferredSeason = preferredSeason;

        Title = "Progress admin";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "ThemeBackgroundBrush");

        var stack = new StackPanel { Margin = new Thickness(24, 22, 24, 22) };
        stack.Children.Add(Block("PROGRESS ADMIN", 16, true, "TextBrush"));
        stack.Children.Add(Block("Profile: " + profile, 12, false, "MutedTextBrush", new Thickness(0, 4, 0, 16)));

        stack.Children.Add(Block("Season", 12, true, "TextBrush", new Thickness(0, 0, 0, 6)));
        _seasons.Height = 34;
        _seasons.IsEnabled = false;
        _seasons.SelectionChanged += (_, _) => ShowCurrent();
        stack.Children.Add(_seasons);

        _current.FontSize = 12;
        _current.Margin = new Thickness(0, 10, 0, 16);
        _current.TextWrapping = TextWrapping.Wrap;
        _current.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        stack.Children.Add(_current);

        stack.Children.Add(Block("XP to add", 12, true, "TextBrush", new Thickness(0, 0, 0, 6)));
        _amount.Height = 34;
        _amount.FontSize = 14;
        _amount.VerticalContentAlignment = VerticalAlignment.Center;
        _amount.Text = "1000";
        _amount.MaxLength = 9;
        _amount.PreviewTextInput += (_, e) => e.Handled = e.Text.Any(c => !char.IsDigit(c));
        _amount.KeyDown += (_, e) => { if (e.Key == Key.Enter) AddClicked(this, new RoutedEventArgs()); };
        DataObject.AddPastingHandler(_amount, (_, e) =>
        {
            if (e.DataObject.GetData(DataFormats.Text) is not string text || text.Any(c => !char.IsDigit(c))) e.CancelCommand();
        });
        stack.Children.Add(_amount);

        var quick = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (long value in new long[] { 1_000, 10_000, 100_000 })
        {
            var button = Ghost("+" + value.ToString("N0", CultureInfo.InvariantCulture));
            button.Margin = new Thickness(0, 0, 8, 0);
            button.Click += (_, _) => _amount.Text = value.ToString(CultureInfo.InvariantCulture);
            quick.Children.Add(button);
            _quick.Add(button);
        }
        stack.Children.Add(quick);

        _add.Content = "ADD XP";
        _add.Height = 40;
        _add.Margin = new Thickness(0, 18, 0, 0);
        _add.IsEnabled = false;
        if (TryFindResource("GhostButton") is Style ghost) _add.Style = ghost;
        _add.Click += AddClicked;
        stack.Children.Add(_add);

        _status.FontSize = 12;
        _status.Margin = new Thickness(0, 12, 0, 0);
        _status.TextWrapping = TextWrapping.Wrap;
        _status.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        stack.Children.Add(_status);

        stack.Children.Add(Block(
            "The level and tier follow the normal season tables, tier rewards are granted, and the active season stays as it is. " +
            "Use it before you start the game; a running game shows the new level after you restart it or log in again.",
            10.5, false, "DimTextBrush", new Thickness(0, 14, 0, 0)));

        Content = stack;
        Loaded += async (_, _) => await ReloadAsync(null);
    }

    private readonly int _preferredSeason;

    private TextBlock Block(string text, double size, bool bold, string brush, Thickness? margin = null)
    {
        var block = new TextBlock
        {
            Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal
        };
        if (margin is Thickness m) block.Margin = m;
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    private Button Ghost(string text)
    {
        var button = new Button { Content = text, Height = 30, Padding = new Thickness(14, 0, 14, 0) };
        if (TryFindResource("GhostButton") is Style ghost) button.Style = ghost;
        return button;
    }

    private void SetStatus(string text, string brush)
    {
        _status.Text = text;
        _status.SetResourceReference(TextBlock.ForegroundProperty, brush);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _seasons.IsEnabled = !busy && _seasons.Items.Count > 0;
        _add.IsEnabled = !busy && _seasons.Items.Count > 0;
        foreach (var button in _quick) button.IsEnabled = !busy;
        _amount.IsEnabled = !busy;
    }

    private async Task ReloadAsync(int? selectSeason)
    {
        SetBusy(true);
        if (selectSeason is null) SetStatus("Loading progress…", "MutedTextBrush");
        try
        {
            if (!await SyndicateBackend.IsAvailableAsync())
            {
                SetStatus("Starting the backend…", "MutedTextBrush");
                await _ensureBackend();
            }
            if (!await SyndicateBackend.IsAvailableAsync())
            {
                SetStatus("The backend is not running. Start Syndicate once (or select a build) and open this panel again.", "WarnBrush");
                return;
            }
            var overview = await ProgressAdmin.LoadAsync(_profile);
            _maxXp = overview.MaxXp;
            int wanted = selectSeason ?? (overview.Seasons.Any(s => s.Season == _preferredSeason) ? _preferredSeason : overview.ActiveSeason);
            _seasons.Items.Clear();
            foreach (var season in overview.Seasons) _seasons.Items.Add(new SeasonItem(season));
            _seasons.SelectedItem = _seasons.Items.Cast<SeasonItem>().FirstOrDefault(i => i.Progress.Season == wanted)
                                    ?? _seasons.Items.Cast<SeasonItem>().FirstOrDefault();
            if (selectSeason is null) SetStatus("", "MutedTextBrush");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, "DangerBrush");
        }
        finally
        {
            SetBusy(false);
            ShowCurrent();
        }
    }

    private void ShowCurrent()
    {
        if (_seasons.SelectedItem is not SeasonItem item) { _current.Text = ""; return; }
        var p = item.Progress;
        string toNext = p.Level >= 100 ? "max level reached" : $"{p.Xp:N0} / {p.XpToNext:N0} XP toward level {p.Level + 1}";
        _current.Text = $"Level {p.Level} · {toNext} · Battle Pass tier {p.Tier}";
    }

    private async void AddClicked(object? sender, RoutedEventArgs e)
    {
        if (_busy || _seasons.SelectedItem is not SeasonItem item) return;
        if (!long.TryParse(_amount.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long amount) || amount < 1 || amount > _maxXp)
        {
            SetStatus($"Enter a whole XP amount from 1 to {_maxXp:N0}.", "DangerBrush");
            return;
        }
        SetBusy(true);
        try
        {
            var result = await ProgressAdmin.AddXpAsync(_profile, item.Progress.Season, amount);
            await ReloadAsync(result.Season);
            string name = ProgressAdmin.SeasonName(result.Season);
            SetStatus(result.Maxed
                ? $"{name} is held at max by Unlock All, so its level did not change."
                : $"Added {amount:N0} XP to {name}. It is now level {result.Level}, tier {result.Tier}.",
                result.Maxed ? "WarnBrush" : "SuccessBrush");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, "DangerBrush");
        }
        finally
        {
            SetBusy(false);
        }
    }
}
