// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator;

using System.Collections.Generic;
using System.IO;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

internal static class FileScanner
{
	internal static IReadOnlyList<AbsoluteFilePath> ScanForFiles(AbsoluteDirectoryPath path)
	{
		if (!path.Exists)
		{
			Console.WriteLine($"Directory not found: {path}");
			return [];
		}

		// Walked one directory at a time rather than with SearchOption.AllDirectories, which
		// enumerates lazily: an UnauthorizedAccessException raised while descending into a single
		// unreadable subdirectory propagates out of the loop and abandons the whole scan, however
		// much of the tree was readable. Listing each directory on its own keeps a failure local to
		// the directory that caused it, and lets the scan say which one it gave up on.
		List<AbsoluteFilePath> files = [];
		Queue<string> pending = new();
		pending.Enqueue(path.WeakString);

		while (pending.Count > 0)
		{
			string directory = pending.Dequeue();

			foreach (string file in List(directory, Directory.EnumerateFiles))
			{
				files.Add(file.As<AbsoluteFilePath>());
			}

			foreach (string subdirectory in List(directory, Directory.EnumerateDirectories))
			{
				if (IsReparsePoint(subdirectory))
				{
					// A directory symlink or junction pointing at one of its own ancestors -- build
					// caches and some backup layouts produce these -- would otherwise be descended
					// into until the path stopped being legal. Skipping reparse points also stops
					// content reachable by two routes being scanned, and reported as duplicated,
					// twice.
					Console.WriteLine($"  Skipped link: {subdirectory}");
					continue;
				}

				pending.Enqueue(subdirectory);
			}
		}

		return files;
	}

	/// <summary>
	/// Lists one directory's entries, reporting and skipping it if it cannot be read.
	/// </summary>
	/// <remarks>
	/// The result is materialized inside the guard on purpose. Both enumerators are lazy, so a
	/// caller iterating one outside this method would see the exception raised on whichever element
	/// triggered it, not here.
	/// </remarks>
	private static List<string> List(string directory, Func<string, IEnumerable<string>> enumerate)
	{
		try
		{
			return [.. enumerate(directory)];
		}
		catch (UnauthorizedAccessException ex)
		{
			Console.WriteLine($"  Skipped {directory}: {ex.Message}");
			return [];
		}
		catch (IOException ex)
		{
			Console.WriteLine($"  Skipped {directory}: {ex.Message}");
			return [];
		}
	}

	private static bool IsReparsePoint(string directory)
	{
		try
		{
			return File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint);
		}
		catch (UnauthorizedAccessException)
		{
			// Unreadable attributes are not grounds for following the link; the listing guard above
			// reports the directory when the descent then fails to read it.
			return true;
		}
		catch (IOException)
		{
			return true;
		}
	}
}
