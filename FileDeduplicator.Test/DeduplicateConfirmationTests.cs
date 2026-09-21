// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator.Test;

using ktsu.FileDeduplicator.Verbs;
using ktsu.Semantics.Paths;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Tests that the Deduplicate verb shows which copies it is about to delete before it asks for
/// permission to delete them.
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
	/// <summary>
	/// Runs the Deduplicate verb over a tree, answering "n" at the confirmation prompt, and
	/// returns everything it wrote.
	/// </summary>
	/// <param name="root">The directory to deduplicate.</param>
	/// <returns>The verb's console output.</returns>
	private static string RunDeclining(AbsoluteDirectoryPath root)
	{
		TextWriter originalOut = Console.Out;
		TextReader originalIn = Console.In;

		try
		{
			using StringWriter captured = new();
			using StringReader answers = new("n");
			Console.SetOut(captured);
			Console.SetIn(answers);

			Deduplicate verb = new() { PathString = root.WeakString };
			verb.Run();

			return captured.ToString();
		}
		finally
		{
			Console.SetOut(originalOut);
			Console.SetIn(originalIn);
		}
	}

	/// <summary>
	/// Collapses runs of whitespace so the assertions describe the listing's content rather than
	/// the column its markers happen to sit in.
	/// </summary>
	/// <param name="output">The captured console output.</param>
	/// <returns>The output with each line trimmed and its internal whitespace collapsed.</returns>
	private static string Normalize(string output) =>
		string.Join('\n', output.Split('\n').Select(line => string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))));

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
		string output = Normalize(RunDeclining(tree.Root));
		int prompt = output.IndexOf("Proceed with deletion?", StringComparison.Ordinal);
		string beforePrompt = prompt < 0 ? string.Empty : output[..prompt];

		// Assert
		Assert.AreNotEqual(-1, prompt, $"The verb never reached the confirmation prompt. Output was:\n{output}");
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
		string output = Normalize(RunDeclining(tree.Root));
		string beforePrompt = output[..output.IndexOf("Proceed with deletion?", StringComparison.Ordinal)];

		// Assert
		foreach (AbsoluteFilePath file in doomed)
		{
			Assert.Contains($"DELETE: {file}", beforePrompt, $"{file} was not listed before the prompt.");
		}
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
}
