// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator.Test;

/// <summary>
/// Makes a directory refuse to be listed for the lifetime of the block, and puts the permissions
/// back on disposal.
/// </summary>
/// <remarks>
/// The Unix arrangement is to drop read and execute on the directory, which makes
/// <see cref="Directory.EnumerateFiles(string)"/> throw <see cref="UnauthorizedAccessException"/> --
/// the failure a scan descending into someone else's folder, or an OS-protected one, has to
/// survive. Windows refuses a listing only through an ACL deny entry rather than a file attribute,
/// so nothing is staged there and <see cref="IsEnforced"/> reports false, the same way it does for a
/// privileged process.
/// </remarks>
internal sealed class DirectoryReadBlock : IDisposable
{
	private readonly string directory;
	private readonly UnixFileMode originalMode;

	/// <summary>
	/// Gets whether the block actually holds. A process running as root lists unreadable
	/// directories regardless, so the staged failure never happens and a test relying on it has
	/// nothing to observe.
	/// </summary>
	internal bool IsEnforced { get; }

	/// <summary>
	/// Blocks listing of a directory, then measures whether the block took effect.
	/// </summary>
	/// <param name="target">The directory to protect.</param>
	internal DirectoryReadBlock(string target)
	{
		directory = target;

		if (OperatingSystem.IsWindows())
		{
			IsEnforced = false;
			return;
		}

		originalMode = File.GetUnixFileMode(directory);
		File.SetUnixFileMode(directory, UnixFileMode.None);
		IsEnforced = ListingIsRefused();
	}

	/// <summary>
	/// Tries the listing the block is meant to prevent, rather than guessing from the platform and
	/// the user id.
	/// </summary>
	/// <returns><see langword="true"/> if listing the directory was refused.</returns>
	private bool ListingIsRefused()
	{
		try
		{
			// Materialized, because the enumerator is lazy and raises nothing until it is walked.
			_ = Directory.EnumerateFileSystemEntries(directory).ToList();
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
		if (!OperatingSystem.IsWindows())
		{
			File.SetUnixFileMode(directory, originalMode);
		}
	}
}
