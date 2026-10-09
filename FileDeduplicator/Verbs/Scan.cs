// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator.Verbs;

using System.Collections.Generic;

using CommandLine;

using ktsu.Semantics.Paths;

[Verb("Scan", HelpText = "Scan a directory for duplicate files and display results.")]
internal sealed class Scan : BaseVerb<Scan>
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

	internal override void Run(Scan options)
	{
		Console.WriteLine($"Scanning: {options.Path}");
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

		// Step 4: Display results
		long totalWastedBytes = 0;
		bool anyDeletable = false;
		Console.WriteLine($"Found {duplicates.Count} group(s) of duplicate files:");
		Console.WriteLine();

		foreach (DuplicateGroup group in duplicates)
		{
			AbsoluteFilePath keeper = Deduplicator.SelectFileToKeep(group.Files);
			bool deletable = Deduplicator.IsDeletable(group);
			anyDeletable |= deletable;
			long wastedBytes = group.FileSize * (group.Files.Count - 1);
			totalWastedBytes += wastedBytes;

			Console.WriteLine($"  Hash: {group.Hash[..12]}... ({DuplicateReport.FormatBytes(group.FileSize)}, {group.Files.Count} copies)");

			foreach (AbsoluteFilePath file in group.Files)
			{
				string marker = (deletable, file == keeper) switch
				{
					(false, _) => " [KEEP, empty]",
					(true, true) => " [KEEP]",
					(true, false) => " [DELETE]",
				};
				Console.WriteLine($"    {file}{marker}");
			}

			Console.WriteLine();
		}

		Console.WriteLine($"Total duplicate groups: {duplicates.Count}");
		Console.WriteLine($"Total wasted space: {DuplicateReport.FormatBytes(totalWastedBytes)}");

		// Empty duplicates are kept, so pointing at Deduplicate when they are all there is would
		// send the user to a verb with nothing to do.
		if (anyDeletable)
		{
			Console.WriteLine();
			Console.WriteLine("Run the 'Deduplicate' command to remove duplicates.");
		}
	}
}
