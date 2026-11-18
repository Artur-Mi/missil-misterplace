using ProtoBuf;

namespace Cython.GuidedMissiles
{
    // Sets the scan range of an Iron Dome launcher (see MissileLauncher.SetRange). A client sends it to the server,
    // which applies it and relays it to everyone.
    [ProtoContract]
    public class RangePacket : PacketBase
    {
        public RangePacket() { }

        [ProtoMember(1)]
        public long blockId;

        [ProtoMember(2)]
        public float range;

        public void Setup(long id, float value)
        {
            blockId = id;
            range = value;
        }

        public static event ReceiveDelegate<RangePacket> OnReceive;

        public override void Received(ref PacketInfo packetInfo, ulong senderSteamId)
        {
            OnReceive?.Invoke(this, ref packetInfo, senderSteamId);
        }
    }
}
