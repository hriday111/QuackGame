using System.Buffers;
using System.Net.Sockets;
using System.Text;
using Quack.Messages;

namespace Quack.Client;

public class NetworkConnection
{
    private const int MaxPayloadSize = 64 * 1024;
    private TcpClient Client { get; }
    private NetworkStream Stream { get; }
    private bool _disconnecting;
  
    public bool IsConnected => Client.Connected;
    
    public event EventHandler<IMessage>? MessageReceived;
    public event EventHandler? Disconnected;

    public NetworkConnection(TcpClient client)
    {
        Client = client;
        Stream = Client.GetStream();
        Client.NoDelay = true;
    }

    public async Task StartReadingAsync(CancellationToken token = default)
    {
        try
        {
            byte[] headerBuffer = new byte[5];
            while (!token.IsCancellationRequested && IsConnected)
            {
                int headerRead = await Stream.ReadAsync(headerBuffer, token);
                if (headerRead == 0) break; // Connection closed

                if (headerRead != 5) throw new IOException("Incomplete header read");

                MessageType type = (MessageType)headerBuffer[4];
                int payloadSize = BitConverter.ToInt32(headerBuffer, 0);

                if (payloadSize < 0 || payloadSize > MaxPayloadSize) throw new IOException("Invalid payload size");

                byte[] payloadBuffer = ArrayPool<byte>.Shared.Rent(payloadSize);
                try
                {
                    int payloadRead = await Stream.ReadAsync(payloadBuffer.AsMemory(0, payloadSize), token);
                    if (payloadRead != payloadSize) throw new IOException("Incomplete payload read");

                    IMessage message;
                    if (type == MessageType.Welcome || type == MessageType.UpdateState || type == MessageType.Disconnected)
                    {
                        string json = Encoding.UTF8.GetString(payloadBuffer, 0, payloadSize);
                        message = IJsonMessage.Deserialize(type, json);
                    }
                    else
                    {
                        message = IBinaryMessage.Deserialize(type, payloadBuffer.AsSpan(0, payloadSize));
                    }
                    MessageReceived?.Invoke(this, message);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(payloadBuffer);
                }
            }
        }
        catch (IOException ioEx) when (ioEx.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionReset })
        {
            Console.WriteLine($"Client connection reset: {ioEx.Message}");
        }
        catch (Exception ex) when (ex is EndOfStreamException or OperationCanceledException or ObjectDisposedException)
        {
            // Expected disconnection or cancellation
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Network read error: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            Disconnect();
        }
    }
    
    public async Task SendAsync(IBinaryMessage message, CancellationToken token = default)
    {
        if (!IsConnected || _disconnecting) return;
        try
        {
            int payloadSize = message.GetSize();
            byte[] buffer = ArrayPool<byte>.Shared.Rent(5 + payloadSize);
            try
            {
                BitConverter.TryWriteBytes(buffer.AsSpan(0), payloadSize);
                buffer[4] = (byte)message.Type;
                message.Serialize(buffer.AsSpan(5));
                await Stream.WriteAsync(buffer.AsMemory(0, 5 + payloadSize), token);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (IOException ioEx) when (ioEx.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionReset })
        {
            Console.WriteLine($"Network send failed (connection reset): {ioEx}");
            Disconnect();
        }
        catch (ObjectDisposedException)
        {
            Console.WriteLine("Network send failed (object disposed).");
            Disconnect();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Network send error: {ex}");
            Disconnect();
        }
    }
    
    public void Disconnect()
    {
        if (Interlocked.Exchange(ref _disconnecting, true)) return;

        try
        {
            if (Client.Connected)
            {
                Client.Client.Shutdown(SocketShutdown.Both);
                Client.Close();
            }
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during disconnect: {ex.Message}");
        }
        finally
        {
            Client.Dispose();
        }
    }
}