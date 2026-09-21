// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator.Test;

using ktsu.Semantics.Paths;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Tests the listing and totals that Deduplicate and DryRun both print.
/// </summary>
/// <remarks>
/// Both verbs now read their numbers from here, so an error in this one place is an error the
/// preview and the confirmation would agree on -- which is exactly the kind of wrong that nobody
/// catches by reading the output.
/// </remarks>
[TestClass]
public sealed class DuplicateReportTests
{
	private static IReadOnlyList<DuplicateGroup> DuplicatesIn(TempTree tree) =>
		Deduplicator.FindDuplicates(Deduplicator.GroupByHash(FileHasher.HashFiles(FileScanner.ScanForFiles(tree.Root))));

	/// <summary>
	/// The plan counts every copy except each group's keeper, and the bytes those copies occupy.
	/// </summary>
	[TestMethod]
	public void PlanCountsEveryCopyExceptEachGroupsKeeper()
	{
		// Arrange -- 3 copies of a 5-byte content and 2 of a 4-byte one, so 2 + 1 deletions
		using TempTree tree = new();
		_ = tree.Write("a.txt", "alpha");
		_ = tree.Write("aa.txt", "alpha");
		_ = tree.Write("aaa.txt", "alpha");
		_ = tree.Write("b.txt", "beta");
		_ = tree.Write("bb.txt", "beta");
		_ = tree.Write("unique.txt", "gamma-and-then-some");

		// Act
		DeletionPlan plan = DuplicateReport.PlanDeletions(DuplicatesIn(tree));

		// Assert
		Assert.AreEqual(3, plan.FileCount);
		Assert.AreEqual((2 * 5L) + 4L, plan.BytesReclaimable);
	}

	/// <summary>
	/// The listing names one keeper and every other copy in each group, so the two sets together
	/// account for every file the group holds.
	/// </summary>
	[TestMethod]
	public void ListingNamesOneKeeperAndEveryOtherCopyPerGroup()
	{
		// Arrange
		using TempTree tree = new();
		_ = tree.Write("a.txt", "alpha");
		_ = tree.Write("aa.txt", "alpha");
		_ = tree.Write("nested/aaa.txt", "alpha");

		IReadOnlyList<DuplicateGroup> duplicates = DuplicatesIn(tree);

		// Act
		DeletionPlan plan = DuplicateReport.PlanDeletions(duplicates);
		List<string> keeps = [.. plan.Listing.Where(l => l.Contains("KEEP:", StringComparison.Ordinal))];
		List<string> deletes = [.. plan.Listing.Where(l => l.Contains("DELETE:", StringComparison.Ordinal))];

		// Assert
		Assert.HasCount(1, duplicates);
		Assert.HasCount(1, keeps);
		Assert.HasCount(2, deletes);

		AbsoluteFilePath keeper = Deduplicator.SelectFileToKeep(duplicates[0].Files);
		Assert.Contains(keeper.WeakString, keeps[0]);

		foreach (AbsoluteFilePath file in duplicates[0].Files.Where(f => f != keeper))
		{
			Assert.IsTrue(
				deletes.Exists(l => l.Contains(file.WeakString, StringComparison.Ordinal)),
				$"{file} was not listed for deletion.");
		}
	}

	/// <summary>
	/// Sizes are rendered in the largest unit that leaves the number above one, so a listing of
	/// large files does not ask the reader to count digits.
	/// </summary>
	/// <param name="bytes">The count to render.</param>
	/// <param name="expected">What it should read as.</param>
	[TestMethod]
	[DataRow(0L, "0 B")]
	[DataRow(1023L, "1023 B")]
	[DataRow(1024L, "1.0 KB")]
	[DataRow(1024L * 1024, "1.0 MB")]
	[DataRow((1024L * 1024 * 1024) + (512L * 1024 * 1024), "1.5 GB")]
	public void FormatBytesUsesTheLargestUnitThatFits(long bytes, string expected) =>
		Assert.AreEqual(expected, DuplicateReport.FormatBytes(bytes));

	/// <summary>
	/// A run with nothing to delete produces nothing to read.
	/// </summary>
	[TestMethod]
	public void PlanForNoDuplicatesIsEmpty()
	{
		// Act
		DeletionPlan plan = DuplicateReport.PlanDeletions([]);

		// Assert
		Assert.IsEmpty(plan.Listing);
		Assert.AreEqual(0, plan.FileCount);
		Assert.AreEqual(0L, plan.BytesReclaimable);
	}
}
