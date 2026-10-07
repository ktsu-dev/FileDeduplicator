// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator;

using System.IO;
using System.Runtime.InteropServices;

using Microsoft.Win32.SafeHandles;

/// <summary>
/// Identifies the file a path leads to, as opposed to the path itself.
/// </summary>
/// <remarks>
/// Two paths can lead to one file: a hardlink, a Linux bind mount, a share mounted twice, or a macOS
/// directory hardlink. None of these is a reparse point, so the scan walks into both paths and both
/// hash the same. Deleting one of them as the "other copy" then deletes the copy being kept, or, for
/// a hardlink, removes a name while reclaiming nothing. The file system's own identity for a file is
/// the device and the file's index on it: <c>(st_dev, st_ino)</c> on Unix, and
/// <c>(VolumeSerialNumber, FileIndexHigh/Low)</c> on Windows.
/// </remarks>
/// <param name="Device">The device or volume the file lives on.</param>
/// <param name="Index">The file's index (inode number) on that device.</param>
internal readonly partial record struct FileIdentity(ulong Device, ulong Index)
{
	/// <summary>
	/// Reads the identity of the file at <paramref name="path"/>.
	/// </summary>
	/// <param name="path">The file to identify.</param>
	/// <param name="identity">The file's identity, when it could be read.</param>
	/// <returns><see langword="false"/> when the file is gone or cannot be inspected.</returns>
	internal static bool TryGet(string path, out FileIdentity identity) =>
		OperatingSystem.IsWindows() ? TryGetWindows(path, out identity) : TryGetUnix(path, out identity);

	/// <summary>
	/// Reads the identity through <c>SystemNative_Stat</c>, the same runtime shim <see cref="FileType"/>
	/// uses, for the same reason: its structure has fixed-width fields on every Unix.
	/// </summary>
	private static bool TryGetUnix(string path, out FileIdentity identity)
	{
		if (Stat(path, out FileStatus status) != 0)
		{
			identity = default;
			return false;
		}

		identity = new FileIdentity(unchecked((ulong)status.Dev), unchecked((ulong)status.Ino));
		return true;
	}

	private static bool TryGetWindows(string path, out FileIdentity identity)
	{
		try
		{
			using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

			if (!GetFileInformationByHandle(handle, out ByHandleFileInformation info))
			{
				identity = default;
				return false;
			}

			identity = new FileIdentity(info.VolumeSerialNumber, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow);
			return true;
		}
		catch (IOException)
		{
			identity = default;
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			identity = default;
			return false;
		}
	}

	[LibraryImport("libSystem.Native", EntryPoint = "SystemNative_Stat", StringMarshalling = StringMarshalling.Utf8)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
	private static partial int Stat(string path, out FileStatus output);

	[LibraryImport("kernel32.dll", SetLastError = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation information);

	/// <summary>
	/// The runtime's <c>FileStatus</c>, of which only <c>Dev</c> and <c>Ino</c> are read. They follow
	/// the flags, mode, owner, size and five timestamp pairs, all fixed-width. The buffer is oversized,
	/// as in <see cref="FileType"/>, so a field the runtime appends later cannot overrun it.
	/// </summary>
	[StructLayout(LayoutKind.Explicit, Size = 256)]
	private struct FileStatus
	{
		[FieldOffset(88)]
		internal long Dev;

		[FieldOffset(104)]
		internal long Ino;
	}

	/// <summary>
	/// <c>BY_HANDLE_FILE_INFORMATION</c>, of which only the volume serial number and file index are read.
	/// </summary>
	[StructLayout(LayoutKind.Explicit, Size = 52)]
	private struct ByHandleFileInformation
	{
		[FieldOffset(28)]
		internal uint VolumeSerialNumber;

		[FieldOffset(44)]
		internal uint FileIndexHigh;

		[FieldOffset(48)]
		internal uint FileIndexLow;
	}
}
