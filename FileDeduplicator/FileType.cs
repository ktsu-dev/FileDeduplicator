// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator;

using System.Runtime.InteropServices;

/// <summary>
/// Tells regular files apart from the other entries a Unix directory can hold.
/// </summary>
/// <remarks>
/// On Unix, <see cref="Directory.EnumerateFiles(string, string, EnumerationOptions)"/> returns every
/// entry that is not a directory, so named pipes, sockets and device nodes arrive alongside
/// regular files. .NET reports all of them with <see cref="FileAttributes.Normal"/>, and exposes no
/// managed way to read the file type. Opening a named pipe for reading blocks until something
/// opens it for writing, which hung every verb; a device node such as <c>/dev/zero</c> never
/// reaches end of file.
/// <para>
/// The type is read through <c>SystemNative_LStat</c>, the shim the runtime itself uses to
/// implement <see cref="FileInfo"/> on Unix. Unlike <c>lstat(2)</c>, whose <c>struct stat</c> layout
/// differs between operating systems and architectures, the shim fills a structure the runtime
/// defines with fixed-width fields, and normalizes the file type bits to the same values on every
/// platform. Only the leading <c>Mode</c> field is read, and the buffer is oversized, so a field
/// the runtime appends later cannot overrun it.
/// </para>
/// </remarks>
internal static partial class FileType
{
	private const int TypeMask = 0xF000;
	private const int RegularFile = 0x8000;

	/// <summary>
	/// Gets whether the entry at <paramref name="path"/> is a regular file, the only kind whose
	/// content can be hashed and whose deletion reclaims space.
	/// </summary>
	/// <param name="path">The entry to inspect. A symlink is inspected, not followed.</param>
	/// <returns>
	/// <see langword="false"/> for a named pipe, socket or device node. <see langword="true"/> for a
	/// regular file, on Windows, and when the entry cannot be inspected, so that hashing it reports
	/// the problem as it did before.
	/// </returns>
	internal static bool IsRegularFile(string path)
	{
		if (OperatingSystem.IsWindows())
		{
			return true;
		}

		return LStat(path, out FileStatus status) != 0 || (status.Mode & TypeMask) == RegularFile;
	}

	[LibraryImport("libSystem.Native", EntryPoint = "SystemNative_LStat", StringMarshalling = StringMarshalling.Utf8)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
	private static partial int LStat(string path, out FileStatus output);

	/// <summary>
	/// The runtime's <c>FileStatus</c>, of which only <c>Mode</c> is read. It follows the 32-bit
	/// <c>Flags</c> field, and both have led the structure since the shim was introduced.
	/// </summary>
	[StructLayout(LayoutKind.Explicit, Size = 256)]
	private struct FileStatus
	{
		[FieldOffset(4)]
		internal int Mode;
	}
}
