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
	/// </remarks>
	private static readonly EnumerationOptions WalkOptions = new()
	{
		RecurseSubdirectories = true,
		IgnoreInaccessible = true,
		AttributesToSkip = FileAttributes.ReparsePoint,
	};

	internal static IReadOnlyList<AbsoluteFilePath> ScanForFiles(AbsoluteDirectoryPath path)
	{
		if (!path.Exists)
		{
			Console.WriteLine($"Directory not found: {path}");
			return [];
		}

		List<AbsoluteFilePath> files = [];
		foreach (string file in Directory.EnumerateFiles(path.WeakString, "*", WalkOptions))
		{
			files.Add(file.As<AbsoluteFilePath>());
		}

		return files;
	}
}
