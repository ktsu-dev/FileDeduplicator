// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace ktsu.FileDeduplicator.Verbs;

using System.Collections.Generic;

using CommandLine;

using ktsu.Semantics.Paths;

[Verb("DryRun", HelpText = "Scan for duplicates and show what would be deleted without actually deleting.")]
internal sealed class DryRun : BaseVerb<DryRun>
{
	internal override bool ValidateArgs()
	{
		if (PathString is "." or "")
		{
			Console.Write("Enter the path to scan: ");
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

	internal override void Run(DryRun options)
	{
		Console.WriteLine($"Dry run for: {options.Path}");
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

		// Step 2: Hash all files in parallel
		Console.WriteLine("Hashing files...");
		Dictionary<AbsoluteFilePath, string> fileHashes = FileHasher.HashFiles(files);
		Console.WriteLine();

		// Step 3: Group by hash and find duplicates
		Dictionary<string, List<AbsoluteFilePath>> hashGroups = Deduplicator.GroupByHash(fileHashes);
		IReadOnlyList<DuplicateGroup> duplicates = Deduplicator.FindDuplicates(hashGroups);

		if (duplicates.Count == 0)
		{
			Console.WriteLine("No duplicate files found.");
			return;
		}

		long totalReclaimable = 0;
		int totalDeletions = 0;

		Console.WriteLine($"Found {duplicates.Count} group(s) of duplicate files:");
		Console.WriteLine();

		foreach (DuplicateGroup group in duplicates)
		{
			AbsoluteFilePath keeper = Deduplicator.SelectFileToKeep(group.Files);

			Console.WriteLine($"  Hash: {group.Hash[..12]}... ({FormatBytes(group.FileSize)}, {group.Files.Count} copies)");
			Console.WriteLine($"    KEEP:   {keeper}");

			foreach (AbsoluteFilePath file in group.Files)
			{
				if (file == keeper)
				{
					continue;
				}

				Console.WriteLine($"    DELETE: {file}");
				totalDeletions++;
				totalReclaimable += group.FileSize;
			}

			Console.WriteLine();
		}

		Console.WriteLine("--- Dry Run Summary ---");
		Console.WriteLine($"Duplicate groups: {duplicates.Count}");
		Console.WriteLine($"Files to delete: {totalDeletions}");
		Console.WriteLine($"Space to reclaim: {FormatBytes(totalReclaimable)}");

		PathString = ".";
	}

	private static string FormatBytes(long bytes) => bytes switch
	{
		< 1024L => $"{bytes} B",
		< 1024L * 1024 => $"{bytes / 1024.0:F1} KB",
		< 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F1} MB",
		_ => $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB",
	};
}
