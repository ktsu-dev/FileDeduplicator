// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator.Verbs;

using System.Collections.Generic;

using CommandLine;

[Verb("DryRun", HelpText = "Scan for duplicates and show what would be deleted without actually deleting.")]
internal sealed class DryRun : BaseVerb<DryRun>
{
	internal override bool ValidateArgs()
	{
		if (string.IsNullOrWhiteSpace(PathString))
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

		DeletionPlan plan = DuplicateReport.PlanDeletions(duplicates);

		Console.WriteLine($"Found {duplicates.Count} group(s) of duplicate files:");
		Console.WriteLine();

		DuplicateReport.WriteListing(plan.Listing);

		Console.WriteLine("--- Dry Run Summary ---");
		Console.WriteLine($"Duplicate groups: {duplicates.Count}");
		Console.WriteLine($"Files to delete: {plan.FileCount}");
		Console.WriteLine($"Space to reclaim: {DuplicateReport.FormatBytes(plan.BytesReclaimable)}");
	}
}
