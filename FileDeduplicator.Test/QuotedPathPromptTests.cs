// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator.Test;

using ktsu.FileDeduplicator.Verbs;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Tests that a path pasted at the prompt in quotes is scanned rather than resolved, quotes and all,
/// under the current directory (ktsu-dev/FileDeduplicator#152).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class QuotedPathPromptTests
{
	[TestMethod]
	[DataRow("\"{0}\"")]
	[DataRow("'{0}'")]
	[DataRow("  \"{0}\"  ")]
	public void ScanStripsOnePairOfQuotesFromAPastedPath(string format)
	{
		using TempTree tree = new();
		_ = tree.Write("q dir/a.txt", "alpha");
		_ = tree.Write("q dir/aa.txt", "alpha");
		string directory = Path.Combine(tree.Root.WeakString, "q dir");

		string output = ConsoleCapture.Run(new Scan(), string.Format(System.Globalization.CultureInfo.InvariantCulture, format, directory) + "\n", out int exitCode);

		Assert.Contains($"Scanning: {directory}", output);
		Assert.DoesNotContain("Directory not found", output);
		Assert.Contains("Found 1 group(s) of duplicate files", output);
		Assert.AreEqual(0, exitCode);
	}

	[TestMethod]
	public void StatsDryRunAndDeduplicateAcceptAQuotedPathToo()
	{
		using TempTree tree = new();
		_ = tree.Write("q dir/a.txt", "alpha");
		string directory = Path.Combine(tree.Root.WeakString, "q dir");
		string answer = $"\"{directory}\"\n";

		Assert.Contains($"Analyzing: {directory}", ConsoleCapture.Run(new Stats(), answer));
		Assert.Contains($"Dry run for: {directory}", ConsoleCapture.Run(new DryRun(), answer));
		Assert.Contains($"Deduplicating: {directory}", ConsoleCapture.Run(new Deduplicate(), answer));
	}

	[TestMethod]
	[DataRow("\"/some dir\"", "/some dir")]
	[DataRow("'/some dir'", "/some dir")]
	[DataRow("\"C:\\some dir\"", "C:\\some dir")]
	[DataRow("\"/some dir'", "\"/some dir'")]
	[DataRow("\"", "\"")]
	[DataRow("/plain", "/plain")]
	[DataRow("\"\"a\"\"", "\"a\"")]
	public void UnquoteRemovesOnlyOneMatchingPair(string input, string expected) =>
		Assert.AreEqual(expected, BaseVerb.Unquote(input));

	[TestMethod]
	public void AQuotedEmptyAnswerAbortsLikeAnEmptyOne()
	{
		string output = ConsoleCapture.Run(new Scan(), "\"\"\n", out int exitCode);

		Assert.Contains("No path provided. Aborting.", output);
		Assert.AreEqual(1, exitCode);
	}
}
