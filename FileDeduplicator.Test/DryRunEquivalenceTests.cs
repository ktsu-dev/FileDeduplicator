// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator.Test;

using ktsu.Semantics.Paths;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Tests that the set of files DryRun says it would delete is exactly the set Deduplicate does
/// delete.
/// </summary>
/// <remarks>
/// DryRun is the safety net users rely on before letting this tool remove anything, so a
/// divergence between the two is the worst defect this codebase could have: the preview would be
/// a lie, and the thing it lied about is irreversible deletion.
///
/// The two verbs share <see cref="Deduplicator.GroupByHash"/>,
/// <see cref="Deduplicator.FindDuplicates"/> and <see cref="Deduplicator.SelectFileToKeep"/>, so
/// today they agree by construction. That is worth pinning rather than assuming: it holds only
/// while both keep calling the same three methods and while SelectFileToKeep stays deterministic,
/// and neither is enforced by anything but this test.
/// </remarks>
[TestClass]
public sealed class DryRunEquivalenceTests
{
	/// <summary>
	/// Computes the deletion set the way DryRun reports it -- every file in every duplicate group
	/// except that group's keeper.
	/// </summary>
	private static HashSet<AbsoluteFilePath> PredictedDeletions(IReadOnlyList<DuplicateGroup> duplicates)
	{
		HashSet<AbsoluteFilePath> predicted = [];

		foreach (DuplicateGroup group in duplicates)
		{
			AbsoluteFilePath keeper = Deduplicator.SelectFileToKeep(group.Files);
			foreach (AbsoluteFilePath file in group.Files.Where(f => f != keeper))
			{
				_ = predicted.Add(file);
			}
		}

		return predicted;
	}

	/// <summary>
	/// Across a tree with several duplicate groups, unique files and nested directories, the
	/// predicted deletion set must match the actual one exactly -- no file deleted that was not
	/// predicted, and none predicted that survived.
	/// </summary>
	[TestMethod]
	public void PredictedDeletionsMatchActualDeletionsExactly()
	{
		// Arrange -- three groups of duplicates plus two unique files, spread over nested folders
		using TempTree tree = new();
		List<AbsoluteFilePath> all =
		[
			tree.Write("group1/a.txt", "alpha"),
			tree.Write("group1/aa.txt", "alpha"),
			tree.Write("group1/nested/aaa.txt", "alpha"),
			tree.Write("group2/b.txt", "beta"),
			tree.Write("group2/bb.txt", "beta"),
			tree.Write("group3/deep/c.txt", "gamma"),
			tree.Write("group3/cc.txt", "gamma"),
			tree.Write("unique-one.txt", "delta"),
			tree.Write("nested/unique-two.txt", "epsilon"),
		];

		Dictionary<AbsoluteFilePath, string> hashes = FileHasher.HashFiles(all);
		IReadOnlyList<DuplicateGroup> duplicates = Deduplicator.FindDuplicates(Deduplicator.GroupByHash(hashes));
		HashSet<AbsoluteFilePath> predicted = PredictedDeletions(duplicates);

		// Act
		DeduplicationResult result = Deduplicator.DeleteDuplicates(duplicates);
		HashSet<AbsoluteFilePath> actuallyDeleted = [.. all.Where(f => !TempTree.Exists(f))];

		// Assert
		Assert.HasCount(3, duplicates, "Three groups of duplicates were created.");
		Assert.AreEqual(predicted.Count, result.DeletedCount);
		Assert.IsTrue(
			predicted.SetEquals(actuallyDeleted),
			$"Predicted [{string.Join(", ", predicted)}] but deleted [{string.Join(", ", actuallyDeleted)}].");
	}

	/// <summary>
	/// Every duplicate group must keep exactly one survivor. Deleting a whole group would destroy
	/// the content entirely, which is the failure mode that matters most here.
	/// </summary>
	[TestMethod]
	public void EveryDuplicateGroupRetainsExactlyOneSurvivor()
	{
		// Arrange
		using TempTree tree = new();
		List<AbsoluteFilePath> all =
		[
			tree.Write("a.txt", "alpha"),
			tree.Write("aa.txt", "alpha"),
			tree.Write("aaa.txt", "alpha"),
			tree.Write("b.txt", "beta"),
			tree.Write("bb.txt", "beta"),
		];

		Dictionary<AbsoluteFilePath, string> hashes = FileHasher.HashFiles(all);
		IReadOnlyList<DuplicateGroup> duplicates = Deduplicator.FindDuplicates(Deduplicator.GroupByHash(hashes));

		// Act
		_ = Deduplicator.DeleteDuplicates(duplicates);

		// Assert
		foreach (DuplicateGroup group in duplicates)
		{
			int survivors = group.Files.Count(TempTree.Exists);
			Assert.AreEqual(1, survivors, $"Group {group.Hash[..12]} should retain exactly one file.");
		}
	}

	/// <summary>
	/// Running the whole pipeline twice must be a no-op the second time: after deduplication there
	/// are no duplicates left to find.
	/// </summary>
	[TestMethod]
	public void DeduplicatingATreeTwiceDeletesNothingTheSecondTime()
	{
		// Arrange
		using TempTree tree = new();
		_ = tree.Write("a.txt", "alpha");
		_ = tree.Write("aa.txt", "alpha");
		_ = tree.Write("b.txt", "beta");

		// Act -- first pass
		IReadOnlyList<AbsoluteFilePath> firstScan = FileScanner.ScanForFiles(tree.Root);
		IReadOnlyList<DuplicateGroup> firstDuplicates =
			Deduplicator.FindDuplicates(Deduplicator.GroupByHash(FileHasher.HashFiles(firstScan)));
		DeduplicationResult first = Deduplicator.DeleteDuplicates(firstDuplicates);

		// Act -- second pass over the now-deduplicated tree
		IReadOnlyList<AbsoluteFilePath> secondScan = FileScanner.ScanForFiles(tree.Root);
		IReadOnlyList<DuplicateGroup> secondDuplicates =
			Deduplicator.FindDuplicates(Deduplicator.GroupByHash(FileHasher.HashFiles(secondScan)));
		DeduplicationResult second = Deduplicator.DeleteDuplicates(secondDuplicates);

		// Assert
		Assert.AreEqual(1, first.DeletedCount);
		Assert.IsEmpty(secondDuplicates);
		Assert.AreEqual(0, second.DeletedCount);
		Assert.HasCount(2, secondScan, "Both distinct contents should survive.");
	}
}
