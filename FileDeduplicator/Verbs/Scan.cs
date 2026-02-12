// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace ktsu.FileDeduplicator.Verbs;

using System.Collections.Generic;

using CommandLine;

using ktsu.Semantics.Paths;

[Verb("Scan", HelpText = "Scan a directory for duplicate files and display results.")]
internal sealed class Scan : BaseVerb<Scan>
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

	internal override void Run(Scan options)
	{
		Console.WriteLine($"Scanning: {options.Path}");
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

		// Step 4: Display results
		long totalWastedBytes = 0;
		Console.WriteLine($"Found {duplicates.Count} group(s) of duplicate files:");
		Console.WriteLine();

		foreach (DuplicateGroup group in duplicates)
		{
			AbsoluteFilePath keeper = Deduplicator.SelectFileToKeep(group.Files);
			long wastedBytes = group.FileSize * (group.Files.Count - 1);
			totalWastedBytes += wastedBytes;

			Console.WriteLine($"  Hash: {group.Hash[..12]}... ({FormatBytes(group.FileSize)}, {group.Files.Count} copies)");

			foreach (AbsoluteFilePath file in group.Files)
			{
				string marker = file == keeper ? " [KEEP]" : " [DELETE]";
				Console.WriteLine($"    {file}{marker}");
			}

			Console.WriteLine();
		}

		Console.WriteLine($"Total duplicate groups: {duplicates.Count}");
		Console.WriteLine($"Total wasted space: {FormatBytes(totalWastedBytes)}");
		Console.WriteLine();
		Console.WriteLine("Run the 'Deduplicate' command to remove duplicates.");

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
