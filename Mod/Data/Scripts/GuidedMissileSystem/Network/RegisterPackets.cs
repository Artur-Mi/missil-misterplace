using ProtoBuf;

namespace Cython.GuidedMissiles
{
    [ProtoInclude(10, typeof(Packet))]
    [ProtoInclude(11, typeof(RangePacket))]

    public abstract partial class PacketBase
    {
    }
}