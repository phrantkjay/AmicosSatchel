using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using XIVMap.Shared;

namespace AmicosSatchel.Modules.SecondSight;

/// <summary>
/// Hosts the named pipe on a background thread. The game thread only hands over the latest
/// snapshot; serialization and all pipe I/O happen here. Older snapshots are dropped if the
/// app falls behind, so nothing ever queues up. The app can send a few requests back (a
/// confirmed teleport); those are raised through <see cref="MessageReceived"/> on the pipe's
/// thread, so the receiver must hand them to the game thread itself.
/// </summary>
internal sealed class PipeServer : IDisposable {
    private readonly IPluginLog log;
    private readonly string pluginVersion;
    private readonly CancellationTokenSource cts = new();
    private readonly SemaphoreSlim signal = new(0, 1);
    private readonly ConcurrentQueue<Message> outgoing = new();
    private readonly Task loop;

    private ZoneMessage? latestZone;
    private StateMessage? latestState;
    private PlayerMessage? latestPlayer;
    private ProgressMessage? latestProgress;
    private TravelInfoMessage? latestTravel;
    private FriendsMessage? latestFriends;
    private volatile bool connected;

    public PipeServer(IPluginLog log, string pluginVersion) {
        this.log = log;
        this.pluginVersion = pluginVersion;
        loop = Task.Run(() => RunAsync(cts.Token));
    }

    public bool IsConnected => connected;

    /// <summary>A message from the app. Raised on the pipe's background thread.</summary>
    public event Action<Message>? MessageReceived;

    /// <summary>Called from the game thread. Never blocks.</summary>
    public void Publish(ZoneMessage zone, StateMessage state) {
        Volatile.Write(ref latestZone, zone);
        Interlocked.Exchange(ref latestState, state);
        Interlocked.Exchange(ref latestPlayer, null); // the state already carries the player
        Signal();
    }

    /// <summary>
    /// Called from the game thread when unlocks change. Kept (like the zone) so every new
    /// connection is sent the latest one. Never blocks.
    /// </summary>
    public void PublishProgress(ProgressMessage progress) {
        Volatile.Write(ref latestProgress, progress);
        Signal();
    }

    /// <summary>Teleport permission, Lifestream and costs; kept so every new connection gets it. Never blocks.</summary>
    public void PublishTravel(TravelInfoMessage travel) {
        Volatile.Write(ref latestTravel, travel);
        Signal();
    }

    /// <summary>The online friends; kept so every new connection gets them. Never blocks.</summary>
    public void PublishFriends(FriendsMessage friends) {
        Volatile.Write(ref latestFriends, friends);
        Signal();
    }

    /// <summary>A one-off message for the app (e.g. a teleport result). Never blocks.</summary>
    public void Send(Message message) {
        outgoing.Enqueue(message);
        Signal();
    }

    /// <summary>Called from the game thread between full states. Never blocks.</summary>
    public void PublishPlayer(PlayerMessage player) {
        Interlocked.Exchange(ref latestPlayer, player);
        Signal();
    }

    private void Signal() {
        try {
            if (signal.CurrentCount == 0)
                signal.Release();
        } catch (SemaphoreFullException) {
            // The writer has not consumed the previous signal yet; it will pick up the new state.
        }
    }

    private async Task RunAsync(CancellationToken ct) {
        while (!ct.IsCancellationRequested) {
            // Ends this connection when either the reader or the writer sees the app go away.
            using var connection = CancellationTokenSource.CreateLinkedTokenSource(ct);
            try {
                await using var pipe = new NamedPipeServerStream(
                    Protocol.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(ct);
                connected = true;
                log.Information("Amico's Second Sight app connected");
                while (outgoing.TryDequeue(out _)) { } // results meant for an earlier connection

                await Framing.WriteAsync(pipe, new HelloMessage(Protocol.Version, pluginVersion), ct);
                var reader = ReadLoopAsync(pipe, connection);

                ZoneMessage? sentZone = null;
                ProgressMessage? sentProgress = null;
                TravelInfoMessage? sentTravel = null;
                FriendsMessage? sentFriends = null;
                while (pipe.IsConnected) {
                    await signal.WaitAsync(connection.Token);

                    var progress = Volatile.Read(ref latestProgress);
                    if (progress != null && progress != sentProgress) {
                        await Framing.WriteAsync(pipe, progress, ct);
                        sentProgress = progress;
                    }

                    var travel = Volatile.Read(ref latestTravel);
                    if (travel != null && travel != sentTravel) {
                        await Framing.WriteAsync(pipe, travel, ct);
                        sentTravel = travel;
                    }

                    var friends = Volatile.Read(ref latestFriends);
                    if (friends != null && friends != sentFriends) {
                        await Framing.WriteAsync(pipe, friends, ct);
                        sentFriends = friends;
                    }

                    while (outgoing.TryDequeue(out var message))
                        await Framing.WriteAsync(pipe, message, ct);

                    var zone = Volatile.Read(ref latestZone);
                    var state = Interlocked.Exchange(ref latestState, null);
                    var player = Interlocked.Exchange(ref latestPlayer, null);

                    if (zone != null && zone != sentZone) {
                        await Framing.WriteAsync(pipe, zone, ct);
                        sentZone = zone;
                    }

                    if (state != null)
                        await Framing.WriteAsync(pipe, state, ct);
                    else if (player != null)
                        await Framing.WriteAsync(pipe, player, ct);
                }

                await reader;
            } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                break;
            } catch (OperationCanceledException) {
                // The reader saw the app disconnect; wait for the next one.
            } catch (IOException) {
                // App closed the pipe, or another game client already owns the pipe name. Retry quietly.
                await DelayQuietly(ct);
            } catch (Exception ex) {
                log.Warning(ex, "Unexpected pipe error");
                await DelayQuietly(ct);
            } finally {
                connection.Cancel();
                if (connected) {
                    connected = false;
                    log.Information("Amico's Second Sight app disconnected");
                }
            }
        }
    }

    /// <summary>Reads the app's requests until it disconnects, then ends the connection.</summary>
    private async Task ReadLoopAsync(Stream pipe, CancellationTokenSource connection) {
        try {
            while (await Framing.ReadAsync(pipe, connection.Token) is { } message) {
                try {
                    MessageReceived?.Invoke(message);
                } catch (Exception e) {
                    log.Warning(e, "Couldn't handle a message from the Second Sight map.");
                }
            }
        } catch (Exception e) when (e is IOException or InvalidDataException or JsonException or OperationCanceledException or ObjectDisposedException) {
            // Disconnected, or a bad message; either way this connection is done.
        } finally {
            try {
                connection.Cancel();
            } catch (ObjectDisposedException) {
                // The connection was already cleaned up by the writer.
            }
        }
    }

    private static async Task DelayQuietly(CancellationToken ct) {
        try {
            await Task.Delay(1000, ct);
        } catch (OperationCanceledException) {
        }
    }

    public void Dispose() {
        cts.Cancel();
        try {
            loop.Wait(TimeSpan.FromSeconds(2));
        } catch (AggregateException) {
        }

        cts.Dispose();
        signal.Dispose();
    }
}
