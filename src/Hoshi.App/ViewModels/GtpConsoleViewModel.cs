using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services;
using Hoshi.App.Services.Engines;
using Hoshi.Core.Localization;
using Hoshi.Engines.Gtp;
using Hoshi.Engines.KataGo;

namespace Hoshi.App.ViewModels;

/// <summary>A console line as shown: time, engine, arrow and text, coloured by direction.</summary>
public sealed record ConsoleRow(string Time, string Engine, string Arrow, string Text, GtpDirection Direction)
{
    public bool IsSent => Direction == GtpDirection.Sent;

    public bool IsLog => Direction == GtpDirection.Log;

    public bool IsError => Direction == GtpDirection.Received && Text.StartsWith('?');
}

/// <summary>
/// The GTP console: every line Hoshi sends to its engines and every line they answer (stdout and their log), plus a
/// box to send commands by hand to one engine (starting it if needed).
/// </summary>
public sealed partial class GtpConsoleViewModel : ViewModelBase
{
    public const int MaxRows = 2000;

    private readonly IGtpEngineHost _host;
    private readonly IUiDispatcher _ui;

    [ObservableProperty]
    private EngineChoice? _engine;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _command = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool _isSending;

    [ObservableProperty]
    private bool _showLog = true;

    public GtpConsoleViewModel(IGtpEngineHost host, IUiDispatcher ui, GameViewModel? game = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _game = game;
        if (game is not null)
        {
            game.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(GameViewModel.Online) or nameof(GameViewModel.IsOnline) or nameof(GameViewModel.StatusText))
                {
                    OnPropertyChanged(nameof(IsBlocked));
                    SendCommand.NotifyCanExecuteChanged();
                }
            };
        }
        foreach (ConsoleLine line in host.ConsoleLines.TakeLast(MaxRows))
        {
            Rows.Add(Row(line));
        }

        Engines = [.. host.Engines];
        _engine = Engines.FirstOrDefault();
        host.ConsoleLineAdded += (_, line) => _ui.Post(() => Append(line));
        host.EnginesChanged += (_, _) => _ui.Post(() =>
        {
            string? id = Engine?.Id;
            Engines.Clear();
            foreach (EngineChoice e in _host.Engines)
            {
                Engines.Add(e);
            }

            Engine = Engines.FirstOrDefault(e => e.Id == id) ?? Engines.FirstOrDefault();
        });
    }

    private readonly GameViewModel? _game;

    /// <summary>No engine help during your own OGS game in progress (OGS forbids AI assistance).</summary>
    public bool IsBlocked => _game?.Online is { IsPlayer: true, IsFinished: false };

    public ObservableCollection<ConsoleRow> Rows { get; } = [];

    public ObservableCollection<EngineChoice> Engines { get; }

    /// <summary>Raised after a row is added (the view scrolls to the end).</summary>
    public event EventHandler? RowAdded;

    private readonly List<string> _history = [];
    private int _historyIndex;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task Send()
    {
        if (Engine is not { } engine || string.IsNullOrWhiteSpace(Command))
        {
            return;
        }

        string command = Command.Trim();
        _history.Add(command);
        _historyIndex = _history.Count;
        Command = string.Empty;
        IsSending = true;
        try
        {
            GtpEngine gtp = await _host.AcquireAsync(engine);
            await gtp.SendRawAsync(command);
        }
        catch (EngineException ex)
        {
            Append(new ConsoleLine(engine.Name, GtpDirection.Log, ex.Message, DateTime.Now));
        }
        finally
        {
            IsSending = false;
        }
    }

    private bool CanSend() => !IsSending && !IsBlocked && Command.Trim().Length > 0 && Engine is not null;

    /// <summary>Up/Down in the command box: earlier and later commands.</summary>
    public void History(int direction)
    {
        if (_history.Count == 0)
        {
            return;
        }

        _historyIndex = Math.Clamp(_historyIndex + direction, 0, _history.Count);
        Command = _historyIndex < _history.Count ? _history[_historyIndex] : string.Empty;
    }

    [RelayCommand]
    private void Clear() => Rows.Clear();

    private void Append(ConsoleLine line)
    {
        if (!ShowLog && line.Direction == GtpDirection.Log)
        {
            return;
        }

        Rows.Add(Row(line));
        while (Rows.Count > MaxRows)
        {
            Rows.RemoveAt(0);
        }

        RowAdded?.Invoke(this, EventArgs.Empty);
    }

    private static ConsoleRow Row(ConsoleLine line) => new(
        line.Time.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
        line.Engine,
        line.Direction switch { GtpDirection.Sent => "→", GtpDirection.Received => "←", _ => "·" },
        line.Text.Length == 0 && line.Direction == GtpDirection.Received ? Tr.T("Engines.EndOfAnswer") : line.Text,
        line.Direction);
}
