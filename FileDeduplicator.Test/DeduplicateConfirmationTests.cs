// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator.Test;

using ktsu.FileDeduplicator.Verbs;
using ktsu.Semantics.Paths;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Tests that the Deduplicate verb shows which copies it is about to delete before it asks for
/// permission to delete them, and that the answer it gets is the one it acts on.
/// </summary>
/// <remarks>
/// The confirmation prompt is the only gate in front of an irreversible deletion, and "keep the
/// shortest filename" is a blunt enough policy to pick the wrong survivor -- r.pdf over
/// report-final-DO-NOT-DELETE.pdf. A prompt that states only a count asks the user to approve an
/// outcome they cannot see, which makes the gate decorative. These tests pin the ordering: the
/// paths come first, the question second.
///
/// They drive the verb through <see cref="BaseVerb.Run()"/> with the console redirected, because
/// the ordering is a property of that method and of nothing else -- a unit test of the listing
/// helper alone would still pass if the call were left out or moved below the prompt.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class DeduplicateConfirmationTests
{
	private static string RunDeclining(AbsoluteDirectoryPath root) =>
		ConsoleCapture.Normalize(ConsoleCapture.Run(new Deduplicate { PathString = root.WeakString }, "n"));

	private static string RunConfirming(AbsoluteDirectoryPath root) =>
		ConsoleCapture.Normalize(ConsoleCapture.Run(new Deduplicate { PathString = root.WeakString }, "y"));

	private static string BeforeThePrompt(string output)
	{
		int prompt = output.IndexOf("Proceed with deletion?", StringComparison.Ordinal);
		Assert.AreNotEqual(-1, prompt, $"The verb never reached the confirmation prompt. Output was:\n{output}");
		return output[..prompt];
	}

	/// <summary>
	/// Every copy that would be deleted has to be named before the prompt, not after the deletion.
	/// </summary>
	[TestMethod]
	public void EveryPathToBeDeletedIsNamedBeforeTheConfirmationPrompt()
	{
		// Arrange -- the blunt-policy case: the descriptive name loses to the short one
		using TempTree tree = new();
		AbsoluteFilePath keeper = tree.Write("r.pdf", "the only copy that survives");
		AbsoluteFilePath doomed = tree.Write("report-final-DO-NOT-DELETE.pdf", "the only copy that survives");
		AbsoluteFilePath alsoDoomed = tree.Write("archive/report-2026-backup.pdf", "the only copy that survives");

		// Act
		string beforePrompt = BeforeThePrompt(RunDeclining(tree.Root));

		// Assert
		Assert.Contains($"DELETE: {doomed}", beforePrompt, $"The prompt was shown without naming {doomed}. Output before it was:\n{beforePrompt}");
		Assert.Contains($"DELETE: {alsoDoomed}", beforePrompt, $"The prompt was shown without naming {alsoDoomed}. Output before it was:\n{beforePrompt}");
		Assert.Contains($"KEEP: {keeper}", beforePrompt, $"The prompt was shown without naming the copy being kept. Output before it was:\n{beforePrompt}");
	}

	/// <summary>
	/// The listing has to cover every group, not just the first one -- a user scrolling past a
	/// truncated listing would approve deletions they never saw.
	/// </summary>
	[TestMethod]
	public void EveryDuplicateGroupAppearsInTheListing()
	{
		// Arrange
		using TempTree tree = new();
		List<AbsoluteFilePath> doomed =
		[
			tree.Write("group1/aa.txt", "alpha"),
			tree.Write("group2/bb.txt", "beta"),
			tree.Write("group3/nested/cc.txt", "gamma"),
		];
		_ = tree.Write("group1/a.txt", "alpha");
		_ = tree.Write("group2/b.txt", "beta");
		_ = tree.Write("group3/c.txt", "gamma");

		// Act
		string beforePrompt = BeforeThePrompt(RunDeclining(tree.Root));

		// Assert
		foreach (AbsoluteFilePath file in doomed)
		{
			Assert.Contains($"DELETE: {file}", beforePrompt, $"{file} was not listed before the prompt.");
		}
	}

	/// <summary>
	/// The totals printed with the listing describe the same deletions the listing names.
	/// </summary>
	[TestMethod]
	public void TheTotalsMatchTheListingShownAboveThem()
	{
		// Arrange -- two groups of two 5-byte files, so 2 deletions and 10 bytes
		using TempTree tree = new();
		_ = tree.Write("a.txt", "alpha");
		_ = tree.Write("aa.txt", "alpha");
		_ = tree.Write("b.txt", "bravo");
		_ = tree.Write("bb.txt", "bravo");

		// Act
		string beforePrompt = BeforeThePrompt(RunDeclining(tree.Root));

		// Assert
		Assert.AreEqual(2, beforePrompt.Split("DELETE:").Length - 1, $"Expected two DELETE lines in:\n{beforePrompt}");
		Assert.Contains("Files to delete: 2", beforePrompt);
		Assert.Contains("Space to reclaim: 10 B", beforePrompt);
	}

	/// <summary>
	/// Showing the listing must not have turned the preview into the deletion: declining still
	/// leaves every file on disk.
	/// </summary>
	[TestMethod]
	public void DecliningTheConfirmationLeavesEveryFileOnDisk()
	{
		// Arrange
		using TempTree tree = new();
		List<AbsoluteFilePath> all =
		[
			tree.Write("a.txt", "alpha"),
			tree.Write("aa.txt", "alpha"),
			tree.Write("b.txt", "beta"),
			tree.Write("bb.txt", "beta"),
		];

		// Act
		string output = RunDeclining(tree.Root);

		// Assert
		Assert.Contains("Aborted.", output);

		foreach (AbsoluteFilePath file in all)
		{
			Assert.IsTrue(TempTree.Exists(file), $"{file} was deleted despite the confirmation being declined.");
		}
	}

	/// <summary>
	/// Confirming deletes exactly the copies the listing named, and no others -- the listing is a
	/// promise about what the next step does, so it has to be kept.
	/// </summary>
	[TestMethod]
	public void ConfirmingDeletesExactlyTheCopiesTheListingNamed()
	{
		// Arrange
		using TempTree tree = new();
		AbsoluteFilePath keeper = tree.Write("a.txt", "alpha");
		AbsoluteFilePath doomed = tree.Write("aa.txt", "alpha");
		AbsoluteFilePath unique = tree.Write("b.txt", "beta");

		// Act
		string output = RunConfirming(tree.Root);
		string beforePrompt = BeforeThePrompt(output);

		// Assert -- what the listing named is gone; what it did not name is not
		Assert.Contains($"DELETE: {doomed}", beforePrompt);
		Assert.IsFalse(TempTree.Exists(doomed), $"{doomed} was listed for deletion but survived.");
		Assert.IsTrue(TempTree.Exists(keeper), $"{keeper} was listed as the kept copy but was deleted.");
		Assert.IsTrue(TempTree.Exists(unique), $"{unique} has no duplicate and should never have been touched.");

		Assert.Contains("Deleted 1 file(s).", output);
		Assert.Contains("Reclaimed 5 B of disk space.", output);
	}
}
