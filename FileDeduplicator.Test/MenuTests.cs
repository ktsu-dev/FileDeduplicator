// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator.Test;

using ktsu.FileDeduplicator.Verbs;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Tests the interactive menu that runs when no verb is given.
/// </summary>
/// <remarks>
/// Input is always redirected here, so these drive the numbered prompt. That prompt is also the
/// menu on Linux and macOS, where the scrolling menu crashed reading <c>Console.CursorVisible</c>
/// (ktsu-dev/FileDeduplicator#161).
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class MenuTests
{
	[TestMethod]
	public void MenuListsTheVerbsAndExitsOnZero()
	{
		string output = ConsoleCapture.Run(new Menu(), "0\n", out int exitCode);

		Assert.AreEqual(0, exitCode, output);
		Assert.Contains("Scan - ", output);
		Assert.Contains("Deduplicate - ", output);
		Assert.Contains("0. Exit", output);
		Assert.DoesNotContain("Menu", output, "The menu offered itself.");
	}

	[TestMethod]
	public void MenuExitsAtTheEndOfInput()
	{
		_ = ConsoleCapture.Run(new Menu(), string.Empty, out int exitCode);

		Assert.AreEqual(0, exitCode);
	}

	[TestMethod]
	public void MenuRunsTheChosenVerbAndOffersTheMenuAgain()
	{
		// Arrange
		using TempTree tree = new();
		_ = tree.Write("a.txt", "alpha");
		_ = tree.Write("aa.txt", "alpha");
		Type[] menuVerbs = [.. Program.Verbs.Where(t => t != typeof(Menu))];
		int scan = Array.IndexOf(menuVerbs, typeof(Scan)) + 1;
		Assert.IsGreaterThan(0, scan, "Scan is not on the menu.");

		// Act
		string output = ConsoleCapture.Run(new Menu(), $"{scan}\n{tree.Root.WeakString}\n0\n", out int exitCode);

		// Assert
		Assert.AreEqual(0, exitCode, output);
		Assert.Contains("Found 1 group(s) of duplicate files", output);
		Assert.AreEqual(2, output.Split("0. Exit").Length - 1, "The menu was not offered again after the verb ran.");
	}

	[TestMethod]
	public void MenuRejectsAChoiceThatIsNotOnIt()
	{
		string output = ConsoleCapture.Run(new Menu(), "42\n0\n", out int exitCode);

		Assert.AreEqual(0, exitCode, output);
		Assert.Contains("Not an option: 42", output);
	}
}
