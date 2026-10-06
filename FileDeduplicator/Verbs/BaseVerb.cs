// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator.Verbs;

using CommandLine;

using DustInTheWind.ConsoleTools.Controls.Menus;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

internal abstract class BaseVerb : ICommand
{
	/// <summary>
	/// Gets or sets the path given with <c>-p</c>, or <see langword="null"/> when none was given and the
	/// verb should ask for one. An explicit <c>-p .</c> means the current directory, so <c>"."</c>
	/// cannot double as the "not supplied" marker.
	/// </summary>
	[Option('p', "path", Required = false, HelpText = "The root path to scan for files.")]
	public string? PathString { get; set; }

	public abstract bool IsActive { get; }

	internal AbsoluteDirectoryPath Path => System.IO.Path.GetFullPath(PathString ?? ".").As<AbsoluteDirectoryPath>();

	public abstract void Run();

	internal virtual bool ValidateArgs() => true;

	public void Execute() => Run();
}

internal abstract class BaseVerb<T> : BaseVerb where T : BaseVerb<T>
{
	private bool isActive = true;
	public override bool IsActive => isActive;

	public override void Run()
	{
		// The interactive menu runs the same instance every time its item is chosen, so the path is
		// put back on every exit, early returns and exceptions included. Otherwise the next run skips
		// the prompt and works on the previous directory.
		try
		{
			if (!ValidateArgs())
			{
				return;
			}

			isActive = false;
			Run((T)this);
		}
		finally
		{
			isActive = true;
			PathString = null;
		}
	}

	internal abstract void Run(T options);
}
