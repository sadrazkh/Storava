# Installing the Storava companion Agent

The Agent runs on your own computer and gives the browser edition the file system a browser cannot
reach: real drives, whole-disk scans, and operating-system absolute paths. It talks to the Storava
page over loopback, never through the Storava server, so nothing about your disk leaves the
machine.

## What you download

Either an installer, `StoravaAgent.msi` (~224 MB), or the bare executable
`storava-agent.exe` (~170 MB) if you would rather not install anything.

The installer needs **no administrator rights**. It puts the Agent in
`%LOCALAPPDATA%\Programs\Storava`, adds a Start-menu shortcut, and appears in Windows' installed-
apps list so it can be removed the usual way.

Per-user is not a preference — see "Leaving it running" below for why it cannot be otherwise.

The MSI is larger than the executable it contains: the payload is already a compressed single
file, so the installer's own compression has nothing left to squeeze. Packaging the Agent as
hundreds of loose files instead would roughly third that, at the cost of an upgrade path that can
leave orphans behind. One file, one component, nothing stranded on uninstall seemed the better
trade for a tool whose whole subject is disk clutter.

Both are self-contained, so connecting a computer does not begin with "first install the .NET
runtime" — a prerequisite would put a second obstacle in front of the first. Neither is trimmed:
the Agent reflects over EF Core and its logging sinks, and a smaller build that starts fine and
fails at the first scan would be the worse bargain.

Nothing is written outside your own user profile either way.

## Connecting it to your account

1. Open Storava in your browser, sign in, and choose **Connect a computer** on your account page.
   A code appears; it lasts ten minutes and connects exactly one machine.
2. On the computer you want to connect, from a terminal:

   ```bash
   storava-agent pair --server https://your-storava-address
   ```

   Leave `--code` off and it asks for the code, which keeps it out of your shell history.

3. Compare the key fingerprint it prints with the one on your account page. They should match.

Pairing reads nothing and sends nothing about your files. The Agent learns which account it belongs
to; the server learns that an Agent exists.

## Leaving it running

Double-click `storava-agent.exe` and it appears in the notification area. The icon shows whether it
is listening; its menu opens Storava, stops the listener, disconnects the computer, and toggles
starting with Windows.

To have it start automatically:

```bash
storava-agent autostart --enable
```

This registers the Agent under your own user's startup entries — no administrator rights, and
nothing machine-wide.

That last point is not a convenience. The Agent's identity and channel secret are encrypted with
Windows DPAPI **scoped to your Windows account**. An Agent started as anyone else — a service
running as SYSTEM, another user's session — could not decrypt them and would be useless. Per-user
is the only place this can honestly live, which is also why the Agent is not a Windows Service.

## The first time the browser reaches it

Since Chrome 142, a browser asks your permission before a public site may connect to anything on
your local network, including your own machine. Allow it. The connection does not leave the
computer; the prompt is the browser being careful on your behalf, and the Storava page explains it
rather than reporting a bare network error.

## What it can and cannot do

It can list drives, walk any folder you name, and — for items the rule catalog permits — move a
folder to another drive or send one to the Recycle Bin, after you type that folder's own name to
confirm.

It cannot delete anything permanently. The interface underneath has no permanent-delete operation
at all, not even for a copy Storava made itself, so nothing reachable through the browser can
destroy data outright.

## Disconnecting

From the tray menu, **Disconnect this computer**, or:

```bash
storava-agent unpair
```

That clears the Agent's identity, its registration and its autostart entry. Remove the device on
your account page as well: that destroys the secret the browser's short-lived passes are signed
with, which is what makes the disconnection real rather than a flag.

Scans the Agent took stay on this computer, in `%LOCALAPPDATA%\Storava\Agent`. Delete that folder
to remove them.

## Where it keeps things

| | |
| --- | --- |
| `%LOCALAPPDATA%\Storava\Agent\secrets` | Identity and registration, DPAPI-encrypted for your account |
| `%LOCALAPPDATA%\Storava\Agent\agent-scans.db` | Scans the Agent has taken |
| `%LOCALAPPDATA%\Storava\Agent\logs` | Daily log, kept for a week |

## Building it yourself

```bash
dotnet publish src/Storava.Agent/Storava.Agent.csproj -p:PublishProfile=win-x64
dotnet build src/Storava.Agent.Installer/Storava.Agent.Installer.wixproj
```

The executable lands in `artifacts/agent/win-x64/` and the installer in `artifacts/installers/`.
The installer project is deliberately not in `Storava.slnx`: it packages the published output
rather than building it, so a plain solution build would try to make an MSI out of files that are
not there yet. It fails with that instruction if you forget.

CI builds both on every run and attaches them to the workflow, so a change that breaks packaging is
caught by the commit that made it. It also reads the per-user markers back out of the built MSI —
that failure is invisible until someone inspects the registry after installing.
