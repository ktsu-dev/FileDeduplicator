// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator;

using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;

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

	/// <summary>
	/// Version-control metadata directories the walk never descends into.
	/// </summary>
	/// <remarks>
	/// Inside <c>.git</c>, every branch ref pointing at the same commit has identical content, and so
	/// do the reflogs of branches created together. Treating those as duplicates deleted branches to
	/// reclaim a few hundred bytes (ktsu-dev/FileDeduplicator#192). Matched on the directory's own
	/// name, ignoring case, so a file whose path merely contains ".git" is still scanned, and so is a
	/// <c>.git</c> <em>file</em> (the pointer a worktree or submodule leaves), which is not a directory.
	/// </remarks>
	private static readonly HashSet<string> VersionControlDirectories = new(StringComparer.OrdinalIgnoreCase)
	{
		".git",
		".svn",
		".hg",
		".bzr",
	};

	/// <summary>
	/// Checks that <paramref name="path"/> is a directory that can be scanned, and says why in one
	/// line when it is not.
	/// </summary>
	/// <param name="path">The root to check.</param>
	/// <returns><see langword="false"/> when the root is missing or is not a directory.</returns>
	internal static bool IsScannableDirectory(AbsoluteDirectoryPath path)
	{
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

		return true;
	}

	internal static IReadOnlyList<AbsoluteFilePath> ScanForFiles(AbsoluteDirectoryPath path)
	{
		if (!IsScannableDirectory(path))
		{
			return [];
		}

		FileSystemEnumerable<string> walk = new(
			path.WeakString,
			(ref entry) => entry.ToFullPath(),
			WalkOptions)
		{
			ShouldIncludePredicate = (ref entry) => !entry.IsDirectory,
			ShouldRecursePredicate = (ref entry) => !VersionControlDirectories.Contains(entry.FileName.ToString()),
		};

		List<AbsoluteFilePath> files = [];
		foreach (string file in walk)
		{
			// On Unix the enumeration also yields named pipes, sockets and device nodes. Opening a
			// pipe to hash it blocks until a writer appears, and none of them is a copy of anything.
			if (!FileType.IsRegularFile(file))
			{
				continue;
			}

			// ktsu.Semantics.Paths rejects some names the file system accepts: '<', '>' and '|' are
			// legal on Unix, and paths over 256 characters are routine under node_modules or deep
			// build output. One such file must not abort the scan of the whole tree, so it is
			// reported and skipped, the same way FileHasher handles a file it cannot read.
			try
			{
				files.Add(file.As<AbsoluteFilePath>());
			}
			catch (ArgumentException ex)
			{
				Console.WriteLine($"  Skipped {file}: {ex.Message}");
			}
		}

		return files;
	}
}
