// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator.Verbs;

using System.Collections.Generic;
using System.Linq;

using CommandLine;

using ktsu.Semantics.Paths;

[Verb("Stats", HelpText = "Show statistics about files and duplicates in a directory.")]
internal sealed class Stats : BaseVerb<Stats>
{
	internal override bool ValidateArgs()
	{
		if (string.IsNullOrWhiteSpace(PathString))
		{
			Console.Write("Enter the path to analyze: ");
			string? input = Console.ReadLine()?.Trim();
			if (string.IsNullOrEmpty(input))
			{
				Console.WriteLine("No path provided. Aborting.");
				return false;
			}

			PathString = input;
		}

		return base.ValidateArgs();
	}

	internal override void Run(Stats options)
	{
		Console.WriteLine($"Analyzing: {options.Path}");
		Console.WriteLine();

		// Step 1: Discover all files
		Console.WriteLine("Discovering files...");
		IReadOnlyList<AbsoluteFilePath> files = FileScanner.ScanForFiles(options.Path);
		Console.WriteLine($"Found {files.Count} file(s).");
		Console.WriteLine();

		if (files.Count == 0)
		{
			return;
		}

		// Step 2: Hash every file whose size another file shares
		Console.WriteLine("Hashing files...");
		Dictionary<AbsoluteFilePath, string> fileHashes = FileHasher.HashPossibleDuplicates(files, out IReadOnlyList<AbsoluteFilePath> uniqueSize);
		Console.WriteLine();

		// Step 3: Compute statistics
		Dictionary<string, List<AbsoluteFilePath>> hashGroups = Deduplicator.GroupByHash(fileHashes);
		IReadOnlyList<DuplicateGroup> allGroups = Deduplicator.FindDuplicates(hashGroups);

		// Empty files group together but are never deleted (Deduplicator.IsDeletable), so counting
		// them here reported dozens of __init__.py files as redundant copies that DryRun and
		// Deduplicate would keep. They are reported on their own line instead.
		DuplicateGroup[] duplicates = [.. allGroups.Where(Deduplicator.IsDeletable)];
		int emptyCopies = allGroups.Where(g => !Deduplicator.IsDeletable(g)).Sum(g => g.Files.Count - 1);

		// Every count below is taken over the files that hashed, plus those left unhashed because their
		// size is unique and so are unique files without being read. HashFiles drops a file it cannot
		// read, so counting against the scanned list would report each one as a duplicate of nothing,
		// and sizing it would throw if it had vanished since the scan.
		long totalSize = Deduplicator.TotalSize(fileHashes.Keys.Concat(uniqueSize));
		int uniqueFiles = hashGroups.Count + uniqueSize.Count;
		int duplicateFiles = fileHashes.Count - hashGroups.Count - emptyCopies;
		int unreadableFiles = files.Count - fileHashes.Count - uniqueSize.Count;

		Console.WriteLine("=== FileDeduplicator Statistics ===");
		Console.WriteLine();
		Console.WriteLine($"Total files: {files.Count}");
		if (unreadableFiles > 0)
		{
			Console.WriteLine($"Unreadable files: {unreadableFiles}");
		}

		Console.WriteLine($"Total size: {DuplicateReport.FormatBytes(totalSize)}");
		Console.WriteLine($"Unique files: {uniqueFiles}");
		Console.WriteLine($"Duplicate files: {duplicateFiles}");
		Console.WriteLine($"Duplicate groups: {duplicates.Length}");
		if (emptyCopies > 0)
		{
			Console.WriteLine($"Empty duplicates (never deleted): {emptyCopies}");
		}

		if (duplicates.Length > 0)
		{
			long wastedSpace = duplicates.Sum(g => g.FileSize * (g.Files.Count - 1));
			Console.WriteLine($"Wasted space: {DuplicateReport.FormatBytes(wastedSpace)}");
			Console.WriteLine();

			Dictionary<string, int> extensionCounts = CountRedundantCopiesByExtension(duplicates);

			Console.WriteLine("Duplicate files by extension:");
			foreach (KeyValuePair<string, int> kvp in extensionCounts.OrderByDescending(kvp => kvp.Value).ThenBy(kvp => kvp.Key, StringComparer.Ordinal))
			{
				Console.WriteLine($"  {kvp.Key}: {kvp.Value} file(s)");
			}

			Console.WriteLine();

			// Largest duplicate groups
			DuplicateGroup[] largestGroups = [.. duplicates.OrderByDescending(g => g.FileSize * (g.Files.Count - 1)).Take(5)];
			Console.WriteLine("Largest duplicate groups (by wasted space):");
			foreach (DuplicateGroup group in largestGroups)
			{
				long wasted = group.FileSize * (group.Files.Count - 1);
				Console.WriteLine($"  {group.Hash[..12]}... - {group.Files.Count} copies, {DuplicateReport.FormatBytes(group.FileSize)} each, {DuplicateReport.FormatBytes(wasted)} wasted");
			}
		}
	}

	/// <summary>
	/// Counts each redundant copy under its own extension, leaving out the copy that is kept, so the
	/// breakdown sums to "Duplicate files".
	/// </summary>
	/// <remarks>
	/// Crediting a whole group to <c>Files[0]</c> counted the keeper too, and picked whichever copy
	/// the parallel hash happened to list first, so the same tree reported a different extension
	/// from run to run.
	/// </remarks>
	/// <param name="duplicates">The duplicate groups to break down.</param>
	/// <returns>The number of redundant copies per extension.</returns>
	private static Dictionary<string, int> CountRedundantCopiesByExtension(IReadOnlyList<DuplicateGroup> duplicates)
	{
		Dictionary<string, int> extensionCounts = [];
		foreach (DuplicateGroup group in duplicates)
		{
			AbsoluteFilePath keeper = Deduplicator.SelectFileToKeep(group.Files);
			IEnumerable<string> extensions = group.Files
				.Where(f => f != keeper)
				// Camera and phone folders mix .JPG and .jpg, which are one kind of file.
				.Select(f => System.IO.Path.GetExtension(f.WeakString).ToLowerInvariant())
				.Select(ext => string.IsNullOrEmpty(ext) ? "(no extension)" : ext);

			foreach (string ext in extensions)
			{
				extensionCounts.TryGetValue(ext, out int count);
				extensionCounts[ext] = count + 1;
			}
		}

		return extensionCounts;
	}
}
