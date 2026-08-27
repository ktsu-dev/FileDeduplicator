// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator.Test;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Tests for <see cref="FileScanner"/> and <see cref="FileHasher"/>, the two stages that decide
/// what the deduplicator sees.
/// </summary>
[TestClass]
public sealed class FileScannerAndHasherTests
{
	/// <summary>
	/// Scanning must recurse, since duplicates across nested folders are the main case.
	/// </summary>
	[TestMethod]
	public void ScanFindsFilesInNestedDirectories()
	{
		// Arrange
		using TempTree tree = new();
		_ = tree.Write("top.txt", "a");
		_ = tree.Write("one/mid.txt", "b");
		_ = tree.Write("one/two/three/deep.txt", "c");

		// Act
		IReadOnlyList<AbsoluteFilePath> files = FileScanner.ScanForFiles(tree.Root);

		// Assert
		Assert.HasCount(3, files);
	}

	/// <summary>
	/// An empty directory must yield no files rather than throwing.
	/// </summary>
	[TestMethod]
	public void ScanOfAnEmptyDirectoryReturnsNothing()
	{
		// Arrange
		using TempTree tree = new();

		// Act
		IReadOnlyList<AbsoluteFilePath> files = FileScanner.ScanForFiles(tree.Root);

		// Assert
		Assert.IsEmpty(files);
	}

	/// <summary>
	/// A path that does not exist must return an empty list rather than throwing, so a mistyped
	/// argument does not crash the tool.
	/// </summary>
	[TestMethod]
	public void ScanOfAMissingDirectoryReturnsNothing()
	{
		// Arrange
		using TempTree tree = new();
		AbsoluteDirectoryPath missing = Path.Combine(tree.Root.WeakString, "does-not-exist").As<AbsoluteDirectoryPath>();

		// Act
		IReadOnlyList<AbsoluteFilePath> files = FileScanner.ScanForFiles(missing);

		// Assert
		Assert.IsEmpty(files);
	}

	/// <summary>
	/// Identical content must hash identically regardless of the file's name or location.
	/// </summary>
	[TestMethod]
	public void IdenticalContentHashesIdenticallyRegardlessOfName()
	{
		// Arrange
		using TempTree tree = new();
		AbsoluteFilePath first = tree.Write("first.txt", "identical");
		AbsoluteFilePath second = tree.Write("nested/second-with-a-longer-name.txt", "identical");

		// Act
		string firstHash = FileHasher.ComputeHash(first);
		string secondHash = FileHasher.ComputeHash(second);

		// Assert
		Assert.AreEqual(firstHash, secondHash);
	}

	/// <summary>
	/// Differing content must hash differently, including a difference of a single character.
	/// </summary>
	[TestMethod]
	public void DifferingContentHashesDifferently()
	{
		// Arrange
		using TempTree tree = new();
		AbsoluteFilePath first = tree.Write("a.txt", "content");
		AbsoluteFilePath second = tree.Write("b.txt", "contenu");

		// Act & Assert
		Assert.AreNotEqual(FileHasher.ComputeHash(first), FileHasher.ComputeHash(second));
	}

	/// <summary>
	/// The hash is SHA-256 rendered as lowercase hex, which the console output slices to 12
	/// characters -- so it must be long enough for that slice and contain no uppercase.
	/// </summary>
	[TestMethod]
	public void HashIsLowercaseHexOfTheExpectedLength()
	{
		// Arrange
		using TempTree tree = new();
		AbsoluteFilePath file = tree.Write("a.txt", "content");

		// Act
		string hash = FileHasher.ComputeHash(file);

		// Assert
		Assert.HasCount(64, hash, "SHA-256 is 32 bytes, so 64 hex characters.");
		Assert.AreEqual(hash.ToLowerInvariant(), hash, "Callers slice this for display and compare it as-is.");
		Assert.IsTrue(hash.All(Uri.IsHexDigit), "Every character should be a hex digit.");
	}

	/// <summary>
	/// An empty file must hash to the well-known SHA-256 of zero bytes, rather than failing.
	/// </summary>
	[TestMethod]
	public void AnEmptyFileHashesToTheEmptySha256()
	{
		// Arrange
		using TempTree tree = new();
		AbsoluteFilePath empty = tree.Write("empty.txt", string.Empty);

		// Act
		string hash = FileHasher.ComputeHash(empty);

		// Assert
		Assert.AreEqual("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", hash);
	}

	/// <summary>
	/// The parallel hashing path must return one entry per input file, with the same values the
	/// single-file path produces.
	/// </summary>
	[TestMethod]
	public void ParallelHashingAgreesWithSingleFileHashing()
	{
		// Arrange
		using TempTree tree = new();
		List<AbsoluteFilePath> files =
		[
			tree.Write("a.txt", "one"),
			tree.Write("b.txt", "two"),
			tree.Write("c.txt", "one"),
			tree.Write("nested/d.txt", "three"),
		];

		// Act
		Dictionary<AbsoluteFilePath, string> hashes = FileHasher.HashFiles(files);

		// Assert
		Assert.HasCount(files.Count, hashes);
		foreach (AbsoluteFilePath file in files)
		{
			Assert.AreEqual(FileHasher.ComputeHash(file), hashes[file]);
		}
	}
}
