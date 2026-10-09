// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator.Verbs;

using System.Diagnostics;
using System.Linq;
using System.Reflection;

using CommandLine;

using DustInTheWind.ConsoleTools.Controls;
using DustInTheWind.ConsoleTools.Controls.Menus;
using DustInTheWind.ConsoleTools.Controls.Menus.MenuItems;

[Verb("Menu", isDefault: true)]
internal sealed class Menu : BaseVerb<Menu>
{
	internal override bool ScansADirectory => false;

	internal override void Run(Menu options)
	{
		// ScrollMenu reads Console.CursorVisible, whose getter throws PlatformNotSupportedException on
		// every platform but Windows, and it needs a real keyboard to drive. Elsewhere, and whenever
		// input is piped in, the menu is a numbered prompt read a line at a time.
		if (OperatingSystem.IsWindows() && !Console.IsInputRedirected)
		{
			RunScrollMenu();
		}
		else
		{
			RunNumberedMenu();
		}
	}

	private Type[] MenuVerbs => [.. Program.Verbs.Where(verb => verb != GetType())];

	private void RunScrollMenu()
	{
		ControlRepeater menuRepeater = BuildScrollMenu(out ExitCommand exit);
		while (!exit.Requested)
		{
			menuRepeater.Display();
		}
	}

	/// <summary>
	/// Builds the scrolling menu: one item per verb, then Exit.
	/// </summary>
	/// <param name="exit">The Exit item's command, which says when the menu has been left.</param>
	/// <returns>The repeater that displays the menu until Exit is chosen.</returns>
	internal ControlRepeater BuildScrollMenu(out ExitCommand exit)
	{
		ScrollMenu scrollMenu = new()
		{
			HorizontalAlignment = HorizontalAlignment.Left,
			ItemsHorizontalAlignment = HorizontalAlignment.Left,
			KeepHighlightingOnClose = true,
		};

		ControlRepeater menuRepeater = new()
		{
			Control = scrollMenu,
		};

		exit = new(menuRepeater);
		LabelMenuItem[] menuItems = [.. MenuVerbs.Select(CreateMenuItem)];

		scrollMenu.AddItems(menuItems);
		scrollMenu.AddItems([new LabelMenuItem() { Text = "Exit", Command = exit, IsEnabled = true }]);

		return menuRepeater;
	}

	private void RunNumberedMenu()
	{
		ICommand[] verbs = [.. MenuVerbs.Select(CreateCommand)];
		string[] texts = [.. MenuVerbs.Select(DescribeVerb)];

		while (true)
		{
			Console.WriteLine();
			for (int i = 0; i < verbs.Length; i++)
			{
				Console.WriteLine($"  {i + 1}. {texts[i]}");
			}

			Console.WriteLine("  0. Exit");
			Console.Write("Choose an option: ");

			// End of input is an exit too, so a piped script cannot leave the menu waiting forever.
			string? input = Console.ReadLine()?.Trim();
			if (input is null or "0")
			{
				return;
			}

			if (int.TryParse(input, out int choice) && choice >= 1 && choice <= verbs.Length)
			{
				verbs[choice - 1].Execute();
			}
			else
			{
				Console.WriteLine($"Not an option: {input}");
			}
		}
	}

	/// <summary>
	/// Creates the command a menu entry runs: the verb, handed the menu's own <c>-p</c> path each
	/// time it is chosen.
	/// </summary>
	/// <remarks>
	/// The menu is the default verb, so <c>FileDeduplicator -p dir</c> lands here with the path set.
	/// Dropping it would ask for the path again and invite a different one into a flow that can end
	/// in deletion. It is re-applied on every run because <see cref="BaseVerb{T}.Run()"/> clears the
	/// verb's path when it finishes.
	/// </remarks>
	/// <param name="verbType">The verb to run.</param>
	/// <returns>The command that runs it.</returns>
	private PresetPathCommand CreateCommand(Type verbType)
	{
		BaseVerb? verb = Activator.CreateInstance(verbType) as BaseVerb;
		Debug.Assert(verb != null);
		return new(verb, () => PathString);
	}

	private static string DescribeVerb(Type verbType)
	{
		string name = verbType.Name;
		string? helpText = verbType.GetCustomAttribute<VerbAttribute>()?.HelpText;
		return string.IsNullOrEmpty(helpText) ? name : $"{name} - {helpText}";
	}

	private LabelMenuItem CreateMenuItem(Type verbType) => new()
	{
		Text = DescribeVerb(verbType),
		Command = CreateCommand(verbType),
		IsEnabled = true,
	};

	/// <summary>
	/// Runs a verb with the path the menu was given, or with none so that the verb asks for one.
	/// </summary>
	/// <param name="verb">The verb to run.</param>
	/// <param name="path">Reads the menu's path at the moment the verb runs.</param>
	private sealed class PresetPathCommand(BaseVerb verb, Func<string?> path) : ICommand
	{
		public bool IsActive => verb.IsActive;

		public void Execute()
		{
			verb.PathString = path();
			verb.Execute();
		}
	}

	/// <summary>
	/// The menu item that leaves the menu, which otherwise could only be left with Ctrl+C.
	/// </summary>
	/// <param name="repeater">The repeater to stop, which otherwise redisplays the menu indefinitely.</param>
	internal sealed class ExitCommand(ControlRepeater repeater) : ICommand
	{
		internal bool Requested { get; private set; }

		public bool IsActive => true;

		public void Execute()
		{
			Requested = true;
			repeater.RequestClose();
		}
	}
}
