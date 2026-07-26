using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Storava.Platform.Storage;

/// <summary>
/// Win32 entry points used to recycle a folder and to create a junction. Kept in one place so the
/// unsafe surface of the whole application is a single, reviewable file.
/// </summary>
internal static partial class NativeMethods
{
    // --- SHFileOperation: the Recycle Bin ---

    internal const uint FoDelete = 0x0003;
    internal const ushort FofSilent = 0x0004;
    internal const ushort FofNoConfirmation = 0x0010;
    internal const ushort FofAllowUndo = 0x0040;          // the flag that makes this recoverable
    internal const ushort FofNoConfirmMkDir = 0x0200;
    internal const ushort FofNoErrorUi = 0x0400;

    // Default packing, not Pack = 1: SHFILEOPSTRUCT is a normally-aligned struct, and forcing byte
    // packing makes the shell read the fields at the wrong offsets and write past the end of it
    // (an access violation, not a managed exception).
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct ShFileOpStruct
    {
        public IntPtr Wnd;
        public uint Func;

        /// <summary>Double-null-terminated list of source paths.</summary>
        [MarshalAs(UnmanagedType.LPWStr)] public string From;

        [MarshalAs(UnmanagedType.LPWStr)] public string? To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool AnyOperationsAborted;
        public IntPtr NameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? ProgressTitle;
    }

    // DllImport rather than LibraryImport throughout: the source generator emits unsafe code, and
    // enabling AllowUnsafeBlocks for the whole platform layer is a bigger concession than the
    // marshalling costs here are worth.
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int SHFileOperation(ref ShFileOpStruct fileOp);

    // --- Junctions ---

    internal const uint GenericWrite = 0x40000000;
    internal const uint FileFlagBackupSemantics = 0x02000000;   // required to open a directory
    internal const uint FileFlagOpenReparsePoint = 0x00200000;  // open the link, not its target
    internal const uint FsctlSetReparsePoint = 0x000900A4;
    internal const uint IoReparseTagMountPoint = 0xA0000003;

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        FileShare shareMode,
        IntPtr securityAttributes,
        FileMode creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint controlCode,
        byte[] inBuffer,
        int inBufferSize,
        IntPtr outBuffer,
        int outBufferSize,
        out int bytesReturned,
        IntPtr overlapped);
}
