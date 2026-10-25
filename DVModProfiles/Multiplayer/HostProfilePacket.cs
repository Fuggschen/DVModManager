using System.IO;
using MPAPI.Interfaces.Packets;

namespace DVModProfiles.Multiplayer;

// One piece of a host's mod settings. The Multiplayer mod can't fit much more than 64 KiB in a
// packet, which a whole profile can outgrow, so it's sent in pieces.
public class HostProfilePacket : ISerializablePacket
{
    // Read first, so a host sending a different layout is recognised rather than misread
    public const int PROTOCOL = 1;

    public int Protocol = PROTOCOL;

    // Which send this piece is part of, so pieces of two sends are never mixed
    public int Transfer;

    public int Index;
    public int Count;
    public byte[] Data = [];

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(Protocol);
        writer.Write(Transfer);
        writer.Write(Index);
        writer.Write(Count);
        writer.Write(Data.Length);
        writer.Write(Data);
    }

    public void Deserialize(BinaryReader reader)
    {
        Protocol = reader.ReadInt32();
        if (Protocol != PROTOCOL)
        {
            return;
        }

        Transfer = reader.ReadInt32();
        Index = reader.ReadInt32();
        Count = reader.ReadInt32();
        Data = reader.ReadBytes(reader.ReadInt32());
    }
}
