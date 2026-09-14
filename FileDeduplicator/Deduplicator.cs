// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator;

using System.Collections.Generic;
using System.IO;
using System.Linq;

using ktsu.Semantics.Paths;

internal static class Deduplicator
{
	internal static Dictionary<string, List<AbsoluteFilePath>> GroupByHash(Dictionary<AbsoluteFilePath, string> fileHashes)
	{
		Dictionary<string, List<AbsoluteFilePath>> groups = [];

		foreach (KeyValuePair<AbsoluteFilePath, string> kvp in fileHashes)
		{
			if (!groups.TryGetValue(kvp.Value, out List<AbsoluteFilePath>? paths))
			{
				paths = [];
				groups[kvp.Value] = paths;
			}

			paths.Add(kvp.Key);
		}

		return groups;
	}

	internal static IReadOnlyList<DuplicateGroup> FindDuplicates(Dictionary<string, List<AbsoluteFilePath>> hashGroups) =>
		[.. hashGroups
			.Where(kvp => kvp.Value.Count > 1)
			.Select(kvp => new DuplicateGroup(kvp.Key, kvp.Value))];

	internal static AbsoluteFilePath SelectFileToKeep(List<AbsoluteFilePath> duplicates) =>
		duplicates.OrderBy(f => f.FileName.WeakString.Length).ThenBy(f => f.WeakString, StringComparer.Ordinal).First();

	internal static DeduplicationResult DeleteDuplicates(IReadOnlyList<DuplicateGroup> duplicateGroups)
	{
		int deletedCount = 0;
		long bytesReclaimed = 0;
		List<string> errors = [];
		List<SkippedFile> skipped = [];

		foreach (DuplicateGroup group in duplicateGroups)
		{
			AbsoluteFilePath keeper = SelectFileToKeep(group.Files);

			// The grouping was computed before the confirmation prompt, which is an interactive
			// pause of unbounded length. If the copy being kept no longer holds the group's
			// content, deleting the others would destroy the only remaining copies of it, so the
			// whole group is left alone.
			if (!StillMatchesGroup(keeper, group.Hash, out string? keeperReason))
			{
				SkipWholeGroup(group, keeper, keeperReason, skipped);
				continue;
			}

			foreach (AbsoluteFilePath file in group.Files.Where(f => f != keeper))
			{
				// Re-hash immediately before deleting: a file that changed since the scan is no
				// longer a duplicate, and deleting it would be irreversible loss of content that
				// exists nowhere else.
				if (!StillMatchesGroup(file, group.Hash, out string? reason))
				{
					Skip(file, reason, skipped);
					continue;
				}

				try
				{
					long fileSize = new FileInfo(file.WeakString).Length;
					File.Delete(file.WeakString);
					deletedCount++;
					bytesReclaimed += fileSize;
					Console.WriteLine($"  Deleted: {file}");
				}
				catch (IOException ex)
				{
					string error = $"  Error deleting {file}: {ex.Message}";
					errors.Add(error);
					Console.WriteLine(error);
				}
			}
		}

		return new DeduplicationResult(deletedCount, bytesReclaimed, errors, skipped);
	}

	/// <summary>
	/// Preserves every copy in a group, because the copy that would have been kept no longer
	/// holds the group's content.
	/// </summary>
	/// <param name="group">The group to leave on disk.</param>
	/// <param name="keeper">The copy that would have been kept.</param>
	/// <param name="keeperReason">What the keeper did, phrased to follow its name.</param>
	/// <param name="skipped">The list to record the preserved files on.</param>
	private static void SkipWholeGroup(DuplicateGroup group, AbsoluteFilePath keeper, string? keeperReason, List<SkippedFile> skipped)
	{
		foreach (AbsoluteFilePath file in group.Files.Where(f => f != keeper))
		{
			Skip(file, $"the copy being kept ({keeper}) {keeperReason}", skipped);
		}
	}

	/// <summary>
	/// Re-reads a file and reports whether it still holds the content its duplicate group was
	/// formed from.
	/// </summary>
	/// <param name="file">The file to re-hash.</param>
	/// <param name="groupHash">The hash the group was formed from.</param>
	/// <param name="reason">When the answer is no, why -- phrased to follow the file's name.</param>
	/// <returns><see langword="true"/> if the file still hashes to <paramref name="groupHash"/>.</returns>
	private static bool StillMatchesGroup(AbsoluteFilePath file, string groupHash, out string? reason)
	{
		try
		{
			if (string.Equals(FileHasher.ComputeHash(file), groupHash, StringComparison.Ordinal))
			{
				reason = null;
				return true;
			}

			reason = "changed since it was scanned, so it is no longer a duplicate";
			return false;
		}
		catch (IOException ex)
		{
			reason = $"could not be re-read to confirm it is still a duplicate: {ex.Message}";
			return false;
		}
		catch (UnauthorizedAccessException ex)
		{
			reason = $"could not be re-read to confirm it is still a duplicate: {ex.Message}";
			return false;
		}
	}

	private static void Skip(AbsoluteFilePath file, string? reason, List<SkippedFile> skipped)
	{
		SkippedFile skip = new(file, reason ?? "could not be confirmed as a duplicate");
		skipped.Add(skip);
		Console.WriteLine($"  Skipped: {file} -- {skip.Reason}");
	}
}

internal sealed class DuplicateGroup(string hash, List<AbsoluteFilePath> files)
{
	internal string Hash { get; } = hash;
	internal List<AbsoluteFilePath> Files { get; } = files;
	internal long FileSize { get; } = new FileInfo(files[0].WeakString).Length;
}

internal sealed class DeduplicationResult(int deletedCount, long bytesReclaimed, List<string> errors, List<SkippedFile> skippedFiles)
{
	internal int DeletedCount { get; } = deletedCount;
	internal long BytesReclaimed { get; } = bytesReclaimed;
	internal IReadOnlyList<string> Errors { get; } = errors;

	/// <summary>
	/// Gets the files that were proposed for deletion but left on disk because they could no
	/// longer be confirmed as duplicates.
	/// </summary>
	internal IReadOnlyList<SkippedFile> SkippedFiles { get; } = skippedFiles;
}

/// <summary>
/// A file that was preserved instead of deleted, and why.
/// </summary>
internal sealed class SkippedFile(AbsoluteFilePath path, string reason)
{
	internal AbsoluteFilePath Path { get; } = path;
	internal string Reason { get; } = reason;
}
