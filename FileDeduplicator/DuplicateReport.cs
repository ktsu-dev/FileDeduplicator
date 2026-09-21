// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator;

using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// Renders what deduplication would do to a set of duplicate groups, without doing any of it.
/// </summary>
/// <remarks>
/// Every verb that talks about duplicates has to answer the same two questions -- which copy
/// survives each group, and which copies do not -- and answer them identically, because the user
/// reads one verb's answer and then acts on another's. Building the listing here, from the same
/// <see cref="Deduplicator.SelectFileToKeep"/> the delete path uses, is what keeps the preview and
/// the deletion from drifting apart.
/// </remarks>
internal static class DuplicateReport
{
	/// <summary>
	/// Works out which copies would be deleted, and renders the per-group KEEP/DELETE listing.
	/// </summary>
	/// <param name="duplicates">The duplicate groups to describe.</param>
	/// <returns>The listing, plus the totals that go with it.</returns>
	internal static DeletionPlan PlanDeletions(IReadOnlyList<DuplicateGroup> duplicates)
	{
		List<string> listing = [];
		int fileCount = 0;
		long bytesReclaimable = 0;

		foreach (DuplicateGroup group in duplicates)
		{
			AbsoluteFilePath keeper = Deduplicator.SelectFileToKeep(group.Files);

			listing.Add($"  Hash: {group.Hash[..12]}... ({FormatBytes(group.FileSize)}, {group.Files.Count} copies)");
			listing.Add($"    KEEP:   {keeper}");

			foreach (AbsoluteFilePath file in group.Files)
			{
				if (file == keeper)
				{
					continue;
				}

				listing.Add($"    DELETE: {file}");
				fileCount++;
				bytesReclaimable += group.FileSize;
			}

			listing.Add(string.Empty);
		}

		return new DeletionPlan(listing, fileCount, bytesReclaimable);
	}

	/// <summary>
	/// Writes a listing produced by <see cref="PlanDeletions"/> to the console.
	/// </summary>
	/// <param name="listing">The lines to write.</param>
	internal static void WriteListing(IReadOnlyList<string> listing)
	{
		foreach (string line in listing)
		{
			Console.WriteLine(line);
		}
	}

	/// <summary>
	/// Renders a byte count in the largest unit that leaves it above one.
	/// </summary>
	/// <param name="bytes">The count to render.</param>
	/// <returns>The rendered size.</returns>
	internal static string FormatBytes(long bytes) => bytes switch
	{
		< 1024L => $"{bytes} B",
		< 1024L * 1024 => $"{bytes / 1024.0:F1} KB",
		< 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F1} MB",
		_ => $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB",
	};
}

/// <summary>
/// What deduplication would delete, described but not yet done.
/// </summary>
/// <param name="listing">The per-group KEEP/DELETE lines, ready to print.</param>
/// <param name="fileCount">How many copies would be deleted.</param>
/// <param name="bytesReclaimable">How many bytes deleting them would free.</param>
internal sealed class DeletionPlan(IReadOnlyList<string> listing, int fileCount, long bytesReclaimable)
{
	/// <summary>
	/// Gets the per-group KEEP/DELETE lines, in the order they should be printed.
	/// </summary>
	internal IReadOnlyList<string> Listing { get; } = listing;

	/// <summary>
	/// Gets the number of copies that would be deleted.
	/// </summary>
	internal int FileCount { get; } = fileCount;

	/// <summary>
	/// Gets the number of bytes deleting them would free.
	/// </summary>
	internal long BytesReclaimable { get; } = bytesReclaimable;
}
