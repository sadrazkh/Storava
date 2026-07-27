using Serilog;

namespace Storava.Agent.Commands;

internal static class HelpCommand
{
    public static int Run()
    {
        Log.Information("""
            storava-agent — the Storava companion Agent

            The Agent runs on your computer and can see the file system a browser cannot. It is
            paired to your account so the Storava page in your browser knows it is yours. Your
            scans are not uploaded: the Agent talks to the browser on this machine, not through
            the Storava server.

            Commands
              pair --server <url> [--code <code>] [--name <label>]
                    Connect this computer. Generate the code on your account page.
                    Leave --code out and the agent asks for it, keeping it out of your shell history.

              tray  Run in the notification area. This is what a double-click and the logon task
                    do, and the normal way to leave the agent running: the icon shows whether it is
                    listening, and its menu can stop it or disconnect the computer.

              serve
                    Run in this terminal instead, printing as it goes. Useful for a look at what
                    the agent is doing. It listens on 127.0.0.1 only, answers just the one site it
                    is paired with, and requires a short-lived token that site gets from your
                    account.

              autostart [--enable | --disable]
                    Whether the agent starts when you sign in to Windows. Per-user, and no
                    administrator rights are involved.

              status
                    Show this installation's key fingerprint, whether it is paired, and whether it
                    starts at logon.

              unpair [--keep-identity]
                    Forget the pairing on this computer. Also remove the device on your account
                    page. --keep-identity keeps the key so the same machine can pair again.

              help  Show this text.
            """);
        return ExitCodes.Success;
    }

    public static int Unknown(string verb)
    {
        Log.Error("Unknown command '{Verb}'.", verb);
        Run();
        return ExitCodes.BadUsage;
    }
}
