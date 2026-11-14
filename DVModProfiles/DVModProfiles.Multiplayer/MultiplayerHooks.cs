using System;
using MPAPI;
using MPAPI.Interfaces;

namespace DVModProfiles.Multiplayer;

public static class MultiplayerHooks
{
    private const int CHUNK_SIZE = 32 * 1024;

    private static int transfers;

    private static Action<string> log = _ => { };
    private static Action<string, Exception> logException = (_, _) => { };
    private static Func<bool> sharing = () => false;
    private static Func<byte[]> captureHostProfile = () => [];
    private static Action joining = () => { };
    private static Action<int, int, int, byte[]> pieceReceived = (_, _, _, _) => { };
    private static Action unreadable = () => { };

    // False when the Multiplayer mod is installed but its API isn't up, and nothing is hooked
    public static bool Init(
        Action<string> log,
        Action<string, Exception> logException,
        Func<bool> sharing,
        Func<byte[]> captureHostProfile,
        Action joining,
        Action left,
        Action<int, int, int, byte[]> pieceReceived,
        Action unreadable)
    {
        if (!MultiplayerAPI.IsMultiplayerLoaded)
        {
            return false;
        }

        MultiplayerHooks.log = log;
        MultiplayerHooks.logException = logException;
        MultiplayerHooks.sharing = sharing;
        MultiplayerHooks.captureHostProfile = captureHostProfile;
        MultiplayerHooks.joining = joining;
        MultiplayerHooks.pieceReceived = pieceReceived;
        MultiplayerHooks.unreadable = unreadable;

        MultiplayerAPI.ServerStarted += OnServerStarted;
        MultiplayerAPI.ClientStarted += OnClientStarted;
        MultiplayerAPI.ClientStopped += left;
        return true;
    }

    // A player is connected once it's let in, just before the server sends it the save, so the
    // settings get there ahead of the world load.
    private static void OnServerStarted(IServer server) =>
        server.OnPlayerConnected += player => SendHostProfile(server, player);

    private static void SendHostProfile(IServer server, IPlayer player)
    {
        if (player.IsHost || !sharing())
        {
            return;
        }

        try
        {
            byte[] data = captureHostProfile();
            int count = Math.Max(1, (data.Length + CHUNK_SIZE - 1) / CHUNK_SIZE);
            int transfer = ++transfers;

            for (int index = 0; index < count; index++)
            {
                int offset = index * CHUNK_SIZE;
                byte[] piece = new byte[Math.Min(CHUNK_SIZE, data.Length - offset)];
                Buffer.BlockCopy(data, offset, piece, 0, piece.Length);

                var packet = new HostProfilePacket { Transfer = transfer, Index = index, Count = count, Data = piece };
                server.SendSerializablePacketToPlayer(packet, player, reliable: true);
            }

            log($"Sent our mod settings to {player.DisplayName}");
        }
        catch (Exception ex)
        {
            logException($"Failed to send our mod settings to {player.DisplayName}", ex);
        }
    }

    private static void OnClientStarted(IClient client)
    {
        // The host plays through a client of its own, connected to its own server
        if (MultiplayerAPI.Instance?.IsHost == true)
        {
            return;
        }

        client.RegisterSerializablePacket<HostProfilePacket>(OnHostProfilePacket);
        joining();
    }

    private static void OnHostProfilePacket(HostProfilePacket packet)
    {
        if (packet.Protocol != HostProfilePacket.PROTOCOL)
        {
            unreadable();
            return;
        }

        pieceReceived(packet.Transfer, packet.Index, packet.Count, packet.Data);
    }
}
