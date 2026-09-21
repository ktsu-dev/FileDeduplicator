// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator.Verbs;

using System.Collections.Generic;

using CommandLine;

using ktsu.Semantics.Paths;

[Verb("Deduplicate", HelpText = "Scan a directory and remove duplicate files, keeping the copy with the shortest name.")]
internal sealed class Deduplicate : BaseVerb<Deduplicate>
{
	internal override bool ValidateArgs()
	{
		if (PathString is "." or "")
		{
			Console.Write("Enter the path to deduplicate: ");
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

	internal override void Run(Deduplicate options)
	{
		Console.WriteLine($"Deduplicating: {options.Path}");
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

		// Step 4: Show exactly which copies go and which one stays. "Shortest filename wins" is
		// a blunt enough policy -- report-final-DO-NOT-DELETE.pdf loses to r.pdf -- that seeing
		// the paths is the only way to catch a bad outcome while it is still reversible. Scan
		// and DryRun already print this; the verb that actually deletes is the one that needs it.
		DeletionPlan plan = DuplicateReport.PlanDeletions(duplicates);

		Console.WriteLine($"Found {duplicates.Count} group(s) of duplicate files.");
		Console.WriteLine("Keeping the copy with the shortest filename in each group.");
		Console.WriteLine();

		DuplicateReport.WriteListing(plan.Listing);

		Console.WriteLine($"Files to delete: {plan.FileCount}");
		Console.WriteLine($"Space to reclaim: {DuplicateReport.FormatBytes(plan.BytesReclaimable)}");
		Console.WriteLine();

		// Step 5: Confirm with user
		Console.Write("Proceed with deletion? (y/N): ");
		string? confirmation = Console.ReadLine()?.Trim();
		if (!string.Equals(confirmation, "y", StringComparison.OrdinalIgnoreCase))
		{
			Console.WriteLine("Aborted.");
			return;
		}

		Console.WriteLine();

		// Step 6: Delete duplicates
		Console.WriteLine("Deleting duplicates...");
		DeduplicationResult result = Deduplicator.DeleteDuplicates(duplicates);
		Console.WriteLine();

		Console.WriteLine($"Deleted {result.DeletedCount} file(s).");
		Console.WriteLine($"Reclaimed {DuplicateReport.FormatBytes(result.BytesReclaimed)} of disk space.");

		if (result.SkippedFiles.Count > 0)
		{
			Console.WriteLine($"Preserved {result.SkippedFiles.Count} file(s) that could no longer be confirmed as duplicates:");

			foreach (SkippedFile skipped in result.SkippedFiles)
			{
				Console.WriteLine($"  {skipped.Path} -- {skipped.Reason}");
			}
		}

		if (result.Errors.Count > 0)
		{
			Console.WriteLine($"Encountered {result.Errors.Count} error(s) during deletion.");
		}

		PathString = ".";
	}
}
