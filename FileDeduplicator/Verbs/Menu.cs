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
		BaseVerb[] verbs = [.. MenuVerbs.Select(CreateVerb)];
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

	private static BaseVerb CreateVerb(Type verbType)
	{
		BaseVerb? verb = Activator.CreateInstance(verbType) as BaseVerb;
		Debug.Assert(verb != null);
		return verb;
	}

	private static string DescribeVerb(Type verbType)
	{
		string name = verbType.Name;
		string? helpText = verbType.GetCustomAttribute<VerbAttribute>()?.HelpText;
		return string.IsNullOrEmpty(helpText) ? name : $"{name} - {helpText}";
	}

	private static LabelMenuItem CreateMenuItem(Type verbType) => new()
	{
		Text = DescribeVerb(verbType),
		Command = CreateVerb(verbType),
		IsEnabled = true,
	};

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
