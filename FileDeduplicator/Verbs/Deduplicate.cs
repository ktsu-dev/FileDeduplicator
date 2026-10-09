// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator.Verbs;

using System.Collections.Generic;

using CommandLine;

[Verb("Deduplicate", HelpText = "Scan a directory and remove duplicate files, keeping the copy with the shortest name.")]
internal sealed class Deduplicate : BaseVerb<Deduplicate>
{
	internal override bool ValidateArgs()
	{
		if (string.IsNullOrWhiteSpace(PathString))
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

		DuplicateScan? scan = DuplicateScan.Run(options.Path);
		if (scan is null)
		{
			return;
		}

		IReadOnlyList<DuplicateGroup> duplicates = scan.Duplicates;

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

		// Empty files are never deleted, so a tree whose only duplicates are empty marker files
		// (__init__.py, .gitkeep) has nothing to do. Asking to approve deleting zero files would
		// read stdin for nothing, and declining it would report failure to a script.
		if (plan.FileCount == 0)
		{
			Console.WriteLine("Nothing to delete (only empty-file duplicates found).");
			return;
		}

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
			ExitCode = 1;
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
			ExitCode = 1;
		}
	}
}
