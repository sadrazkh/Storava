using System.Net;
using System.Net.Sockets;
using Storava.Contracts.Agent;

namespace Storava.Agent.Channel;

/// <summary>
/// Finds the port the Agent will listen on.
/// <para>
/// The page cannot be told which one it is — a browser cannot read a file — so both sides walk the
/// same short fixed list in the same order. Taking the first free one lets several Windows accounts
/// each run an Agent without configuration.
/// </para>
/// </summary>
public static class LoopbackPort
{
    /// <summary>
    /// Whether this process can take the port right now. Only a hint: the caller still has to
    /// cope with losing the race between this answer and its own bind.
    /// </summary>
    public static bool IsFree(int port)
    {
        // Bound and released rather than merely inspected: the answer that matters is whether this
        // process can take it, and a listing of the machine's sockets would not say that.
        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        finally
        {
            listener?.Stop();
            listener?.Dispose();
        }
    }
}
