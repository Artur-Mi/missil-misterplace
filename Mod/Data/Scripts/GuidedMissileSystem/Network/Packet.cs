using ProtoBuf;

namespace Cython.GuidedMissiles
{
    [ProtoContract]
    public class Packet : PacketBase
    {
        public Packet() { }

        [ProtoMember(1)]
        public long targetGrid;

        [ProtoMember(2)]
        public bool clear;

        public void Setup(long target, bool clean)
        {
            targetGrid = target;
            clear = clean;
        }

        public static event ReceiveDelegate<Packet> OnReceive;

        public override void Received(ref PacketInfo packetInfo, ulong senderSteamId)
        {
            OnReceive?.Invoke(this, ref packetInfo, senderSteamId);
        }
    }
}
