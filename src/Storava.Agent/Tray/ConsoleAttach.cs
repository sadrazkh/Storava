using System.Runtime.InteropServices;

namespace Storava.Agent.Tray;

/// <summary>
/// Lets one executable be both a tray application and a command-line tool.
/// <para>
/// The Agent is built as a <c>WinExe</c> so that starting at logon does not flash a console
/// window. That also means it has no console of its own, so <c>status</c> run from a terminal
/// would print into nothing. Attaching to the terminal that launched it puts the output back
/// where the user is looking, without ever creating a window of its own.
/// </para>
/// </summary>
internal static class ConsoleAttach
{
    private const int AttachParentProcess = -1;

    /// <summary>
    /// Reconnects stdout and stderr to the launching terminal, if there is one. Returns false
    /// when the process was started from Explorer or the scheduler, where there is nothing to
    /// attach to and nothing should be printed.
    /// </summary>
    public static bool ToParentTerminal()
    {
        if (!AttachConsole(AttachParentProcess))
            return false;

        try
        {
            var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            Console.SetOut(stdout);

            var stderr = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
            Console.SetError(stderr);

            // The shell already printed its prompt before handing control back, so output would
            // otherwise begin halfway along that line.
            Console.WriteLine();
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// The shell returns its prompt the moment a WinExe starts, so anything printed afterwards
    /// lands beneath it. A trailing newline keeps the next prompt on its own line.
    /// </summary>
    public static void ReleaseTerminal()
    {
        try
        {
            Console.Out.Flush();
            Console.WriteLine();
        }
        catch (IOException)
        {
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);
}
