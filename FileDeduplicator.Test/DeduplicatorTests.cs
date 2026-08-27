// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator.Test;

using ktsu.Semantics.Paths;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Tests for <see cref="Deduplicator"/>, the type that decides which files are deleted.
/// </summary>
[TestClass]
public sealed class DeduplicatorTests
{
	private static Dictionary<AbsoluteFilePath, string> Hash(TempTree tree, params string[] relativePaths) =>
		FileHasher.HashFiles([.. relativePaths.Select(p => tree.Write(p, "same content"))]);

	private static IReadOnlyList<DuplicateGroup> Duplicates(Dictionary<AbsoluteFilePath, string> hashes) =>
		Deduplicator.FindDuplicates(Deduplicator.GroupByHash(hashes));

	/// <summary>
	/// Identical content must group together regardless of filename or directory.
	/// </summary>
	[TestMethod]
	public void IdenticalContentGroupsTogetherAcrossDirectories()
	{
		// Arrange
		using TempTree tree = new();
		Dictionary<AbsoluteFilePath, string> hashes = Hash(tree, "a.txt", "nested/b.txt", "nested/deep/c.txt");

		// Act
		IReadOnlyList<DuplicateGroup> duplicates = Duplicates(hashes);

		// Assert
		Assert.ContainsSingle(duplicates);
		Assert.HasCount(3, duplicates[0].Files);
	}

	/// <summary>
	/// Distinct content must not be grouped, so nothing is proposed for deletion.
	/// </summary>
	[TestMethod]
	public void DistinctContentProducesNoDuplicateGroups()
	{
		// Arrange
		using TempTree tree = new();
		Dictionary<AbsoluteFilePath, string> hashes = FileHasher.HashFiles(
		[
			tree.Write("a.txt", "one"),
			tree.Write("b.txt", "two"),
			tree.Write("c.txt", "three"),
		]);

		// Act
		IReadOnlyList<DuplicateGroup> duplicates = Duplicates(hashes);

		// Assert
		Assert.IsEmpty(duplicates);
	}

	/// <summary>
	/// A file with no twin must never be offered for deletion.
	/// </summary>
	[TestMethod]
	public void ASingleCopyIsNeverADuplicate()
	{
		// Arrange
		using TempTree tree = new();
		AbsoluteFilePath lonely = tree.Write("lonely.txt", "unique");
		Dictionary<AbsoluteFilePath, string> hashes = FileHasher.HashFiles(
		[
			lonely,
			tree.Write("dup-a.txt", "shared"),
			tree.Write("dup-bb.txt", "shared"),
		]);

		// Act
		IReadOnlyList<DuplicateGroup> duplicates = Duplicates(hashes);

		// Assert
		Assert.ContainsSingle(duplicates);
		Assert.IsFalse(duplicates[0].Files.Contains(lonely), "A file with unique content must not appear in a duplicate group.");
	}

	/// <summary>
	/// Empty files hash identically to each other and must be treated as duplicates.
	/// </summary>
	[TestMethod]
	public void EmptyFilesAreDuplicatesOfEachOther()
	{
		// Arrange
		using TempTree tree = new();
		Dictionary<AbsoluteFilePath, string> hashes = FileHasher.HashFiles(
		[
			tree.Write("empty-a.txt", string.Empty),
			tree.Write("empty-b.txt", string.Empty),
		]);

		// Act
		IReadOnlyList<DuplicateGroup> duplicates = Duplicates(hashes);

		// Assert
		Assert.ContainsSingle(duplicates);
		Assert.AreEqual(0, duplicates[0].FileSize);
	}

	/// <summary>
	/// The documented rule is "keep the copy with the shortest filename".
	/// </summary>
	[TestMethod]
	public void TheShortestFileNameIsKept()
	{
		// Arrange
		using TempTree tree = new();
		AbsoluteFilePath shortest = tree.Write("a.txt", "x");
		List<AbsoluteFilePath> group =
		[
			tree.Write("aaaa.txt", "x"),
			shortest,
			tree.Write("aa.txt", "x"),
		];

		// Act
		AbsoluteFilePath keeper = Deduplicator.SelectFileToKeep(group);

		// Assert
		Assert.AreEqual(shortest, keeper);
	}

	/// <summary>
	/// Only the file name length is compared, not the length of the whole path -- a deeply nested
	/// file with a short name still wins over a shallow one with a long name.
	/// </summary>
	[TestMethod]
	public void OnlyTheFileNameLengthDecidesNotThePathLength()
	{
		// Arrange
		using TempTree tree = new();
		AbsoluteFilePath deepButShortName = tree.Write("one/two/three/four/a.txt", "x");
		List<AbsoluteFilePath> group =
		[
			tree.Write("bbbbbbbb.txt", "x"),
			deepButShortName,
		];

		// Act
		AbsoluteFilePath keeper = Deduplicator.SelectFileToKeep(group);

		// Assert
		Assert.AreEqual(deepButShortName, keeper);
	}

	/// <summary>
	/// Ties on name length must break deterministically, or which copy survives would depend on
	/// enumeration order and differ between runs and platforms.
	/// </summary>
	[TestMethod]
	public void TiesOnNameLengthBreakDeterministicallyByPath()
	{
		// Arrange
		using TempTree tree = new();
		AbsoluteFilePath a = tree.Write("aa.txt", "x");
		AbsoluteFilePath b = tree.Write("bb.txt", "x");
		AbsoluteFilePath c = tree.Write("cc.txt", "x");

		// Act -- the same set in three different orders
		AbsoluteFilePath first = Deduplicator.SelectFileToKeep([a, b, c]);
		AbsoluteFilePath second = Deduplicator.SelectFileToKeep([c, b, a]);
		AbsoluteFilePath third = Deduplicator.SelectFileToKeep([b, c, a]);

		// Assert
		Assert.AreEqual(first, second);
		Assert.AreEqual(second, third);
		Assert.AreEqual(a, first, "The ordinally-first path should win a tie on name length.");
	}

	/// <summary>
	/// Deletion must remove every copy except the keeper, and must not touch anything else.
	/// </summary>
	[TestMethod]
	public void DeleteDuplicatesRemovesEveryCopyExceptTheKeeper()
	{
		// Arrange
		using TempTree tree = new();
		AbsoluteFilePath unrelated = tree.Write("unrelated.txt", "different content");
		Dictionary<AbsoluteFilePath, string> hashes = FileHasher.HashFiles(
		[
			unrelated,
			tree.Write("a.txt", "shared"),
			tree.Write("bb.txt", "shared"),
			tree.Write("ccc.txt", "shared"),
		]);
		IReadOnlyList<DuplicateGroup> duplicates = Duplicates(hashes);
		AbsoluteFilePath keeper = Deduplicator.SelectFileToKeep(duplicates[0].Files);

		// Act
		DeduplicationResult result = Deduplicator.DeleteDuplicates(duplicates);

		// Assert
		Assert.AreEqual(2, result.DeletedCount);
		Assert.IsEmpty(result.Errors);
		Assert.IsTrue(TempTree.Exists(keeper), "The keeper must survive.");
		Assert.IsTrue(TempTree.Exists(unrelated), "A file outside any duplicate group must be untouched.");

		foreach (AbsoluteFilePath file in duplicates[0].Files.Where(f => f != keeper))
		{
			Assert.IsFalse(TempTree.Exists(file), $"{file} should have been deleted.");
		}
	}

	/// <summary>
	/// The reclaimed byte count must reflect what was actually removed.
	/// </summary>
	[TestMethod]
	public void ReclaimedBytesCountsOnlyTheDeletedCopies()
	{
		// Arrange
		using TempTree tree = new();
		string content = new('x', 100);
		Dictionary<AbsoluteFilePath, string> hashes = FileHasher.HashFiles(
		[
			tree.Write("a.txt", content),
			tree.Write("bb.txt", content),
			tree.Write("ccc.txt", content),
		]);
		IReadOnlyList<DuplicateGroup> duplicates = Duplicates(hashes);

		// Act
		DeduplicationResult result = Deduplicator.DeleteDuplicates(duplicates);

		// Assert -- three copies, two deleted, 100 bytes apiece
		Assert.AreEqual(2, result.DeletedCount);
		Assert.AreEqual(200, result.BytesReclaimed);
	}

	/// <summary>
	/// Deleting nothing must report nothing, rather than throwing on an empty group list.
	/// </summary>
	[TestMethod]
	public void DeletingAnEmptyGroupListIsANoOp()
	{
		// Act
		DeduplicationResult result = Deduplicator.DeleteDuplicates([]);

		// Assert
		Assert.AreEqual(0, result.DeletedCount);
		Assert.AreEqual(0, result.BytesReclaimed);
		Assert.IsEmpty(result.Errors);
	}
}
