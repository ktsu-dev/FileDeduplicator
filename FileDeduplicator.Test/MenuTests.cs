// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator.Test;

using DustInTheWind.ConsoleTools.Controls;
using DustInTheWind.ConsoleTools.Controls.Menus;

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

	/// <summary>
	/// <c>FileDeduplicator -p dir</c> with no verb lands on the menu, and the verb chosen from it
	/// works on that directory rather than asking for a path again -- every time it is chosen.
	/// </summary>
	[TestMethod]
	public void MenuHandsItsPathToTheChosenVerb()
	{
		// Arrange
		using TempTree tree = new();
		_ = tree.Write("a.txt", "alpha");
		_ = tree.Write("aa.txt", "alpha");
		Type[] menuVerbs = [.. Program.Verbs.Where(t => t != typeof(Menu))];
		int scan = Array.IndexOf(menuVerbs, typeof(Scan)) + 1;
		Assert.IsGreaterThan(0, scan, "Scan is not on the menu.");

		// Act -- no path on stdin: the only path is the one the menu was given
		string output = ConsoleCapture.Run(new Menu { PathString = tree.Root.WeakString }, $"{scan}\n{scan}\n0\n", out int exitCode);

		// Assert
		Assert.AreEqual(0, exitCode, output);
		Assert.DoesNotContain("Enter the path", output, $"The menu dropped its -p path and asked again. Output was:\n{output}");
		Assert.AreEqual(2, output.Split($"Scanning: {tree.Root}").Length - 1, $"Scan did not work on the menu's path both times. Output was:\n{output}");
		Assert.AreEqual(2, output.Split("Found 1 group(s) of duplicate files").Length - 1, output);
	}

	[TestMethod]
	public void MenuRejectsAChoiceThatIsNotOnIt()
	{
		string output = ConsoleCapture.Run(new Menu(), "42\n0\n", out int exitCode);

		Assert.AreEqual(0, exitCode, output);
		Assert.Contains("Not an option: 42", output);
	}

	/// <summary>
	/// The scrolling menu used in an interactive Windows console has an Exit item whose command ends
	/// the display loop, so it can be left without Ctrl+C.
	/// </summary>
	[TestMethod]
	public void ScrollMenuHasAnExitItemThatClosesIt()
	{
		// Arrange
		Menu menu = new();
		ControlRepeater repeater = menu.BuildScrollMenu(out Menu.ExitCommand exit);

		// Act
		bool requestedBefore = exit.Requested;
		exit.Execute();

		// Assert
		Assert.IsInstanceOfType<ScrollMenu>(repeater.Control);
		Assert.IsTrue(exit.IsActive);
		Assert.IsFalse(requestedBefore);
		Assert.IsTrue(exit.Requested);
	}
}
