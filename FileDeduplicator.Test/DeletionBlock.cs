// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator.Test;

using ktsu.Semantics.Paths;

/// <summary>
/// Makes a file refuse to be deleted for the lifetime of the block, and puts the permissions back
/// on disposal.
/// </summary>
/// <remarks>
/// The two platforms refuse for different reasons. Windows will not unlink a file carrying
/// <see cref="FileAttributes.ReadOnly"/>. Unix ignores that bit when deleting -- unlink is governed
/// by write permission on the containing directory -- so the directory is what gets write-protected
/// there. Both surface as <see cref="UnauthorizedAccessException"/> out of
/// <see cref="File.Delete(string)"/>, which is the failure the delete path has to survive.
/// </remarks>
internal sealed class DeletionBlock : IDisposable
{
	private readonly string file;
	private readonly string directory;
	private readonly string probe;
	private readonly UnixFileMode originalDirectoryMode;

	/// <summary>
	/// Gets whether the block actually holds. A process running as root deletes through
	/// write-protected directories regardless, so the staged failure never happens and a test
	/// relying on it has nothing to observe.
	/// </summary>
	internal bool IsEnforced { get; }

	/// <summary>
	/// Blocks deletion of a file, then measures whether the block took effect.
	/// </summary>
	/// <param name="target">The file to protect.</param>
	internal DeletionBlock(AbsoluteFilePath target)
	{
		file = target.WeakString;
		directory = Path.GetDirectoryName(file)!;
		probe = Path.Combine(directory, $"{Path.GetFileName(file)}.deletion-probe");

		// Written before the block goes on, because on Unix the block closes the directory to new
		// files as well as to deletions.
		File.WriteAllText(probe, "probe");

		originalDirectoryMode = OperatingSystem.IsWindows() ? default : File.GetUnixFileMode(directory);
		Block();
		IsEnforced = ProbeRefusesDeletion();
	}

	private void Block()
	{
		if (OperatingSystem.IsWindows())
		{
			File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);
			File.SetAttributes(probe, File.GetAttributes(probe) | FileAttributes.ReadOnly);
		}
		else
		{
			File.SetUnixFileMode(directory, originalDirectoryMode & ~(UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite));
		}
	}

	private void Unblock()
	{
		if (OperatingSystem.IsWindows())
		{
			ClearReadOnly(file);
			ClearReadOnly(probe);
		}
		else
		{
			File.SetUnixFileMode(directory, originalDirectoryMode);
		}
	}

	private static void ClearReadOnly(string path)
	{
		if (File.Exists(path))
		{
			File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
		}
	}

	/// <summary>
	/// Spends a throwaway file to find out whether this process is actually refused, rather than
	/// guessing from the platform and the user id.
	/// </summary>
	/// <returns><see langword="true"/> if deleting the probe was refused.</returns>
	private bool ProbeRefusesDeletion()
	{
		try
		{
			File.Delete(probe);
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return true;
		}
	}

	/// <inheritdoc />
	public void Dispose()
	{
		Unblock();

		if (File.Exists(probe))
		{
			File.Delete(probe);
		}
	}
}
