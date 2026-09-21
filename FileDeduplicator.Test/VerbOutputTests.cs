// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator.Test;

using ktsu.FileDeduplicator.Verbs;
using ktsu.Semantics.Paths;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Tests what the read-only verbs report, and that they stay read-only.
/// </summary>
/// <remarks>
/// Scan, DryRun and Stats exist to be read before anything is deleted, so what they print is their
/// entire contract -- and Deduplicate's listing is now built from the same helper DryRun uses, which
/// makes "DryRun still says what it used to" something worth holding still rather than assuming.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class VerbOutputTests
{
	/// <summary>
	/// Writes a tree with one group of three copies and one unique file.
	/// </summary>
	/// <param name="tree">The tree to write into.</param>
	/// <param name="keeper">The copy the shortest-name policy will keep.</param>
	/// <param name="doomed">The copies it will not.</param>
	private static void WriteOneGroup(TempTree tree, out AbsoluteFilePath keeper, out List<AbsoluteFilePath> doomed)
	{
		keeper = tree.Write("a.txt", "alpha");
		doomed =
		[
			tree.Write("aa.txt", "alpha"),
			tree.Write("nested/aaa.txt", "alpha"),
		];
		_ = tree.Write("unique.txt", "beta");
	}

	/// <summary>
	/// DryRun names the keeper and every other copy, totals them, and deletes nothing.
	/// </summary>
	[TestMethod]
	public void DryRunListsTheGroupAndLeavesEveryFileOnDisk()
	{
		// Arrange
		using TempTree tree = new();
		WriteOneGroup(tree, out AbsoluteFilePath keeper, out List<AbsoluteFilePath> doomed);

		// Act
		string output = ConsoleCapture.Normalize(ConsoleCapture.Run(new DryRun { PathString = tree.Root.WeakString }));

		// Assert
		Assert.Contains("Found 1 group(s) of duplicate files:", output);
		Assert.Contains($"KEEP: {keeper}", output);
		Assert.Contains("--- Dry Run Summary ---", output);
		Assert.Contains("Duplicate groups: 1", output);
		Assert.Contains("Files to delete: 2", output);
		Assert.Contains("Space to reclaim: 10 B", output);

		foreach (AbsoluteFilePath file in doomed)
		{
			Assert.Contains($"DELETE: {file}", output);
			Assert.IsTrue(TempTree.Exists(file), $"DryRun deleted {file}.");
		}

		Assert.IsTrue(TempTree.Exists(keeper), $"DryRun deleted {keeper}.");
	}

	/// <summary>
	/// Scan marks each copy in a group, and points at the verb that acts on them.
	/// </summary>
	[TestMethod]
	public void ScanMarksEveryCopyKeepOrDelete()
	{
		// Arrange
		using TempTree tree = new();
		WriteOneGroup(tree, out AbsoluteFilePath keeper, out List<AbsoluteFilePath> doomed);

		// Act
		string output = ConsoleCapture.Normalize(ConsoleCapture.Run(new Scan { PathString = tree.Root.WeakString }));

		// Assert
		Assert.Contains($"{keeper} [KEEP]", output);
		Assert.Contains("Total duplicate groups: 1", output);
		Assert.Contains("Total wasted space: 10 B", output);
		Assert.Contains("Run the 'Deduplicate' command to remove duplicates.", output);

		foreach (AbsoluteFilePath file in doomed)
		{
			Assert.Contains($"{file} [DELETE]", output);
			Assert.IsTrue(TempTree.Exists(file), $"Scan deleted {file}.");
		}
	}

	/// <summary>
	/// Stats counts the tree and breaks the duplicates down without touching anything.
	/// </summary>
	[TestMethod]
	public void StatsReportsTotalsForTheTree()
	{
		// Arrange
		using TempTree tree = new();
		WriteOneGroup(tree, out AbsoluteFilePath keeper, out List<AbsoluteFilePath> doomed);

		// Act
		string output = ConsoleCapture.Normalize(ConsoleCapture.Run(new Stats { PathString = tree.Root.WeakString }));

		// Assert -- three 5-byte copies plus a 4-byte unique file, so 19 B held and 10 B wasted
		Assert.Contains("Total files: 4", output);
		Assert.Contains("Total size: 19 B", output);
		Assert.Contains("Unique files: 2", output);
		Assert.Contains("Duplicate files: 2", output);
		Assert.Contains("Duplicate groups: 1", output);
		Assert.Contains("Wasted space: 10 B", output);
		Assert.Contains("Duplicate files by extension:", output);
		Assert.Contains(".txt: 3 file(s)", output);
		Assert.Contains("Largest duplicate groups (by wasted space):", output);
		Assert.Contains("3 copies, 5 B each, 10 B wasted", output);

		Assert.IsTrue(TempTree.Exists(keeper), $"Stats deleted {keeper}.");
		Assert.IsTrue(doomed.TrueForAll(TempTree.Exists), "Stats deleted a file.");
	}

	/// <summary>
	/// A tree with nothing duplicated says so, in every verb that looks for duplicates.
	/// </summary>
	[TestMethod]
	public void NoDuplicatesIsReportedByEveryVerb()
	{
		// Arrange
		using TempTree tree = new();
		_ = tree.Write("a.txt", "alpha");
		_ = tree.Write("b.txt", "beta");

		// Act
		string scan = ConsoleCapture.Run(new Scan { PathString = tree.Root.WeakString });
		string dryRun = ConsoleCapture.Run(new DryRun { PathString = tree.Root.WeakString });
		string deduplicate = ConsoleCapture.Run(new Deduplicate { PathString = tree.Root.WeakString }, "y");

		// Assert
		Assert.Contains("No duplicate files found.", scan);
		Assert.Contains("No duplicate files found.", dryRun);
		Assert.Contains("No duplicate files found.", deduplicate);
		Assert.DoesNotContain("Proceed with deletion?", deduplicate, "Nothing was found, so nothing should have been asked.");
	}

	/// <summary>
	/// An empty directory stops before hashing, rather than reporting on nothing.
	/// </summary>
	[TestMethod]
	public void AnEmptyDirectoryStopsAfterDiscovery()
	{
		// Arrange
		using TempTree tree = new();

		// Act
		string output = ConsoleCapture.Run(new Scan { PathString = tree.Root.WeakString });

		// Assert
		Assert.Contains("Found 0 file(s).", output);
		Assert.DoesNotContain("Hashing files...", output);
	}
}
