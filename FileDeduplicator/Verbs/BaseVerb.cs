// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace ktsu.FileDeduplicator.Verbs;

using CommandLine;

using DustInTheWind.ConsoleTools.Controls.Menus;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

internal abstract class BaseVerb : ICommand
{
	[Option('p', "path", Required = false, HelpText = "The root path to scan for files.")]
	public string PathString { get; set; } = ".";

	public abstract bool IsActive { get; }

	internal AbsoluteDirectoryPath Path => System.IO.Path.GetFullPath(PathString).As<AbsoluteDirectoryPath>();

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
		if (!ValidateArgs())
		{
			return;
		}

		isActive = false;
		Run((T)this);
		isActive = true;
	}

	internal abstract void Run(T options);
}
