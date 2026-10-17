using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Hoshi.App.Services.Presence;
using Hoshi.App.ViewModels;
using Point = Hoshi.Core.Point;

namespace Hoshi.App.Tests;

/// <summary>The Discord status: off by default, only the kind of activity, Discord's IPC frames.</summary>
public sealed class PresenceTests
{
    /// <summary>Discord's side of the pipe: answers the handshake with READY and keeps what Hoshi sent.</summary>
    private sealed class FakeDiscord : Stream
    {
        private readonly MemoryStream _toHoshi = new();
        private readonly List<byte> _fromHoshi = [];

        public List<(int Opcode, JsonElement Body)> Frames { get; } = [];

        public bool Disposed { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => _toHoshi.Read(buffer, offset, count);

        public override void Write(byte[] buffer, int offset, int count)
        {
            _fromHoshi.AddRange(buffer.AsSpan(offset, count).ToArray());
            while (_fromHoshi.Count >= 8)
            {
                byte[] all = [.. _fromHoshi];
                int length = BinaryPrimitives.ReadInt32LittleEndian(all.AsSpan(4));
                if (all.Length < 8 + length)
                {
                    break;
                }

                int opcode = BinaryPrimitives.ReadInt32LittleEndian(all);
                Frames.Add((opcode, JsonDocument.Parse(all.AsMemory(8, length)).RootElement.Clone()));
                _fromHoshi.RemoveRange(0, 8 + length);
                if (opcode == 0)
                {
                    byte[] ready = Encoding.UTF8.GetBytes("{\"cmd\":\"DISPATCH\",\"evt\":\"READY\"}");
                    long at = _toHoshi.Position;
                    _toHoshi.Seek(0, SeekOrigin.End);
                    byte[] header = new byte[8];
                    BinaryPrimitives.WriteInt32LittleEndian(header, 1);
                    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), ready.Length);
                    _toHoshi.Write(header);
                    _toHoshi.Write(ready);
                    _toHoshi.Position = at;
                }
            }
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class FakeTransport : IPresenceTransport
    {
        public FakeDiscord? Last { get; private set; }

        public int Connections { get; private set; }

        public bool Available { get; set; } = true;

        public Task<Stream?> ConnectAsync(CancellationToken cancellationToken)
        {
            Connections++;
            if (!Available)
            {
                return Task.FromResult<Stream?>(null);
            }

            Last = new FakeDiscord();
            return Task.FromResult<Stream?>(Last);
        }
    }

    [Fact]
    public async Task Nothing_is_sent_until_the_status_is_on_with_an_application_id()
    {
        var transport = new FakeTransport();
        var settings = new TestSettings();
        var presence = new DiscordPresence(transport, settings);

        await presence.UpdateAsync(PresenceActivity.PlayingOgs);
        transport.Connections.Should().Be(0, "off by default");

        settings.Save(settings.Current with { DiscordPresence = true, DiscordAppId = "not-a-number" });
        await presence.UpdateAsync(PresenceActivity.PlayingOgs);
        transport.Connections.Should().Be(0);
    }

    [Fact]
    public async Task The_handshake_then_the_activity_without_any_game_data()
    {
        var transport = new FakeTransport();
        var settings = new TestSettings();
        settings.Save(settings.Current with { DiscordPresence = true, DiscordAppId = "123456789012345678" });
        var presence = new DiscordPresence(transport, settings);

        await presence.UpdateAsync(PresenceActivity.Studying);
        await presence.UpdateAsync(PresenceActivity.Studying);
        await presence.UpdateAsync(PresenceActivity.PlayingOgs);

        FakeDiscord discord = transport.Last!;
        discord.Frames.Select(f => f.Opcode).Should().Equal(0, 1, 1);
        discord.Frames[0].Body.GetProperty("client_id").GetString().Should().Be("123456789012345678");
        JsonElement activity = discord.Frames[2].Body.GetProperty("args").GetProperty("activity");
        discord.Frames[2].Body.GetProperty("cmd").GetString().Should().Be("SET_ACTIVITY");
        activity.GetProperty("details").GetString().Should().Be(DiscordPresence.Text(PresenceActivity.PlayingOgs).Details);
        activity.EnumerateObject().Select(p => p.Name).Should().BeSubsetOf(["details", "state", "timestamps"]);

        settings.Save(settings.Current with { DiscordPresence = false });
        await presence.UpdateAsync(PresenceActivity.PlayingOgs);
        discord.Frames.Should().HaveCount(4);
        discord.Frames[3].Body.GetProperty("args").GetProperty("activity").ValueKind.Should().Be(JsonValueKind.Null, "turned off: cleared");
        discord.Disposed.Should().BeTrue();
        transport.Connections.Should().Be(1);
    }

    [Fact]
    public async Task Without_Discord_it_waits_before_trying_again()
    {
        var transport = new FakeTransport { Available = false };
        var settings = new TestSettings();
        settings.Save(settings.Current with { DiscordPresence = true, DiscordAppId = "42" });
        var time = new ManualTime();
        var presence = new DiscordPresence(transport, settings, time);

        await presence.UpdateAsync(PresenceActivity.Studying);
        await presence.UpdateAsync(PresenceActivity.PlayingLocal);
        transport.Connections.Should().Be(1);
        time.Now += DiscordPresence.RetryAfter;
        transport.Available = true;
        await presence.UpdateAsync(PresenceActivity.PlayingLocal);
        transport.Connections.Should().Be(2);
        presence.Shown.Should().Be(PresenceActivity.PlayingLocal);
    }

    [AvaloniaFact]
    public void The_activity_follows_the_board()
    {
        var game = new GameViewModel();
        var main = new MainWindowViewModel(game, settings: new TestSettings());
        main.CurrentActivity().Should().Be(PresenceActivity.None);
        game.PlayCommand.Execute(new Point(3, 3));
        main.CurrentActivity().Should().Be(PresenceActivity.PlayingLocal);
        main.Study.IsActive = true;
        main.CurrentActivity().Should().Be(PresenceActivity.Studying);
    }
}
