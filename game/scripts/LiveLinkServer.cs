using System.Text;
using Godot;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// A TCP server, active only in development runs, that lets a Racket REPL
/// (racket/heroic/live.rkt) drive the running game: read and write named
/// fields on any loaded machine (see <see cref="MachineRuntime.FieldGetters"/>),
/// subscribe to a telemetry stream, and switch build/run mode. Every
/// message in both directions is one S-expression per line, parsed and
/// written with the same reader the .machine format uses.
///
/// Enable with the environment variable HEROIC_LIVE_LINK=1. Off by
/// default: the shipped game never opens this port.
/// </summary>
public partial class LiveLinkServer : Node
{
    public const string EnableEnvVar = "HEROIC_LIVE_LINK";
    public const string PortEnvVar = "HEROIC_LIVE_LINK_PORT";
    public const int DefaultPort = 4747;

    private readonly IReadOnlyDictionary<string, MachineView> _machines;
    private readonly Action<bool> _setRunning; // true = run, false = build/paused
    private readonly TcpServer _server = new();
    private readonly List<Peer> _peers = [];

    private sealed class Peer
    {
        public required StreamPeerTcp Socket;
        public StringBuilder Buffer = new();
        public readonly List<(string Machine, string Target, string Field)> Subscriptions = [];
        public double SecondsSinceTelemetry;
        public double TelemetryIntervalSeconds = 0.2; // 5 Hz default
    }

    public LiveLinkServer(IReadOnlyDictionary<string, MachineView> machines, Action<bool> setRunning)
    {
        _machines = machines;
        _setRunning = setRunning;
    }

    public override void _Ready()
    {
        int port = int.TryParse(OS.GetEnvironment(PortEnvVar), out int p) ? p : DefaultPort;
        var err = _server.Listen((ushort)port);
        if (err != Error.Ok)
        {
            GD.PushError($"live link: could not listen on port {port}: {err}");
            return;
        }
        GD.Print($"live link: listening on 127.0.0.1:{port}");
    }

    public override void _Process(double delta)
    {
        while (_server.IsConnectionAvailable())
            _peers.Add(new Peer { Socket = _server.TakeConnection() });

        for (int i = _peers.Count - 1; i >= 0; i--)
        {
            var peer = _peers[i];
            if (peer.Socket.GetStatus() != StreamPeerTcp.Status.Connected)
            {
                _peers.RemoveAt(i);
                continue;
            }

            int available = peer.Socket.GetAvailableBytes();
            if (available > 0)
            {
                var result = peer.Socket.GetPartialData(available);
                if ((Error)result[0].AsInt32() == Error.Ok)
                    peer.Buffer.Append(Encoding.UTF8.GetString(result[1].AsByteArray()));
            }

            string text = peer.Buffer.ToString();
            int newline;
            while ((newline = text.IndexOf('\n')) >= 0)
            {
                string line = text[..newline].Trim();
                text = text[(newline + 1)..];
                if (line.Length > 0) Handle(peer, line);
            }
            peer.Buffer = new StringBuilder(text);

            if (peer.Subscriptions.Count > 0)
            {
                peer.SecondsSinceTelemetry += delta;
                if (peer.SecondsSinceTelemetry >= peer.TelemetryIntervalSeconds)
                {
                    peer.SecondsSinceTelemetry = 0;
                    SendTelemetry(peer);
                }
            }
        }
    }

    private void Handle(Peer peer, string line)
    {
        SList form;
        try
        {
            var forms = SExprReader.ReadAll(line);
            if (forms.Count != 1 || forms[0] is not SList f) throw new FormatException("expected one (command ...) form");
            form = f;
        }
        catch (FormatException e)
        {
            Reply(peer, $"(error {Str($"parse error: {e.Message}")})");
            return;
        }

        try
        {
            switch (form.Head)
            {
                case "ping":
                    Reply(peer, "(pong)");
                    break;
                case "list-machines":
                    Reply(peer, $"(machines {string.Join(' ', _machines.Keys)})");
                    break;
                case "get":
                {
                    var (m, target, field) = Args3(form);
                    double v = Machine(m).Runtime.GetField(target, field);
                    Reply(peer, $"(value {m} {target} {field} {Num(v)})");
                    break;
                }
                case "set!":
                {
                    if (form.Items.Count != 5) throw new FormatException("(set! machine target field value)");
                    string m = Sym(form, 1), target = Sym(form, 2), field = Sym(form, 3);
                    double v = ((SNumber)form.Items[4]).Value;
                    Machine(m).Runtime.SetField(target, field, v);
                    Reply(peer, "(ok)");
                    break;
                }
                case "subscribe":
                {
                    var (m, target, field) = Args3(form);
                    _ = Machine(m).Runtime.GetField(target, field); // validates the field now, not at the next tick
                    if (!peer.Subscriptions.Contains((m, target, field)))
                        peer.Subscriptions.Add((m, target, field));
                    Reply(peer, "(ok)");
                    break;
                }
                case "unsubscribe-all":
                    peer.Subscriptions.Clear();
                    Reply(peer, "(ok)");
                    break;
                case "run":
                    _setRunning(true);
                    Reply(peer, "(ok)");
                    break;
                case "pause":
                    _setRunning(false);
                    Reply(peer, "(ok)");
                    break;
                default:
                    Reply(peer, $"(error {Str($"unknown command {form.Head}")})");
                    break;
            }
        }
        catch (Exception e) when (e is MachineFormatException or FormatException or InvalidCastException or KeyNotFoundException)
        {
            Reply(peer, $"(error {Str(e.Message)})");
        }
    }

    private void SendTelemetry(Peer peer)
    {
        var entries = peer.Subscriptions.Select(s =>
        {
            try { return $"({s.Machine} {s.Target} {s.Field} {Num(Machine(s.Machine).Runtime.GetField(s.Target, s.Field))})"; }
            catch { return null; }
        }).Where(e => e is not null);
        Reply(peer, $"(telemetry {Num(Time.GetTicksMsec() / 1000.0)} {string.Join(' ', entries)})");
    }

    private MachineView Machine(string name) =>
        _machines.TryGetValue(name, out var m) ? m : throw new MachineFormatException($"no machine named {name} is loaded");

    private static (string, string, string) Args3(SList form)
    {
        if (form.Items.Count != 4) throw new FormatException($"({form.Head} machine target field)");
        return (Sym(form, 1), Sym(form, 2), Sym(form, 3));
    }

    private static string Sym(SList l, int i) =>
        l.Items[i] is SSymbol s ? s.Name : throw new FormatException($"expected a name at position {i}");

    private static string Num(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    private static string Str(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static void Reply(Peer peer, string text) => peer.Socket.PutData(Encoding.UTF8.GetBytes(text + "\n"));

    public override void _ExitTree()
    {
        foreach (var peer in _peers) peer.Socket.DisconnectFromHost();
        _server.Stop();
    }
}
