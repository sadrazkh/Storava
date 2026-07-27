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

              serve
                    Run the agent so the Storava page in your browser can reach it. It listens on
                    127.0.0.1 only, answers just the one site it is paired with, and requires a
                    short-lived token that site gets from your account.

              status
                    Show this installation's key fingerprint and whether it is paired.

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
