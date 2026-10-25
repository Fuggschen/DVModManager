using System.Runtime.CompilerServices;
using MPAPI;
using MPAPI.Interfaces;

namespace DVModProfiles.Multiplayer;

// Everything that talks to the Multiplayer mod's API, which only exists alongside it. Reached only
// through MultiplayerSupport, once the Multiplayer mod is known to be running.
internal static class MultiplayerHooks
{
    // Well under what the Multiplayer mod can fit in one packet, even before it compresses them
    private const int PIECE_SIZE = 32 * 1024;

    private static int transfers;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool Init()
    {
        if (!MultiplayerAPI.IsMultiplayerLoaded)
        {
            Main.Logger.Log("The Multiplayer mod is installed but not loaded; host mod settings won't be shared");
            return false;
        }

        MultiplayerAPI.ServerStarted += OnServerStarted;
        MultiplayerAPI.ClientStarted += OnClientStarted;
        MultiplayerAPI.ClientStopped += OnClientStopped;
        return true;
    }

    // A player is connected once it's let in, just before the server sends it the save, so the
    // settings get there ahead of the world load.
    private static void OnServerStarted(IServer server) =>
        server.OnPlayerConnected += player => SendHostProfile(server, player);

    private static void SendHostProfile(IServer server, IPlayer player)
    {
        if (player.IsHost || !Main.Config.shareWithMultiplayerClients)
        {
            return;
        }

        try
        {
            byte[] data = HostProfile.Current();
            int count = Math.Max(1, (data.Length + PIECE_SIZE - 1) / PIECE_SIZE);
            int transfer = ++transfers;

            for (int index = 0; index < count; index++)
            {
                int offset = index * PIECE_SIZE;
                byte[] piece = new byte[Math.Min(PIECE_SIZE, data.Length - offset)];
                Buffer.BlockCopy(data, offset, piece, 0, piece.Length);

                var packet = new HostProfilePacket { Transfer = transfer, Index = index, Count = count, Data = piece };
                server.SendSerializablePacketToPlayer(packet, player, reliable: true);
            }

            Main.Logger.Log($"Sent our mod settings to {player.DisplayName}");
        }
        catch (Exception ex)
        {
            Main.Logger.LogException($"Failed to send our mod settings to {player.DisplayName}", ex);
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
        HostProfileClient.Start();
    }

    private static void OnClientStopped() => HostProfileClient.Stop();

    private static void OnHostProfilePacket(HostProfilePacket packet)
    {
        if (packet.Protocol != HostProfilePacket.PROTOCOL)
        {
            HostProfileClient.Unreadable();
            return;
        }

        HostProfileClient.Receive(packet.Transfer, packet.Index, packet.Count, packet.Data);
    }
}
