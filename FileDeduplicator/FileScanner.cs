// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator;

using System.Collections.Generic;
using System.IO;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

internal static class FileScanner
{
	/// <summary>
	/// How the scan walks the tree.
	/// </summary>
	/// <remarks>
	/// Replaces <see cref="SearchOption.AllDirectories"/>, which resolves to the legacy-compatible
	/// options carrying <c>IgnoreInaccessible = false</c>. Enumeration is lazy, so the
	/// <see cref="UnauthorizedAccessException"/> from one unreadable subdirectory surfaced partway
	/// through the walk and abandoned the whole scan -- a restricted share or an OS-protected folder
	/// anywhere under the root was enough.
	/// <para>
	/// <see cref="FileAttributes.ReparsePoint"/> is skipped so a directory symlink pointing at one of
	/// its own ancestors cannot be descended into indefinitely. It skips linked files too, which
	/// suits a deduplicator: a symlink is not a second copy, so deleting one reclaims nothing and
	/// hashing through it would report content as duplicated with itself.
	/// </para>
	/// <para>
	/// <see cref="FileAttributes.Hidden"/> and <see cref="FileAttributes.System"/> are deliberately
	/// not skipped, which a bare <c>new EnumerationOptions()</c> would do. Those files were scanned
	/// before this change, and a duplicate among them is still a duplicate.
	/// </para>
	/// <para>
	/// No attribute marks a named pipe, socket or device node, so <see cref="ScanForFiles"/> filters
	/// those out itself through <see cref="FileType.IsRegularFile"/>.
	/// </para>
	/// </remarks>
	private static readonly EnumerationOptions WalkOptions = new()
	{
		RecurseSubdirectories = true,
		IgnoreInaccessible = true,
		AttributesToSkip = FileAttributes.ReparsePoint,
	};

	internal static IReadOnlyList<AbsoluteFilePath> ScanForFiles(AbsoluteDirectoryPath path)
	{
		_ = TryScanForFiles(path, out IReadOnlyList<AbsoluteFilePath> files);
		return files;
	}

	/// <summary>
	/// Lists the regular files under <paramref name="path"/>, telling a root that could not be scanned
	/// apart from one that holds no files.
	/// </summary>
	/// <param name="path">The directory to scan.</param>
	/// <param name="files">The files found, or none when the root could not be scanned.</param>
	/// <returns><see langword="false"/> when the root is missing or is not a directory.</returns>
	internal static bool TryScanForFiles(AbsoluteDirectoryPath path, out IReadOnlyList<AbsoluteFilePath> files)
	{
		files = [];

		// path.Exists is also true for a file, which Directory.EnumerateFiles then rejects with an
		// unhandled DirectoryNotFoundException. A path tab-completed one level too far is an easy
		// mistake, so it gets the same one-line error as a path that is not there at all.
		if (File.Exists(path.WeakString))
		{
			Console.WriteLine($"Not a directory: {path}");
			return false;
		}

		if (!Directory.Exists(path.WeakString))
		{
			Console.WriteLine($"Directory not found: {path}");
			return false;
		}

		List<AbsoluteFilePath> found = [];
		foreach (string file in Directory.EnumerateFiles(path.WeakString, "*", WalkOptions))
		{
			// On Unix the enumeration also yields named pipes, sockets and device nodes. Opening a
			// pipe to hash it blocks until a writer appears, and none of them is a copy of anything.
			if (!FileType.IsRegularFile(file))
			{
				continue;
			}

			found.Add(file.As<AbsoluteFilePath>());
		}

		files = found;
		return true;
	}
}
