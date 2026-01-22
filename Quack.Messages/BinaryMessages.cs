using System.Text;
using System.Buffers.Binary;
namespace Quack.Messages;

public interface IBinaryMessage : IMessage
{
    int GetSize();
    void Serialize(Span<byte> buffer);
    static abstract IBinaryMessage Deserialize(ReadOnlySpan<byte> buffer);
    
    public static IBinaryMessage Deserialize(MessageType type, ReadOnlySpan<byte> buffer) => type switch
    {
        MessageType.Join => JoinMessage.Deserialize(buffer),
        MessageType.ClientInput => InputMessage.Deserialize(buffer),
        _ => throw new ArgumentOutOfRangeException(nameof(type), "Message type out of range")
    };
}

public class JoinMessage : IBinaryMessage
{
    public MessageType Type => MessageType.Join;
    public string Name { get; set; } = string.Empty;

    public int GetSize() => 4 + Encoding.UTF8.GetByteCount(Name);

    public void Serialize(Span<byte> buffer)
    {
        int byteCount = Encoding.UTF8.GetByteCount(Name);
        BitConverter.TryWriteBytes(buffer, byteCount);
        Encoding.UTF8.GetBytes(Name, buffer[4..]);
    }

    public static IBinaryMessage Deserialize(ReadOnlySpan<byte> buffer)
    {
        JoinMessage message = new JoinMessage();
        int nameLen = BinaryPrimitives.ReadInt32LittleEndian(buffer);
        message.Name = Encoding.UTF8.GetString(buffer.Slice(4, nameLen));
        return message;
    }
}

public class InputMessage : IBinaryMessage
{
    public MessageType Type => MessageType.ClientInput;
    public bool Up { get; set; }
    public bool Down { get; set; }
    public bool Left { get; set; }
    public bool Right { get; set; }
    public bool Sprint { get; set; }

    public int GetSize() => 1;

    public void Serialize(Span<byte> buffer)
    {
        byte value = 0;
        if (Up) value |= (byte)(1 << 0);
        if (Down) value |= (byte)(1 << 1);
        if (Left) value |= (byte)(1 << 2);
        if (Right) value |= (byte)(1 << 3);
        if (Sprint) value |= (byte)(1 << 4);
        buffer[0] = value;
    }

    public static IBinaryMessage Deserialize(ReadOnlySpan<byte> buffer)
    {
        byte value = buffer[0];
        return new InputMessage
        {
            Up = (value & (1 << 0)) != 0,
            Down = (value & (1 << 1)) != 0,
            Left = (value & (1 << 2)) != 0,
            Right = (value & (1 << 3)) != 0,
            Sprint = (value & (1 << 4)) != 0
        };
    }
}