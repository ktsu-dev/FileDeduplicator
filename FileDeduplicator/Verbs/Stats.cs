// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

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
		if (PathString is "." or "")
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

		// Step 2: Hash all files
		Console.WriteLine("Hashing files...");
		Dictionary<AbsoluteFilePath, string> fileHashes = FileHasher.HashFiles(files);
		Console.WriteLine();

		// Step 3: Compute statistics
		Dictionary<string, List<AbsoluteFilePath>> hashGroups = Deduplicator.GroupByHash(fileHashes);
		IReadOnlyList<DuplicateGroup> duplicates = Deduplicator.FindDuplicates(hashGroups);

		long totalSize = files.Sum(f => new FileInfo(f.WeakString).Length);
		int uniqueFiles = hashGroups.Count;
		int duplicateFiles = files.Count - uniqueFiles;

		Console.WriteLine("=== FileDeduplicator Statistics ===");
		Console.WriteLine();
		Console.WriteLine($"Total files: {files.Count}");
		Console.WriteLine($"Total size: {FormatBytes(totalSize)}");
		Console.WriteLine($"Unique files: {uniqueFiles}");
		Console.WriteLine($"Duplicate files: {duplicateFiles}");
		Console.WriteLine($"Duplicate groups: {duplicates.Count}");

		if (duplicates.Count > 0)
		{
			long wastedSpace = duplicates.Sum(g => g.FileSize * (g.Files.Count - 1));
			Console.WriteLine($"Wasted space: {FormatBytes(wastedSpace)}");
			Console.WriteLine();

			// Extension breakdown
			Dictionary<string, int> extensionCounts = [];
			foreach (DuplicateGroup group in duplicates)
			{
				string ext = System.IO.Path.GetExtension(group.Files[0].WeakString);
				if (string.IsNullOrEmpty(ext))
				{
					ext = "(no extension)";
				}

				extensionCounts.TryGetValue(ext, out int count);
				extensionCounts[ext] = count + group.Files.Count;
			}

			Console.WriteLine("Duplicate files by extension:");
			foreach (KeyValuePair<string, int> kvp in extensionCounts.OrderByDescending(kvp => kvp.Value))
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
				Console.WriteLine($"  {group.Hash[..12]}... - {group.Files.Count} copies, {FormatBytes(group.FileSize)} each, {FormatBytes(wasted)} wasted");
			}
		}

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
