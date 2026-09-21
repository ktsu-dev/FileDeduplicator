// Copyright (c) 2023-2026 ktsu-dev contributors

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

		DeletionPlan plan = DuplicateReport.PlanDeletions(duplicates);

		Console.WriteLine($"Found {duplicates.Count} group(s) of duplicate files:");
		Console.WriteLine();

		DuplicateReport.WriteListing(plan.Listing);

		Console.WriteLine("--- Dry Run Summary ---");
		Console.WriteLine($"Duplicate groups: {duplicates.Count}");
		Console.WriteLine($"Files to delete: {plan.FileCount}");
		Console.WriteLine($"Space to reclaim: {DuplicateReport.FormatBytes(plan.BytesReclaimable)}");

		PathString = ".";
	}
}
