// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator;

using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// What every verb works from: the files under a root, the hashes of those that might have a
/// duplicate, and the duplicate groups those hashes form.
/// </summary>
internal sealed class DuplicateScan
{
	private DuplicateScan(
		IReadOnlyList<AbsoluteFilePath> files,
		Dictionary<AbsoluteFilePath, string> fileHashes,
		IReadOnlyList<AbsoluteFilePath> uniqueSize,
		Dictionary<string, List<AbsoluteFilePath>> hashGroups)
	{
		Files = files;
		FileHashes = fileHashes;
		UniqueSize = uniqueSize;
		HashGroups = hashGroups;
		Duplicates = Deduplicator.FindDuplicates(hashGroups);
	}

	/// <summary>Gets every file found under the root.</summary>
	internal IReadOnlyList<AbsoluteFilePath> Files { get; }

	/// <summary>Gets the hash of every file that might have a duplicate and could be read.</summary>
	internal Dictionary<AbsoluteFilePath, string> FileHashes { get; }

	/// <summary>Gets the files left unhashed because no other file has their size.</summary>
	internal IReadOnlyList<AbsoluteFilePath> UniqueSize { get; }

	/// <summary>Gets the hashed files grouped by hash.</summary>
	internal Dictionary<string, List<AbsoluteFilePath>> HashGroups { get; }

	/// <summary>Gets the groups that still have at least two copies on disk.</summary>
	internal IReadOnlyList<DuplicateGroup> Duplicates { get; }

	/// <summary>
	/// Discovers the files under <paramref name="root"/> and hashes those whose size another file
	/// shares, reporting each step on the console.
	/// </summary>
	/// <param name="root">The directory to scan.</param>
	/// <returns>The scan, or <see langword="null"/> when the root holds no files.</returns>
	internal static DuplicateScan? Run(AbsoluteDirectoryPath root)
	{
		Console.WriteLine("Discovering files...");
		IReadOnlyList<AbsoluteFilePath> files = FileScanner.ScanForFiles(root);
		Console.WriteLine($"Found {files.Count} file(s).");
		Console.WriteLine();

		if (files.Count == 0)
		{
			return null;
		}

		Console.WriteLine("Hashing files...");
		Dictionary<AbsoluteFilePath, string> fileHashes = FileHasher.HashPossibleDuplicates(files, out IReadOnlyList<AbsoluteFilePath> uniqueSize);
		Console.WriteLine();

		return new DuplicateScan(files, fileHashes, uniqueSize, Deduplicator.GroupByHash(fileHashes));
	}
}
