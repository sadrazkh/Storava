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
    /// <summary>Returns the first port on the list nothing else is holding, or null when all are taken.</summary>
    public static int? FirstAvailable()
    {
        foreach (int port in AgentEndpoints.Ports)
        {
            if (IsFree(port))
                return port;
        }

        return null;
    }

    private static bool IsFree(int port)
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
